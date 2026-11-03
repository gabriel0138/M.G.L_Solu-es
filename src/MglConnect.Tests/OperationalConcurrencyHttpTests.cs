using System.Net;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class OperationalConcurrencyHttpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentDispatchesNeverDuplicateAnOrderOrReserveTheSameDriverAndVehicle(bool sameOrder)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var vehicle = new Veiculo { TenantId = fixture.TenantA, Placa = "RACE001", Modelo = "Concurrent fixture", CapacidadeCargaKg = 1000 };
        var driver = new Usuario { TenantId = fixture.TenantA, Nome = "Concurrent driver", Login = "race-driver", Perfil = "Motorista", SenhaHash = fixture.Users["driver-a"].SenhaHash };
        var firstOrder = new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "RACE-ONE", Descricao = "First synthetic cargo", Status = "EmProcessamento", PesoKg = 10 };
        var secondOrder = new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "RACE-TWO", Descricao = "Second synthetic cargo", Status = "EmProcessamento", PesoKg = 10 };
        await fixture.ChangeAsync(db => db.AddRange(vehicle, driver, firstOrder, secondOrder));
        using var firstAdmin = await fixture.LoginAsync("admin-a");
        using var secondAdmin = await fixture.LoginAsync("admin-a");
        var firstForm = await Form(firstAdmin, firstOrder.Id);
        var secondForm = await Form(secondAdmin, sameOrder ? firstOrder.Id : secondOrder.Id);
        var responses = await Task.WhenAll(
            firstAdmin.Client.PostAsync("/Entregas/Create", new FormUrlEncodedContent(firstForm)),
            secondAdmin.Client.PostAsync("/Entregas/Create", new FormUrlEncodedContent(secondForm)));
        foreach (var response in responses)
        {
            using (response) Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
                $"Dispatch must report a usable validation/concurrency result, not {response.StatusCode}.");
        }
        await using var database = new MglDbContext(fixture.Options);
        var delivery = Assert.Single(await database.Entregas.Where(e => e.PedidoId == firstOrder.Id || e.PedidoId == secondOrder.Id).ToListAsync());
        Assert.True(delivery.AtribuicaoAtiva);
        Assert.Equal(driver.Id, delivery.MotoristaId);
        Assert.Equal(vehicle.Id, delivery.VeiculoId);
        Assert.Equal(1, await database.Entregas.CountAsync(e => e.MotoristaId == driver.Id && e.AtribuicaoAtiva));
        Assert.Equal(1, await database.Entregas.CountAsync(e => e.VeiculoId == vehicle.Id && e.AtribuicaoAtiva));
        Assert.Equal("ComEntregaAtribuida", (await database.Pedidos.FindAsync(delivery.PedidoId))!.Status);
        var loserId = delivery.PedidoId == firstOrder.Id ? secondOrder.Id : firstOrder.Id;
        Assert.Equal("EmProcessamento", (await database.Pedidos.FindAsync(loserId))!.Status);

        async Task<Dictionary<string,string>> Form(SecurityHttpFixture.Session admin, int orderId) => new()
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Entregas/Create"),
            ["PedidoId"] = orderId.ToString(), ["MotoristaId"] = driver.Id.ToString(), ["VeiculoId"] = vehicle.Id.ToString(),
            ["DestinatarioNome"] = "Synthetic receiver", ["CepDestino"] = "00000-000", ["EnderecoDestino"] = "Fixture street",
            ["NumeroDestino"] = "1", ["BairroDestino"] = "Fixture", ["CidadeDestino"] = "Santos", ["EstadoDestino"] = "SP", ["PrevisaoDias"] = "1"
        };
    }

    [Fact]
    public async Task ExpiredOrRevokedDriverAuthorizationCannotFinalizeOrSendGpsOnAnExistingConnection()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var driver = await fixture.LoginAsync("driver-a");
        var delivery = fixture.Deliveries[0];
        await using var hub = await SecurityHubConnection.ConnectAsync(fixture, driver);
        Assert.False((await hub.InvokeAsync("JoinLiveTracking", delivery.CodigoRastreio)).TryGetProperty("error", out _));
        using var expired = driver.Api(HttpMethod.Post, $"/api/entregas/{delivery.Id}/pod", new { nomeRecebedor = "Expired synthetic receipt" });
        expired.Headers.Authorization = new("Bearer", fixture.IssueTestToken("driver-a", DateTimeOffset.UtcNow.AddMinutes(-1)));
        using var expiredResult = await driver.Client.SendAsync(expired);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResult.StatusCode);
        await fixture.ChangeAsync(db => db.Usuarios.Single(u => u.Id == fixture.Users["driver-a"].Id).Ativo = false);
        Assert.True((await hub.InvokeAsync("SendVehicleTelemetry", delivery.CodigoRastreio, -23d, -46d, 30d, 90d, 10d, false)).TryGetProperty("error", out _));
        using var revoked = await driver.Client.SendAsync(driver.Api(HttpMethod.Post, $"/api/entregas/{delivery.Id}/pod", new { nomeRecebedor = "Revoked synthetic receipt" }));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var offlineContext = await driver.Client.GetAsync("/Motorista/Offline/Contexto");
        Assert.Equal(HttpStatusCode.Redirect, offlineContext.StatusCode);
        Assert.Contains("Login", offlineContext.Headers.Location!.ToString());
        await using var database = new MglDbContext(fixture.Options);
        var persisted = (await database.Entregas.FindAsync(delivery.Id))!;
        Assert.Equal("Em transporte", persisted.StatusEntrega);
        Assert.Null(persisted.NomeRecebedor);
        Assert.Null(persisted.UltimoGpsRecebidoEmUtc);
    }
}
