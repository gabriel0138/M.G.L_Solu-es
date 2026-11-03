using System.Net;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class AdminOrderWorkflowTests
{
    [Theory]
    [InlineData("0", "100", "1")]
    [InlineData("-1", "100", "1")]
    [InlineData("10", "-1", "1")]
    [InlineData("10", "100", "-1")]
    public async Task InvalidCargoNeverCreatesAnAdministrativeOrder(string weight, string value, string height)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Pedidos/Create");
        await using var database = new MglDbContext(fixture.Options);
        var before = await database.Pedidos.CountAsync();
        using var response = await admin.Client.PostAsync("/Pedidos/Create", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = token, ["ClienteId"] = fixture.Customers[0].Id.ToString(),
            ["Descricao"] = "Invalid cargo fixture", ["TipoCarga"] = "Geral", ["PesoKg"] = weight,
            ["ValorTotal"] = value, ["AlturaCargaMetros"] = height
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await database.Pedidos.CountAsync());
    }

    [Theory]
    [InlineData("Registrado", true)]
    [InlineData("EmProcessamento", true)]
    [InlineData("Cancelado", false)]
    [InlineData("Concluido", false)]
    [InlineData("ComEntregaAtribuida", false)]
    [InlineData("EstadoDesconhecido", false)]
    public async Task OrderDetailsOffersDispatchOnlyForAnEligibleOrder(string status, bool expected)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var order = new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id,
            NumeroPedido = "ADMIN-DETAIL", Descricao = "Cargo fixture", Status = status };
        await fixture.ChangeAsync(db => db.Pedidos.Add(order));
        using var admin = await fixture.LoginAsync("admin-a");
        using var response = await admin.Client.GetAsync($"/Pedidos/Details/{order.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(expected, html.Contains($"/Entregas/Create?pedidoId={order.Id}", StringComparison.Ordinal));
    }
}
