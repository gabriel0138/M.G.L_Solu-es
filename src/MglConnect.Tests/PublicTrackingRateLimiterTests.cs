using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public sealed class PublicTrackingRateLimiterTests
{
    [Fact]
    public void RepeatedLegacyProbesAreLimitedPerAddressWithoutBlockingOtherAddresses()
    {
        using var limiter = new PublicTrackingRateLimiter(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicTracking:PermitLimit"] = "2",
                ["PublicTracking:WindowSeconds"] = "60"
            })
            .Build());

        using var first = limiter.Acquire(Context("192.0.2.1"));
        using var second = limiter.Acquire(Context("192.0.2.1"));
        using var third = limiter.Acquire(Context("192.0.2.1"));
        using var otherAddress = limiter.Acquire(Context("192.0.2.2"));

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
        Assert.True(third.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(otherAddress.IsAcquired);
    }

    private static DefaultHttpContext Context(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }
}
