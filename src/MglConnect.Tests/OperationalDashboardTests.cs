using System.Net;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class OperationalDashboardTests
{
    [Fact]
    public async Task DashboardFragmentUpdatesFromDatabaseAndKeepsTenantAndPeriodIsolation()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        using var first = await admin.Client.GetAsync("/Dashboard/Painel");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("data-metric=\"total-pedidos\">2</strong>", await first.Content.ReadAsStringAsync());
        Assert.True(first.Headers.CacheControl?.NoStore);
        await fixture.ChangeAsync(db =>
        {
            var order = db.Pedidos.Single(p => p.Id == fixture.Deliveries[0].PedidoId);
            order.DataPedido = new DateTime(2026, 10, 10, 23, 59, 59, DateTimeKind.Utc).AddTicks(9999999);
            db.Pedidos.Single(p => p.Id == fixture.Deliveries[1].PedidoId).DataPedido = new DateTime(2026, 10, 11);
            db.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id).DataPrevisaoEntrega = DateTime.UtcNow.AddDays(-1);
            db.Entregas.Single(e => e.Id == fixture.Deliveries[2].Id).DestinatarioNome = "FOREIGN-PRIVATE-DELIVERY";
        });
        using var updated = await admin.Client.GetAsync("/Dashboard/Painel");
        var html = await updated.Content.ReadAsStringAsync();
        Assert.Contains("data-metric=\"atrasadas\">1</strong>", html);
        Assert.DoesNotContain("FOREIGN-PRIVATE-DELIVERY", html);
        Assert.DoesNotContain(fixture.Deliveries[2].CodigoRastreio, html);
        using var filtered = await admin.Client.GetAsync("/Dashboard/Painel?de=2026-10-10&ate=2026-10-10");
        Assert.Contains("data-metric=\"total-pedidos\">1</strong>", await filtered.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("driver-a")]
    public async Task NonAdministrativeProfilesCannotReadOperationalFragment(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var session = await fixture.LoginAsync(login);
        using var response = await session.Client.GetAsync("/Dashboard/Painel");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("de=2026-10-20&ate=2026-10-01")]
    [InlineData("de=invalid")]
    public async Task InvalidPeriodIsRejectedWithoutSilentlyShowingUnfilteredData(string query)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        using var response = await admin.Client.GetAsync("/Dashboard/Painel?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DashboardCountsOnlyRegisteredUndispatchedOrdersAndDoesNotInventFinancialMetrics()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db => db.Pedidos.AddRange(
            new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "QUEUE-OPEN", Descricao = "Authorized queued order", Status = "Registrado", ValorTotal = 123m },
            new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "QUEUE-CANCELLED", Descricao = "Cancelled order must not be dispatched", Status = "Cancelado" }));
        using var admin = await fixture.LoginAsync("admin-a");
        using var response = await admin.Client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("1 pendente(s) de despacho", html);
        Assert.Contains("Authorized queued order", html);
        Assert.DoesNotContain("Cancelled order must not be dispatched", html);
        Assert.DoesNotContain("Receita Total", html);
        Assert.DoesNotContain("Margem Líquida", html);
        Assert.DoesNotContain("estimativa anual", html);
        Assert.Contains("Sem despesas registradas", html);
    }
}
