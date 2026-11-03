using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MglConnect.Web.Middleware;
using Xunit;
namespace MglConnect.Tests;

public sealed class OperationalResilienceTests
{
    [Fact]
    public async Task DatabaseFailureHasSafeRetryableResponseAndStructuredCorrelationWithoutSecretExceptionDetails()
    {
        var logger=new Capture();
        var middleware=new OperationalDiagnosticsMiddleware(_=>throw new SqliteException("SECRET-TOKEN GPS=-23,-46 Password=secret",5),logger);
        var context=new DefaultHttpContext();context.Response.Body=new MemoryStream();context.Request.Headers["X-Correlation-ID"]="untrusted\r\nheader";
        await middleware.InvokeAsync(context);context.Response.Body.Position=0;
        var body=await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(503,context.Response.StatusCode);Assert.Equal("5",context.Response.Headers.RetryAfter);
        Assert.DoesNotContain("SECRET",body);Assert.DoesNotContain("GPS=",body);Assert.DoesNotContain("secret",string.Join('|',logger.Messages));
        Assert.Matches("CorrelationId=[a-f0-9]{32}",string.Join("|",logger.Messages));
        Assert.DoesNotContain("untrusted",body);Assert.Contains("correlationId",body);Assert.Contains(logger.Messages,s=>s.Contains("SqliteException"));
    }
    [Fact]
    public async Task HealthIsAdministrativePrivateAndOrdinaryResponsesHaveServerGeneratedCorrelation()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();
        using var admin=await f.LoginAsync("admin-a");using var customer=await f.LoginAsync("client-a");using var driver=await f.LoginAsync("driver-a");
        using(var healthy=await admin.Client.GetAsync("/operacional/saude"))
        {Assert.Equal(HttpStatusCode.OK,healthy.StatusCode);Assert.True(healthy.Headers.CacheControl?.NoStore);Assert.Matches("^[a-f0-9]{32}$",healthy.Headers.GetValues("X-Correlation-ID").Single());Assert.DoesNotContain("Data Source",await healthy.Content.ReadAsStringAsync());}
        foreach(var actor in new[]{customer,driver})
        {using var denied=await actor.Client.GetAsync("/operacional/saude");Assert.Equal(HttpStatusCode.Redirect,denied.StatusCode);Assert.Contains("AccessDenied",denied.Headers.Location!.ToString());}
    }
    private sealed class Capture:ILogger<OperationalDiagnosticsMiddleware>
    {
        public List<string> Messages {get;}=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> format)=>Messages.Add(format(state,exception));
    }
}
