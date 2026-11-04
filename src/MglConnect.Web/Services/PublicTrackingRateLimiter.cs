using System.Threading.RateLimiting;

namespace MglConnect.Web.Services;

// The per-IP budget is shared by page/API requests and public hub joins. It is
// local to this application instance; a cluster needs shared edge rate limiting.
public sealed class PublicTrackingRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public PublicTrackingRateLimiter(IConfiguration configuration)
    {
        var limit = configuration.GetValue("PublicTracking:PermitLimit", 30);
        var seconds = configuration.GetValue("PublicTracking:WindowSeconds", 60);
        if (limit <= 0 || seconds <= 0)
            throw new InvalidOperationException("Public tracking limits must be positive.");
        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit, Window = TimeSpan.FromSeconds(seconds),
                QueueLimit = 0, AutoReplenishment = true
            }));
    }

    public RateLimitLease Acquire(HttpContext? context) =>
        _limiter.AttemptAcquire(context?.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    public void Dispose() => _limiter.Dispose();
}
