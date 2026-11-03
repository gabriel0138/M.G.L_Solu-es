using System.Threading.RateLimiting;
using MglConnect.Web.Services;

namespace MglConnect.Web.Middleware;

public sealed class PublicTrackingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PublicTrackingRateLimiter limiter)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/Rastreio", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/api/rastreio", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/trackingHub", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        using var lease = limiter.Acquire(context);
        if (lease.IsAcquired)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new { erro = "Muitas consultas. Aguarde antes de tentar novamente." });
    }
}
