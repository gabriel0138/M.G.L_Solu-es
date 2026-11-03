using System.Net;
using Xunit;

namespace MglConnect.Tests;

public class PodReceiptTests
{
    [Theory]
    [InlineData("document")]
    [InlineData("signature")]
    public async Task CompletedPodRejectsDifferentContentEvenWithTheSameReceiver(string field)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var path = $"/api/entregas/{fixture.Deliveries[0].Id}/pod";
        using var first = await admin.Client.SendAsync(admin.Api(HttpMethod.Post, path,
            new { nomeRecebedor = "Receiver", documentoRecebedor = "TEST-ONE", assinaturaBase64 = (string?)null }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var retry = await admin.Client.SendAsync(admin.Api(HttpMethod.Post, path,
            new { nomeRecebedor = "Receiver", documentoRecebedor = field == "document" ? "TEST-TWO" : "TEST-ONE", assinaturaBase64 = field == "signature" ? "different-signature" : null }));
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
    }

    [Fact]
    public async Task CustomerCannotSubmitPodEvenWhenTheDeliveryHasAlreadyBeenCompleted()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        using var customer = await fixture.LoginAsync("client-a");
        var path = $"/api/entregas/{fixture.Deliveries[0].Id}/pod";
        var pod = new { nomeRecebedor = "Receiver" };
        using var first = await admin.Client.SendAsync(admin.Api(HttpMethod.Post, path, pod));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var forbidden = await customer.Client.SendAsync(customer.Api(HttpMethod.Post, path, pod));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
