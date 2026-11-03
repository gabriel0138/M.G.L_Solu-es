using System.Net;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using Xunit;

namespace MglConnect.Tests;

public class OperationalJourneyRegressionTests
{
    [Fact]
    public async Task LowAccuracyBeforeRouteInitializationNeverReachesAnAuthorizedObserver()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        await using var sender = await SecurityHubConnection.ConnectAsync(fixture, admin);
        await using var observer = await SecurityHubConnection.ConnectAsync(fixture, admin);
        var code = fixture.Deliveries[0].CodigoRastreio;
        Assert.False((await observer.InvokeAsync("JoinLiveTracking", code)).TryGetProperty("error", out _));
        Assert.False((await sender.InvokeAsync("SendVehicleTelemetry", code, -23d, -46d, 30d, 90d, 100d, false)).TryGetProperty("error", out _));
        Assert.False(await observer.ReceivesAsync("ReceiveVehiclePosition", TimeSpan.FromMilliseconds(350)));
        await using var verify = new MglDbContext(fixture.Options);
        Assert.Null((await verify.Entregas.FindAsync(fixture.Deliveries[0].Id))!.UltimoGpsRecebidoEmUtc);
    }

    [Theory]
    [InlineData("Cancelado")]
    [InlineData("EmProcessamento")]
    public async Task OrderWithAnExistingDeliveryCannotBeChangedByThePreDispatchStatusEndpoint(string status)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var delivery = fixture.Deliveries[0];
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Pedidos/Details/{delivery.PedidoId}");
        using var response = await admin.Client.PostAsync("/Pedidos/AtualizarStatus", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = token, ["id"] = delivery.PedidoId.ToString(), ["status"] = status
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = new MglDbContext(fixture.Options);
        var order = await db.Pedidos.FindAsync(delivery.PedidoId);
        Assert.Equal("Registrado", order!.Status); // existing legacy state must not be made less consistent
        Assert.Equal(0, order.Versao);
        Assert.Equal("Em transporte", (await db.Entregas.FindAsync(delivery.Id))!.StatusEntrega);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("signature")]
    public async Task MvcPodRetryWithTheSameReceiverButDifferentContentNeverAcknowledgesSuccess(string field)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        var delivery = fixture.Deliveries[0];
        using var admin = await fixture.LoginAsync("admin-a");
        using var driver = await fixture.LoginAsync("driver-a");
        using var complete = await admin.Client.SendAsync(admin.Api(HttpMethod.Post, $"/api/entregas/{delivery.Id}/pod",
            new { nomeRecebedor = "Receiver", documentoRecebedor = "ORIGINAL", assinaturaBase64 = "original-signature" }));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var token = await SecurityHttpFixture.AntiForgeryAsync(driver.Client, "/Entregas");
        using var retry = await driver.Client.PostAsync("/Entregas/FinalizarEntregaPOD", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = token, ["entregaId"] = delivery.Id.ToString(), ["nomeRecebedor"] = "Receiver",
            ["documentoRecebedor"] = field == "document" ? "CHANGED" : "ORIGINAL",
            ["assinaturaBase64"] = field == "signature" ? "changed-signature" : "original-signature"
        }));
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
        Assert.Equal("/Entregas", retry.Headers.Location!.ToString());
        using var feedback = await driver.Client.GetAsync(retry.Headers.Location);
        var html = WebUtility.HtmlDecode(await feedback.Content.ReadAsStringAsync());
        Assert.Contains("outro conteúdo", html);
        Assert.DoesNotContain("A confirmação de entrega já havia sido registrada", html);
        await using var db = new MglDbContext(fixture.Options);
        var persisted = await db.Entregas.FindAsync(delivery.Id);
        Assert.Equal("ORIGINAL", persisted!.DocumentoRecebedor);
        Assert.Equal("original-signature", persisted.AssinaturaDigitalBase64);
        Assert.Equal(1, await db.EventosRastreio.CountAsync(e => e.EntregaId == delivery.Id && e.StatusAssociado == "Entregue"));
    }

    [Theory]
    [InlineData("Entregue")]
    [InlineData("Cancelada")]
    [InlineData("Pendente")]
    public async Task AnAdministratorCannotBroadcastOrRecordGpsForAnEndedOrInactiveAssignment(string status)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        await using var sender = await SecurityHubConnection.ConnectAsync(fixture, admin);
        await using var observer = await SecurityHubConnection.ConnectAsync(fixture, admin);
        var delivery = fixture.Deliveries[0];
        Assert.False((await observer.InvokeAsync("JoinLiveTracking", delivery.CodigoRastreio)).TryGetProperty("error", out _));
        await fixture.ChangeAsync(db =>
        {
            var row = db.Entregas.Single(e => e.Id == delivery.Id);
            row.StatusEntrega = status;
            row.AtribuicaoAtiva = false;
        });
        Assert.True((await sender.InvokeAsync("SendVehicleTelemetry", delivery.CodigoRastreio,
            -23d, -46d, 30d, 90d, 10d, false)).TryGetProperty("error", out _));
        Assert.False(await observer.ReceivesAsync("ReceiveVehiclePosition", TimeSpan.FromMilliseconds(350)));
        await using var verify = new MglDbContext(fixture.Options);
        Assert.Null((await verify.Entregas.FindAsync(delivery.Id))!.UltimoGpsRecebidoEmUtc);
    }
}
