using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class PortalCommandCenterTests
{
    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    public async Task CancelledOrderKeepsItsStateInBothCustomerViewsAndLinksToPrivateDetails(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        await fixture.ChangeAsync(db =>
        {
            var order = db.Pedidos.Single(item => item.Id == delivery.PedidoId);
            order.Status = "Cancelado";
            order.PesoKg = 1250.5m;
            order.ValorTotal = 98765.43m;
        });
        using var customer = await fixture.LoginAsync(login);

        foreach (var path in new[] { "/Cliente/Pedidos", "/Cliente/Painel" })
        {
            var html = await PageAsync(customer.Client, path);
            var row = Row(html, "B01-0");
            Assert.Matches("data-status=\"cancelled\"[^>]*>\\s*Cancelado\\s*</span>", row);
            Assert.DoesNotContain("Em transporte", row);
            Assert.Contains($"href=\"/Cliente/Pedido/{delivery.PedidoId}\"", row);
            Assert.Contains("1.250,5 kg", WebUtility.HtmlDecode(row));
            Assert.Contains("98.765,43", WebUtility.HtmlDecode(row));
            Assert.DoesNotContain("Private B01 cargo 1", html);
            Assert.DoesNotContain("Private B01 cargo 2", html);
            Assert.DoesNotContain("href=\"/Pedidos", html);
            Assert.DoesNotContain("href=\"/Entregas", html);
        }
    }

    [Fact]
    public async Task CancelledDeliveryAndUnassignedOrderPreserveTheirActualStates()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db =>
        {
            db.Entregas.Single(item => item.Id == fixture.Deliveries[0].Id).StatusEntrega = "Cancelada";
            db.Pedidos.Add(new Pedido
            {
                TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id,
                NumeroPedido = "PORTAL-REVIEW", Descricao = "Awaiting operational validation", Status = "EmProcessamento"
            });
        });
        using var customer = await fixture.LoginAsync("client-a");
        foreach (var path in new[] { "/Cliente/Pedidos", "/Cliente/Painel" })
        {
            var html = await PageAsync(customer.Client, path);
            Assert.Matches("data-status=\"cancelled\"[^>]*>\\s*Cancelada\\s*</span>", Row(html, "B01-0"));
            var unassigned = Row(html, "PORTAL-REVIEW");
            Assert.Matches("data-status=\"pending\"[^>]*>\\s*Em processamento\\s*</span>", unassigned);
            Assert.Contains("Disponível após o despacho", WebUtility.HtmlDecode(unassigned));
            Assert.DoesNotContain("data-tracking-code", unassigned);
        }
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    public async Task SearchAndStatusFiltersApplyTogetherWithinTheBoundCustomerAndTenant(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db => db.Pedidos.AddRange(
            new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "PORTAL-MATCH", Descricao = "Carga ORBITA", Status = "Cancelado" },
            new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "PORTAL-ACTIVE", Descricao = "Carga ORBITA", Status = "Registrado" },
            new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[1].Id, NumeroPedido = "PORTAL-OTHER-CUSTOMER", Descricao = "Carga ORBITA", Status = "Cancelado" },
            new Pedido { TenantId = fixture.TenantB, ClienteId = fixture.Customers[2].Id, NumeroPedido = "PORTAL-OTHER-TENANT", Descricao = "Carga ORBITA", Status = "Cancelado" }));
        using var customer = await fixture.LoginAsync(login);
        var html = await PageAsync(customer.Client, "/Cliente/Pedidos?termo=orbita&status=Cancelado");

        Assert.Contains("PORTAL-MATCH", html);
        Assert.DoesNotContain("PORTAL-ACTIVE", html);
        Assert.DoesNotContain("PORTAL-OTHER-CUSTOMER", html);
        Assert.DoesNotContain("PORTAL-OTHER-TENANT", html);
        Assert.Contains("value=\"orbita\"", html);
        Assert.Matches("<option\\b[^>]*value=\"Cancelado\"[^>]*selected", html);
        Assert.Contains("1 pedido encontrado", WebUtility.HtmlDecode(html));
        Assert.Contains("Limpar filtros", html);
    }

    [Fact]
    public async Task TrackingCodeAndCargoAreEncodedOutsideTheCopyHandlersJavaScript()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        const string trackingCode = "X');alert(1)//";
        await fixture.ChangeAsync(db =>
        {
            db.Entregas.Single(item => item.Id == fixture.Deliveries[0].Id).CodigoRastreio = trackingCode;
            db.Pedidos.Single(item => item.Id == fixture.Deliveries[0].PedidoId).Descricao = "<img src=x onerror=x>";
        });
        using var customer = await fixture.LoginAsync("client-a");

        foreach (var path in new[] { "/Cliente/Pedidos", "/Cliente/Painel" })
        {
            var html = await PageAsync(customer.Client, path);
            var row = Row(html, "B01-0");
            Assert.DoesNotContain("<img src=x", row);
            Assert.Contains("&lt;img", row);
            var copyButton = Assert.Single(Regex.Matches(row, "<button\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>());
            var code = Regex.Match(copyButton.Value, "data-tracking-code=\"([^\"]*)\"");
            Assert.True(code.Success);
            Assert.Equal(trackingCode, WebUtility.HtmlDecode(code.Groups[1].Value));
            Assert.Contains("onclick=\"copiarCodigoRastreio(this.dataset.trackingCode, this)\"", copyButton.Value);
            Assert.DoesNotContain("copiarCodigoRastreio('X", row);
        }
    }

    [Fact]
    public async Task EmptyFilteredResultOffersAWorkingResetWhileKeepingExistingOrders()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var customer = await fixture.LoginAsync("client-a");
        var html = await PageAsync(customer.Client, "/Cliente/Pedidos?termo=NO-MATCHING-ORDER&status=Cancelado");

        Assert.Contains("Nenhum pedido corresponde aos filtros", html);
        Assert.Contains("href=\"/Cliente/Pedidos\"", html);
        Assert.DoesNotContain("Private B01 cargo 0", html);
        Assert.DoesNotContain("Você ainda não tem pedidos", WebUtility.HtmlDecode(html));
        var reset = await PageAsync(customer.Client, "/Cliente/Pedidos");
        Assert.Contains("Private B01 cargo 0", reset);
    }

    [Fact]
    public async Task UndispatchedOrderShowsActualCargoAddressesAndStateWithoutInventedTransport()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var order = new Pedido
        {
            TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id,
            NumeroPedido = "PORTAL-NO-DELIVERY", Descricao = "Mercadoria <privada>", Status = "EmProcessamento",
            PesoKg = 125.5m, ValorTotal = 5432.10m, OrigemEndereco = "Rua Coleta Real", OrigemNumero = "10",
            OrigemCidade = "Campinas", OrigemEstado = "SP", DestinoEndereco = "Rua Destino Real",
            DestinoNumero = "20", DestinoCidade = "Santos", DestinoEstado = "SP"
        };
        await fixture.ChangeAsync(db => db.Pedidos.Add(order));
        using var customer = await fixture.LoginAsync("client-a");
        var html = await PageAsync(customer.Client, $"/Cliente/Pedido/{order.Id}");
        var text = WebUtility.HtmlDecode(html);

        Assert.Contains("Mercadoria &lt;privada&gt;", html);
        Assert.DoesNotContain("Mercadoria <privada>", html);
        Assert.Contains("Rua Coleta Real, 10", text);
        Assert.Contains("Rua Destino Real, 20", text);
        Assert.Contains("125,5 kg", text);
        Assert.Contains("5.432,10", text);
        Assert.Contains("Não atribuído", text);
        Assert.Contains("data-has-delivery=\"false\"", html);
        Assert.Matches("data-status=\"pending\"[^>]*>\\s*Em processamento\\s*</span>", html);
        foreach (var invented in new[] { "Caminhão Pesado de Frota", "Validação Biométrica", "Bipado e Triado", "Última Milha", "Sincronizado ao Vivo" })
            Assert.DoesNotContain(invented, text);
        Assert.DoesNotContain("Abrir rastreamento", text);
        Assert.DoesNotContain("/pod-pdf", html);
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    public async Task PodLinkRequiresCompletedDeliveryAndReceiverAndRemainsPrivateToItsCustomer(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        var detailsPath = $"/Cliente/Pedido/{delivery.PedidoId}";
        var pdfPath = $"/Cliente/Pedido/{delivery.PedidoId}/Comprovante";
        using var customer = await fixture.LoginAsync(login);
        using var otherCustomer = await fixture.LoginAsync("client-a-other");
        await fixture.ChangeAsync(db => db.Entregas.Single(item => item.Id == delivery.Id).NomeRecebedor = "Private Receiver");
        Assert.DoesNotContain(pdfPath, await PageAsync(customer.Client, detailsPath));
        await fixture.ChangeAsync(db =>
        {
            var item = db.Entregas.Single(item => item.Id == delivery.Id);
            item.StatusEntrega = "Entregue";
            item.NomeRecebedor = null;
        });
        Assert.DoesNotContain(pdfPath, await PageAsync(customer.Client, detailsPath));
        await fixture.ChangeAsync(db =>
        {
            var item = db.Entregas.Single(item => item.Id == delivery.Id);
            item.NomeRecebedor = "Private Receiver";
            item.DataHoraRecebimento = DateTime.UtcNow;
        });
        var completed = await PageAsync(customer.Client, detailsPath);
        Assert.Contains($"href=\"{pdfPath}\"", completed);
        Assert.Contains("Private Receiver", completed);
        Assert.DoesNotContain("Documento do recebedor", completed);
        Assert.DoesNotContain("Assinatura coletada no recebimento", completed);
        using var pdf = await customer.Client.GetAsync(pdfPath);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        using var deniedPage = await otherCustomer.Client.GetAsync(detailsPath);
        Assert.Equal(HttpStatusCode.NotFound, deniedPage.StatusCode);
        Assert.True(pdf.Headers.CacheControl?.NoStore);
        using var deniedPdf = await otherCustomer.Client.GetAsync(pdfPath);
        Assert.Equal(HttpStatusCode.NotFound, deniedPdf.StatusCode);
        using var foreignCustomer = await fixture.LoginAsync("client-b");
        using var foreignPdf = await foreignCustomer.Client.GetAsync(pdfPath);
        Assert.Equal(HttpStatusCode.NotFound, foreignPdf.StatusCode);
        using var anonymous = fixture.Anonymous();
        using var anonymousPdf = await anonymous.GetAsync(pdfPath);
        Assert.Equal(HttpStatusCode.Redirect, anonymousPdf.StatusCode);
        Assert.Contains("Login", anonymousPdf.Headers.Location!.ToString());
    }

    [Fact]
    public async Task NewOrderPreservesAntiforgerySubmissionKeyAndRejectsInvalidRequiredFieldsAndRanges()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var customer = await fixture.LoginAsync("client-a");
        var html = await PageAsync(customer.Client, "/Cliente/Pedido/Novo");
        var form = Regex.Match(html, "<form\\b[^>]*id=\"clientOrderForm\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(form));
        Assert.Contains("method=\"post\"", form);
        Assert.Contains("action=\"/Cliente/Pedido/Novo\"", form);
        var token = Attribute(Field(form, "__RequestVerificationToken"), "value");
        var submission = Attribute(Field(form, "ChaveEnvio"), "value");
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(Guid.TryParse(submission, out _));
        foreach (var name in new[] { "Descricao", "OrigemEndereco", "OrigemNumero", "OrigemCidade", "OrigemEstado", "DestinoEndereco", "DestinoNumero", "DestinoCidade", "DestinoEstado" })
        {
            Assert.Matches("\\brequired(?:\\s|=|>)", Field(form, name));
            Assert.Contains($"for=\"{name}\"", form);
        }
        Assert.Equal("0.01", Attribute(Field(form, "PesoKg"), "min"));
        Assert.Equal("1000000", Attribute(Field(form, "PesoKg"), "max"));
        Assert.Equal("0", Attribute(Field(form, "ValorTotal"), "min"));
        Assert.Equal("20", Attribute(Field(form, "AlturaCargaMetros"), "max"));
        Assert.DoesNotContain("name=\"ClienteId\"", form);
        Assert.DoesNotContain("name=\"TenantId\"", form);
        await using var database = new MglDbContext(fixture.Options);
        var originalCount = await database.Pedidos.CountAsync();
        using var invalid = await customer.Client.PostAsync("/Cliente/Pedido/Novo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token!, ["ChaveEnvio"] = submission!,
            ["Descricao"] = "", ["TipoCarga"] = "Carga Geral Seca", ["PesoKg"] = "0", ["ValorTotal"] = "-1", ["AlturaCargaMetros"] = "21"
        }));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var rejected = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", rejected);
        foreach (var name in new[] { "Descricao", "PesoKg", "ValorTotal", "AlturaCargaMetros", "OrigemEndereco", "DestinoEndereco" })
        {
            var error = Regex.Match(rejected, $"<span\\b(?=[^>]*data-valmsg-for=\"{name}\")[^>]*>").Value;
            Assert.Contains("field-validation-error", error);
        }
        Assert.Equal(originalCount, await database.Pedidos.CountAsync());
    }

    private static string Field(string html, string name) => Assert.Single(Regex
        .Matches(html, "<(?:input|textarea|select)\\b[^>]*>", RegexOptions.IgnoreCase)
        .Cast<Match>().Select(match => match.Value), field => Attribute(field, "name") == name);

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"(?:^|\\s){Regex.Escape(name)}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
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
        .Select(match => match.Value), row => row.Contains(content, StringComparison.Ordinal));
}
