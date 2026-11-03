using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MglConnect.Tests;

public class DriverOfflineContextTests
{
    [Fact]
    public async Task MixedCookieAndBearerIdentitiesCannotMintAnOfflineContext()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var first = await fixture.LoginAsync("driver-a");
        using var second = await fixture.LoginAsync("driver-b");
        using var response = await first.Client.SendAsync(second.Api(HttpMethod.Get, "/Motorista/Offline/Contexto"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("token", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DriverContextUsesCurrentActorAndItsTokenCanConfirmOnlyItsOwnPod()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var driver = await fixture.LoginAsync("driver-a");
        using var response = await driver.Client.GetAsync("/Motorista/Offline/Contexto");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(fixture.Users["driver-a"].Id.ToString(), json.RootElement.GetProperty("userId").GetString());
        Assert.Equal(fixture.TenantA.ToString(), json.RootElement.GetProperty("tenantId").GetString());
        var refreshed = driver with { Token = json.RootElement.GetProperty("token").GetString()! };
        using var foreign = await driver.Client.SendAsync(refreshed.Api(HttpMethod.Post, $"/api/entregas/{fixture.Deliveries[1].Id}/pod", new { nomeRecebedor = "Receiver" }));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        var path = $"/api/entregas/{fixture.Deliveries[0].Id}/pod";
        foreach (var attempt in new[] { 1, 2 })
        {
            using var completed = await driver.Client.SendAsync(refreshed.Api(HttpMethod.Post, path, new { nomeRecebedor = "Receiver" }));
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            using var receipt = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
            Assert.True(receipt.RootElement.GetProperty("sucesso").GetBoolean());
            Assert.Equal(fixture.Deliveries[0].Id, receipt.RootElement.GetProperty("entregaId").GetInt32());
            Assert.Equal("Entregue", receipt.RootElement.GetProperty("status").GetString());
        }
        await fixture.ChangeAsync(db => db.Usuarios.Single(u => u.Id == fixture.Users["driver-a"].Id).Ativo = false);
        using var revoked = await driver.Client.GetAsync("/Motorista/Offline/Contexto");
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);
        Assert.Contains("Login", revoked.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("admin-a")]
    public async Task OtherRolesCannotMintDriverOfflineCredentials(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var session = await fixture.LoginAsync(login);
        using var response = await session.Client.GetAsync("/Motorista/Offline/Contexto");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
    }
}
