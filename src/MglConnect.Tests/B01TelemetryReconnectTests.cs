using System.Net;
using Xunit;

namespace MglConnect.Tests;

public class B01TelemetryReconnectTests
{
    [Theory]
    [InlineData("role", false)]
    [InlineData("role", true)]
    [InlineData("tenant", false)]
    [InlineData("tenant", true)]
    public async Task ReconnectRejectsStaleIdentityAndNewLoginUsesCurrentPermissions(string change, bool useBearer)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var original = await fixture.LoginAsync("client-a");
        await using (var first = await SecurityHubConnection.ConnectAsync(fixture, original, useBearer: useBearer))
            Assert.False((await first.InvokeAsync("JoinLiveTracking", fixture.Deliveries[0].CodigoRastreio)).TryGetProperty("error", out _));

        await fixture.ChangeAsync(db =>
        {
            var user = db.Usuarios.Single(u => u.Id == fixture.Users["client-a"].Id);
            if (change == "role") user.Perfil = "Intermediador";
            else
            {
                user.TenantId = fixture.TenantB;
                user.ClienteId = fixture.Customers[2].Id;
            }
        });

        using var negotiate = useBearer
            ? original.Api(HttpMethod.Post, "/telemetryHub/negotiate?negotiateVersion=1")
            : new HttpRequestMessage(HttpMethod.Post, "/telemetryHub/negotiate?negotiateVersion=1");
        using var rejected = await original.Client.SendAsync(negotiate);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        using var renewed = await fixture.LoginAsync("client-a");
        await using var reconnected = await SecurityHubConnection.ConnectAsync(fixture, renewed, useBearer: useBearer);
        var permitted = change == "role" ? 0 : 2;
        for (var i = 0; i < fixture.Deliveries.Count; i++)
        {
            var result = await reconnected.InvokeAsync("JoinLiveTracking", fixture.Deliveries[i].CodigoRastreio);
            Assert.Equal(i != permitted, result.TryGetProperty("error", out _));
        }
    }
}
