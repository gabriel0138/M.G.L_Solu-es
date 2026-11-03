using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MglConnect.Tests;

public class ClientDashboardRefreshTests
{
    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    public async Task LivePanelShowsOnlyBoundCustomerAndReflectsCommittedDeliveryChanges(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var customer = await fixture.LoginAsync(login);
        using var first = await customer.Client.GetAsync("/Cliente/Painel");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        var before = await first.Content.ReadAsStringAsync();
        Assert.Contains("Private B01 cargo 0", before);
        Assert.DoesNotContain("Private B01 cargo 1", before);
        Assert.DoesNotContain("Private B01 cargo 2", before);
        Assert.Contains("data-client-metric=\"transito\">1</strong>", before);
        Assert.Contains("data-client-metric=\"entregues\">0</strong>", before);
        await fixture.ChangeAsync(db =>
        {
            db.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id).StatusEntrega = "Entregue";
            db.Pedidos.Single(p => p.Id == fixture.Deliveries[0].PedidoId).Descricao = "<img src=x onerror=alert(1)>";
        });
        using var second = await customer.Client.GetAsync("/Cliente/Painel");
        var after = await second.Content.ReadAsStringAsync();
        Assert.Contains("data-client-metric=\"transito\">0</strong>", after);
        Assert.Contains("data-client-metric=\"entregues\">1</strong>", after);
        Assert.DoesNotContain("<img src=x", after);
        Assert.Contains("&lt;img", after);
        await fixture.ChangeAsync(db => db.Clientes.Single(c => c.Id == fixture.Customers[0].Id).Ativo = false);
        using var denied = await customer.Client.GetAsync("/Cliente/Painel");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("AccessDenied", denied.Headers.Location!.ToString());
    }
}
