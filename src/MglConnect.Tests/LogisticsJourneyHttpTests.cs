using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class LogisticsJourneyHttpTests
{
    [Fact]
    public async Task ClientOrderDispatchDriverShiftAndPodConvergeAcrossAllProfilesWithRetries()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var vehicle = new Veiculo { TenantId = fixture.TenantA, Placa = "FLOW001", Modelo = "Journey test", CapacidadeCargaKg = 1000m };
        var newDriver = new Usuario
        {
            TenantId = fixture.TenantA, Login = "workflow-driver", Nome = "Journey driver", Perfil = "Motorista",
            SenhaHash = fixture.Users["driver-a"].SenhaHash
        };
        await fixture.ChangeAsync(db => db.AddRange(vehicle, newDriver));
        using var customer = await fixture.LoginAsync("client-a");
        using var admin = await fixture.LoginAsync("admin-a");
        using var driver = await fixture.LoginAsync("workflow-driver");
        using var otherCustomer = await fixture.LoginAsync("client-a-other");
        using var foreignCustomer = await fixture.LoginAsync("client-b");
        using var foreignAdmin = await fixture.LoginAsync("admin-b");
        var submission = Guid.NewGuid().ToString("N");
        var orderForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(customer.Client, "/Cliente/Pedido/Novo"),
            ["ChaveEnvio"] = submission, ["Descricao"] = "Integrated journey synthetic cargo", ["PesoKg"] = "10", ["ValorTotal"] = "1000",
            ["TipoCarga"] = "Carga Geral Seca", ["OrigemEndereco"] = "Test collection street", ["OrigemNumero"] = "1",
            ["OrigemCidade"] = "Campinas", ["OrigemEstado"] = "SP", ["DestinoEndereco"] = "Test destination street",
            ["DestinoNumero"] = "2", ["DestinoCidade"] = "Santos", ["DestinoEstado"] = "SP",
            ["ClienteId"] = fixture.Customers[2].Id.ToString(), ["TenantId"] = fixture.TenantB.ToString()
        };
        Uri? location = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var created = await customer.Client.PostAsync("/Cliente/Pedido/Novo", new FormUrlEncodedContent(orderForm));
            Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
            location ??= created.Headers.Location;
            Assert.Equal(location, created.Headers.Location);
        }
        int orderId;
        await using (var db = new MglDbContext(fixture.Options))
        {
            var order = Assert.Single(await db.Pedidos.Where(p => p.ChaveIdempotencia == submission).ToListAsync());
            Assert.Equal(fixture.TenantA, order.TenantId);
            Assert.Equal(fixture.Customers[0].Id, order.ClienteId);
            Assert.Equal("Registrado", order.Status);
            orderId = order.Id;
        }
        using (var queue = await admin.Client.GetAsync("/Dashboard/Painel"))
            Assert.Contains("Integrated journey synthetic cargo", await queue.Content.ReadAsStringAsync());
        using (var forbidden = await otherCustomer.Client.GetAsync(location))
            Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        using (var foreignOrder = await foreignAdmin.Client.GetAsync($"/Pedidos/Details/{orderId}"))
            Assert.Equal(HttpStatusCode.NotFound, foreignOrder.StatusCode);
        using (var foreignPrivate = await foreignCustomer.Client.GetAsync(location))
            Assert.Equal(HttpStatusCode.NotFound, foreignPrivate.StatusCode);
        using (var validated = await admin.Client.PostAsync("/Pedidos/AtualizarStatus", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Pedidos/Details/{orderId}"),
            ["id"] = orderId.ToString(), ["status"] = "EmProcessamento"
        }))) Assert.Equal(HttpStatusCode.Redirect, validated.StatusCode);
        await using (var db = new MglDbContext(fixture.Options))
            Assert.Equal("EmProcessamento", (await db.Pedidos.FindAsync(orderId))!.Status);

        var dispatchForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Entregas/Create"),
            ["PedidoId"] = orderId.ToString(), ["MotoristaId"] = newDriver.Id.ToString(), ["VeiculoId"] = vehicle.Id.ToString(),
            ["DestinatarioNome"] = "Journey Recipient", ["CepDestino"] = "00000-000", ["EnderecoDestino"] = "Test destination street",
            ["NumeroDestino"] = "2", ["BairroDestino"] = "Test district", ["CidadeDestino"] = "Santos", ["EstadoDestino"] = "SP",
            ["PrevisaoDias"] = "2"
        };
        using (var dispatch = await admin.Client.PostAsync("/Entregas/Create", new FormUrlEncodedContent(dispatchForm)))
            Assert.Equal(HttpStatusCode.Redirect, dispatch.StatusCode);
        using (var repeatedDispatch = await admin.Client.PostAsync("/Entregas/Create", new FormUrlEncodedContent(dispatchForm)))
            Assert.Equal(HttpStatusCode.OK, repeatedDispatch.StatusCode); // validation, not a second dispatch
        Entrega delivery;
        await using (var db = new MglDbContext(fixture.Options))
        {
            delivery = Assert.Single(await db.Entregas.Where(e => e.PedidoId == orderId).ToListAsync());
            Assert.Equal("Pendente", delivery.StatusEntrega);
            Assert.True(delivery.AtribuicaoAtiva);
            var dispatchEvent = await db.EventosRastreio.SingleAsync(e => e.EntregaId == delivery.Id);
            Assert.Equal("Coleta prevista: Campinas / SP", dispatchEvent.Localidade);
        }
        using (var afterDispatch = await admin.Client.GetAsync("/Dashboard/Painel"))
            Assert.DoesNotContain("Integrated journey synthetic cargo", await afterDispatch.Content.ReadAsStringAsync());
        using (var notStarted = await driver.Client.SendAsync(driver.Api(HttpMethod.Get, $"/api/entregas/{delivery.Id}")))
            Assert.Equal(HttpStatusCode.Forbidden, notStarted.StatusCode);

        using (var beforeShiftNavigation = await driver.Client.GetAsync($"/Entregas/Navegar/{delivery.Id}"))
        {
            Assert.Equal(HttpStatusCode.Redirect, beforeShiftNavigation.StatusCode);
            Assert.Contains("AccessDenied", beforeShiftNavigation.Headers.Location!.ToString());
        }
        using (var driverTrips = await driver.Client.GetAsync("/Entregas"))
        {
            var tripsHtml = await driverTrips.Content.ReadAsStringAsync();
            Assert.Contains("Journey test", tripsHtml);
            Assert.DoesNotContain("Private B01 cargo", tripsHtml);
        }

        var adminToken = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Entregas/Details/{delivery.Id}");
        using var codeResponse = await admin.Client.PostAsync("/Entregas/EmitirCodigoTurno", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = adminToken, ["entregaId"] = delivery.Id.ToString()
        }));
        Assert.Equal(HttpStatusCode.OK, codeResponse.StatusCode);
        var codeMatch = Regex.Match(await codeResponse.Content.ReadAsStringAsync(), "aria-label=\"Código temporário\">([^<]+)</output>");
        Assert.True(codeMatch.Success);
        var shiftForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(driver.Client, "/Entregas"),
            ["entregaId"] = delivery.Id.ToString(), ["codigo"] = WebUtility.HtmlDecode(codeMatch.Groups[1].Value)
        };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var started = await driver.Client.PostAsync("/Entregas/IniciarTurno", new FormUrlEncodedContent(shiftForm));
            Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
            Assert.Contains("Navegar", started.Headers.Location!.ToString());
        }
        await using (var db = new MglDbContext(fixture.Options))
        {
            Assert.Equal(1, await db.TurnosMotorista.CountAsync(s => s.EntregaId == delivery.Id && s.Ativo));
            Assert.Equal("Em transporte", (await db.Entregas.FindAsync(delivery.Id))!.StatusEntrega);
            var shiftEvent = await db.EventosRastreio.SingleAsync(e => e.EntregaId == delivery.Id && e.StatusAssociado == "Em transporte");
            Assert.Equal("Localização não informada no início do turno", shiftEvent.Localidade);
        }
        using (var navigation = await driver.Client.GetAsync($"/Entregas/Navegar/{delivery.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);
            Assert.True(navigation.Headers.CacheControl?.NoStore);
            var html = await navigation.Content.ReadAsStringAsync();
            Assert.Contains("id=\"cockpitMap\"", html);
            Assert.Contains("data-offline-enabled=\"true\"", html);
            Assert.DoesNotContain("id=\"btnSimular\"", html);
        }
        await using var customerHub = await SecurityHubConnection.ConnectAsync(fixture, customer);
        Assert.False((await customerHub.InvokeAsync("JoinLiveTracking", delivery.CodigoRastreio)).TryGetProperty("error", out _));
        await using var otherCustomerHub = await SecurityHubConnection.ConnectAsync(fixture, otherCustomer);
        Assert.True((await otherCustomerHub.InvokeAsync("JoinLiveTracking", delivery.CodigoRastreio)).TryGetProperty("error", out _));
        await using var foreignHub = await SecurityHubConnection.ConnectAsync(fixture, foreignAdmin);
        Assert.True((await foreignHub.InvokeAsync("JoinLiveTracking", delivery.CodigoRastreio)).TryGetProperty("error", out _));
        await using var driverHub = await SecurityHubConnection.ConnectAsync(fixture, driver);
        Assert.False((await driverHub.InvokeAsync("SendVehicleTelemetry", delivery.CodigoRastreio, -23d, -46d, 30d, 90d, 10d, false)).TryGetProperty("error", out _));
        var fix = await customerHub.EventPayloadAsync("ReceiveVehiclePosition", TimeSpan.FromSeconds(5));
        Assert.Equal(-23d, fix.GetProperty("latitude").GetDouble());
        Assert.False(fix.GetProperty("isSimulated").GetBoolean());
        using (var map = await admin.Client.GetAsync("/Operacoes/Posicoes"))
        {
            using var snapshot = JsonDocument.Parse(await map.Content.ReadAsStringAsync());
            var row = Assert.Single(snapshot.RootElement.GetProperty("items").EnumerateArray(), e => e.GetProperty("id").GetInt32() == delivery.Id);
            Assert.Equal(-23d, row.GetProperty("latitude").GetDouble());
        }
        Assert.True((await driverHub.InvokeAsync("SendVehicleTelemetry", delivery.CodigoRastreio, 91d, -46d, 30d, 90d, 10d, false)).TryGetProperty("error", out _));
        Assert.False((await driverHub.InvokeAsync("SendVehicleTelemetry", delivery.CodigoRastreio, -23.1d, -46d, 30d, 90d, 100d, false)).TryGetProperty("error", out _));
        Assert.False(await customerHub.ReceivesAsync("ReceiveVehiclePosition", TimeSpan.FromMilliseconds(250)));
        await using (var db = new MglDbContext(fixture.Options))
            Assert.Equal(-23d, (await db.Entregas.FindAsync(delivery.Id))!.UltimaLatitudeGps);

        var pod = new { nomeRecebedor = "Synthetic Receiver", documentoRecebedor = "PRIVATE-RECEIPT-DOC", localidade = "Private destination" };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var completed = await driver.Client.SendAsync(driver.Api(HttpMethod.Post, $"/api/entregas/{delivery.Id}/pod", pod));
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        }
        await using (var db = new MglDbContext(fixture.Options))
        {
            Assert.Equal("Concluido", (await db.Pedidos.FindAsync(orderId))!.Status);
            var persisted = (await db.Entregas.FindAsync(delivery.Id))!;
            Assert.Equal("Entregue", persisted.StatusEntrega);
            Assert.False(persisted.AtribuicaoAtiva);
            Assert.False(await db.TurnosMotorista.AnyAsync(s => s.EntregaId == delivery.Id && s.Ativo));
            Assert.Equal(1, await db.EventosRastreio.CountAsync(e => e.EntregaId == delivery.Id && e.StatusAssociado == "Entregue"));
        }
        using (var details = await customer.Client.GetAsync(location))
            Assert.Contains("Pacote Entregue com Sucesso", await details.Content.ReadAsStringAsync());
        using (var pdf = await customer.Client.GetAsync($"/Cliente/Pedido/{orderId}/Comprovante"))
        {
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.True(pdf.Headers.CacheControl?.NoStore);
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
        using (var deniedPdf = await otherCustomer.Client.SendAsync(otherCustomer.Api(HttpMethod.Get, $"/api/entregas/{delivery.Id}/pod-pdf")))
            Assert.Equal(HttpStatusCode.Forbidden, deniedPdf.StatusCode);
        using (var deniedMvcPdf = await otherCustomer.Client.GetAsync($"/Cliente/Pedido/{orderId}/Comprovante"))
            Assert.Equal(HttpStatusCode.NotFound, deniedMvcPdf.StatusCode);
        using (var foreignPdf = await foreignCustomer.Client.GetAsync($"/Cliente/Pedido/{orderId}/Comprovante"))
            Assert.Equal(HttpStatusCode.NotFound, foreignPdf.StatusCode);
        using (var finalMap = await admin.Client.GetAsync("/Operacoes/Posicoes"))
            Assert.DoesNotContain(delivery.CodigoRastreio, await finalMap.Content.ReadAsStringAsync());
        using (var roster = await admin.Client.GetAsync("/Frota/Painel"))
            Assert.DoesNotContain(delivery.CodigoRastreio, await roster.Content.ReadAsStringAsync());
        using var anonymous = fixture.Anonymous();
        using var tracking = await anonymous.GetAsync($"/api/rastreio/{delivery.CodigoRastreio}");
        var publicJson = await tracking.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRIVATE-RECEIPT-DOC", publicJson);
        using var json = JsonDocument.Parse(publicJson);
        Assert.Equal("Entregue", json.RootElement.GetProperty("status").GetString());
        Assert.True((await driverHub.InvokeAsync("SendVehicleTelemetry", delivery.CodigoRastreio, -23d, -46d, 30d, 90d, 10d, false)).TryGetProperty("error", out _));
    }
}
