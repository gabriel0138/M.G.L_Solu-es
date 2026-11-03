using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class OrderListUiTests
{
    [Fact]
    public async Task CancelledOrderKeepsItsActualStateAndHasNoDispatchAction()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db => db.Pedidos.Add(new Pedido
        {
            TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id,
            NumeroPedido = "UI-CANCELLED", Descricao = "Cancelled cargo", Status = "Cancelado",
            PesoKg = 1250.5m, ValorTotal = 98765.43m
        }));
        using var admin = await fixture.LoginAsync("admin-a");
        var html = await PageAsync(admin.Client, "/Pedidos?status=Cancelado");
        var row = Row(html, "UI-CANCELLED");

        Assert.Matches("data-status=\"cancelled\"[^>]*>\\s*Cancelado\\s*</span>", row);
        Assert.DoesNotContain("Despachar", row);
        Assert.DoesNotContain("Registrado", row);
        var text = WebUtility.HtmlDecode(row);
        Assert.Contains("1.250,5 kg", text);
        Assert.Contains("98.765,43", text);
        Assert.Contains("Valor da mercadoria", html);
    }

    [Fact]
    public async Task CancelledDeliveryKeepsItsStateAndLinksToItsPrivateDetails()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        await fixture.ChangeAsync(db => db.Entregas.Single(item => item.Id == delivery.Id).StatusEntrega = "Cancelada");
        using var admin = await fixture.LoginAsync("admin-a");
        var html = await PageAsync(admin.Client, "/Entregas?status=Cancelada");
        var row = Row(html, delivery.CodigoRastreio);

        Assert.Matches("data-status=\"cancelled\"[^>]*>\\s*Cancelada\\s*</span>", row);
        Assert.Contains($"href=\"/Entregas/Details/{delivery.Id}\"", row);
        Assert.DoesNotContain("Pendente", row);
        Assert.DoesNotContain(fixture.Deliveries[1].CodigoRastreio, html);
        Assert.DoesNotContain(fixture.Deliveries[2].CodigoRastreio, html);
    }

    [Theory]
    [InlineData("Registrado", true)]
    [InlineData("EmProcessamento", true)]
    [InlineData("ComEntregaAtribuida", false)]
    [InlineData("Concluido", false)]
    [InlineData("EstadoDesconhecido", false)]
    public async Task DispatchActionRequiresAnEligibleOrderWithoutADelivery(string status, bool expectedDispatch)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db => db.Pedidos.Add(new Pedido
        {
            TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id,
            NumeroPedido = "UI-DISPATCH", Descricao = "Carga de teste", Status = status,
            OrigemCidade = "Campinas", OrigemEstado = "SP", DestinoCidade = "Santos", DestinoEstado = "SP"
        }));
        using var admin = await fixture.LoginAsync("admin-a");
        var row = Row(await PageAsync(admin.Client, "/Pedidos"), "UI-DISPATCH");

        Assert.Equal(expectedDispatch, row.Contains("Despachar", StringComparison.Ordinal));
        Assert.Contains("Campinas/SP", row);
        Assert.Contains("Santos/SP", row);
    }

    [Fact]
    public async Task OrderWithAnExistingDeliveryLinksToPrivateDetailsInsteadOfDispatchingAgain()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        using var admin = await fixture.LoginAsync("admin-a");
        var row = Row(await PageAsync(admin.Client, "/Pedidos"), delivery.CodigoRastreio);

        Assert.Contains($"href=\"/Entregas/Details/{delivery.Id}\"", row);
        Assert.DoesNotContain("Despachar", row);
    }

    [Theory]
    [InlineData("ui-match-101")]
    [InlineData("orbita")]
    [InlineData("ui-acme")]
    public async Task OrderSearchAndStatusFilterApplyTogetherInsideTheCurrentTenant(string term)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db =>
        {
            db.Clientes.Single(item => item.Id == fixture.Customers[0].Id).RazaoSocial = "UI-ACME";
            db.Clientes.Single(item => item.Id == fixture.Customers[2].Id).RazaoSocial = "UI-ACME FOREIGN";
            db.Pedidos.AddRange(
                new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "UI-MATCH-101", Descricao = "Carga especial ORBITA", Status = "EmProcessamento" },
                new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "UI-OTHER-102", Descricao = "Carga especial ORBITA", Status = "Registrado" },
                new Pedido { TenantId = fixture.TenantB, ClienteId = fixture.Customers[2].Id, NumeroPedido = "UI-FOREIGN-103", Descricao = "Carga especial ORBITA", Status = "EmProcessamento" });
        });
        using var admin = await fixture.LoginAsync("admin-a");
        var html = await PageAsync(admin.Client, $"/Pedidos?termo={Uri.EscapeDataString(term)}&status=EmProcessamento");

        Assert.Contains("UI-MATCH-101", html);
        Assert.DoesNotContain("UI-OTHER-102", html);
        Assert.DoesNotContain("UI-FOREIGN-103", html);
        Assert.DoesNotContain("UI-ACME FOREIGN", html);
        Assert.Matches("data-status=\"pending\"[^>]*>\\s*Em processamento\\s*</span>", Row(html, "UI-MATCH-101"));
        Assert.Contains($"value=\"{term}\"", html);
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    public async Task CustomerProfilesCannotReadAdministrativeOrderOrDeliveryLists(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var actor = await fixture.LoginAsync(login);
        foreach (var path in new[] { "/Pedidos", "/Entregas" })
        {
            using var response = await actor.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    private static async Task<string> PageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Row(string html, string content) => Assert.Single(Regex
        .Matches(html, "<tr\\b[^>]*>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
        .Cast<Match>()
        .Select(match => match.Value),
        row => row.Contains(content, StringComparison.Ordinal));
}
