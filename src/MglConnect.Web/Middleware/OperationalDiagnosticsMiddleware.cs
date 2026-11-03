using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace MglConnect.Web.Middleware;

public sealed class OperationalDiagnosticsMiddleware(RequestDelegate next,ILogger<OperationalDiagnosticsMiddleware> logger)
{
    public static readonly Meter Meter=new("MglConnect.Operational","1.0");
    private static readonly Counter<long> Failures=Meter.CreateCounter<long>("mgl.database.failures");
    private static readonly Histogram<double> Duration=Meter.CreateHistogram<double>("mgl.http.duration","ms");
    public async Task InvokeAsync(HttpContext context)
    {
        var id=Activity.Current?.TraceId.ToHexString()??Guid.NewGuid().ToString("N");
        context.Items["Mgl.CorrelationId"]=id;
        using var scope=logger.BeginScope(new Dictionary<string,object>{{"CorrelationId",id}});
        context.Response.OnStarting(()=>{context.Response.Headers["X-Correlation-ID"]=id;return Task.CompletedTask;});
        var watch=Stopwatch.StartNew();
        try { await next(context); }
        catch(Exception ex) when(ex is DbException or DbUpdateException)
        {
            Failures.Add(1);
            logger.LogError(new EventId(6001,"DatabaseUnavailable"),"Banco não confirmou a operação. CorrelationId={CorrelationId}; ErrorType={ErrorType}",id,ex.GetType().Name);
            if(context.Response.HasStarted){context.Abort();return;}
            if(context.Features.Get<IStatusCodePagesFeature>() is {} pages)pages.Enabled=false;
            context.Response.Clear();context.Response.StatusCode=503;
            context.Response.Headers.RetryAfter="5";
            // Same safe response for MVC, API and hub negotiation; never reflect SQL or request data.
            await context.Response.WriteAsJsonAsync(new {erro="Serviço temporariamente indisponível. Nenhuma operação deve ser considerada confirmada sem recibo. Tente novamente.",correlationId=id},context.RequestAborted);
        }
        finally
        {
            Duration.Record(watch.Elapsed.TotalMilliseconds,new KeyValuePair<string,object?>("status",context.Response.StatusCode/100+"xx"));
            if(context.Response.StatusCode>=500)logger.LogWarning(new EventId(6002,"RequestFailed"),"Requisição encerrada com HTTP {Status}; duração {ElapsedMs} ms; CorrelationId={CorrelationId}",context.Response.StatusCode,watch.ElapsedMilliseconds,id);
        }
    }
}
