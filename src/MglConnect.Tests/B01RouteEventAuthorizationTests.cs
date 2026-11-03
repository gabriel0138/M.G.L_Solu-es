using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;
using MglConnect.Web.Services;
using Moq;
using Xunit;

namespace MglConnect.Tests;

// Service-level integration: real SQLite/shift authorization, controlled routing
// and SignalR clients. HTTP authentication/transport is covered separately.
public class B01RouteEventAuthorizationTests
{
    public static IEnumerable<object[]> RoutingAndRevocationCases()
    {
        foreach (var outcome in new[] { "success", "unavailable", "exception" })
        foreach (var reason in new[] { "none", "account", "role", "customer", "tenant", "shift", "assignment", "customer-active" })
            yield return new object[] { outcome, reason, false };
        foreach (var outcome in new[] { "success", "unavailable", "exception" })
            yield return new object[] { outcome, "none", true };
    }

    [Theory]
    [MemberData(nameof(RoutingAndRevocationCases))]
    public async Task RouteResultRevalidatesRecipientsAfterProviderCompletes(string outcome, string reason, bool isSimulated)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(startServer: false);
        await using var db = new MglDbContext(fixture.Options, new BoundTenant(fixture.TenantA));
        var subscriptions = new TelemetrySubscriptions();
        var emissions = new List<Emission>();
        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        var caller = new Mock<ISingleClientProxy>();
        caller.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        clients.SetupGet(c => c.Caller).Returns(caller.Object);
        clients.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>()))
            .Returns((IReadOnlyList<string> recipients) =>
            {
                var ids = recipients.ToArray();
                var proxy = new Mock<IClientProxy>();
                proxy.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                    .Callback<string, object?[], CancellationToken>((name, args, _) =>
                        emissions.Add(new Emission(name, ids, JsonSerializer.SerializeToElement(args[0]))))
                    .Returns(Task.CompletedTask);
                return proxy.Object;
            });
        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        groups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var engine = new Mock<IRouteDeviationEngine>();
        engine.Setup(e => e.EvaluatePosition(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>()))
            .Returns(new DeviationEvaluationResult { GpsDataAccepted = true, NeedsReroute = true });
        engine.Setup(e => e.GetState(It.IsAny<string>()))
            .Returns(new VehicleTelemetryState { EntregaId = fixture.Deliveries[0].Id, PerfilVeiculo = "Leve" });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TaskCompletionSource<TruckRoutePlan?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var routing = new Mock<ITruckRoutingService>();
        var code = fixture.Deliveries[0].CodigoRastreio;
        routing.Setup(r => r.RecalcularRotaDinamicaAsync(code, -23d, -46d, "Leve"))
            .Returns(() => { entered.TrySetResult(); return provider.Task; });

        var affectedLogin = reason is "shift" or "assignment" ? "driver-a" : "client-a";
        var affected = CreateHub(affectedLogin, "affected");
        // The administrator retains access when the recipient loses permission.
        var control = CreateHub("admin-a", "control");
        await affected.JoinLiveTracking(code);
        await control.JoinLiveTracking(code);

        var sending = control.SendVehicleTelemetry(code, -23d, -46d, 40d, 90d, 10d, isSimulated);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var position = Assert.Single(emissions);
            Assert.Equal("ReceiveVehiclePosition", position.Event);
            Assert.Contains("affected", position.Recipients);
            Assert.Contains("control", position.Recipients);

            // The position was legitimately delivered. Change permissions in a
            // separate committed context while the route provider is pending.
            await fixture.ChangeAsync(database =>
            {
                var user = database.Usuarios.Single(u => u.Id == fixture.Users[affectedLogin].Id);
                switch (reason)
                {
                    case "account": user.Ativo = false; break;
                    case "role": user.Perfil = "Motorista"; break;
                    case "customer": user.ClienteId = fixture.Customers[1].Id; break;
                    case "tenant": user.TenantId = fixture.TenantB; break;
                    case "shift": database.TurnosMotorista.Single(t => t.MotoristaId == user.Id).Ativo = false; break;
                    case "assignment": database.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id).MotoristaId = null; break;
                    case "customer-active": database.Clientes.Single(c => c.Id == fixture.Customers[0].Id).Ativo = false; break;
                }
            });

            if (outcome == "exception") provider.SetException(new HttpRequestException("Controlled provider failure"));
            else provider.SetResult(outcome == "unavailable" ? null : new TruckRoutePlan
            {
                Origem = "Fixture origin", Destino = "Fixture destination", DistanciaKm = 12m,
                TempoEstimadoMinutos = 20, RotaDisponivel = true, RotaHomologadaParaVeiculo = false,
                AvisoRota = "Fixture route requires review",
                TrajetoCoordenadas = new() { new() { Latitude = -23d, Longitude = -46d } }
            });
            await sending.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, emissions.Count);
            var result = emissions[1];
            Assert.Equal(outcome == "success" ? "ReceiveRouteRecalculated" : "ReceiveRouteRecalculationFailed", result.Event);
            Assert.Contains("control", result.Recipients);
            Assert.Equal(reason == "none", result.Recipients.Contains("affected"));
            Assert.Equal(code, result.Payload.GetProperty("codigoRastreio").GetString());
            Assert.Equal(isSimulated, result.Payload.GetProperty("isSimulated").GetBoolean());
            if (outcome == "success")
            {
                Assert.Equal(12m, result.Payload.GetProperty("distanciaKm").GetDecimal());
                Assert.False(result.Payload.GetProperty("verifiedForVehicle").GetBoolean());
            }
            else Assert.True(result.Payload.GetProperty("revisaoDespachanteNecessaria").GetBoolean());
            Assert.Equal(reason == "none", subscriptions.ForCode(code).Any(s => s.ConnectionId == "affected"));
            groups.Verify(g => g.RemoveFromGroupAsync("affected", code, It.IsAny<CancellationToken>()),
                reason == "none" ? Times.Never() : Times.Once());
            routing.Verify(r => r.RecalcularRotaDinamicaAsync(code, -23d, -46d, "Leve"), Times.Once());
        }
        finally
        {
            provider.TrySetResult(null);
            await sending.WaitAsync(TimeSpan.FromSeconds(5));
        }

        TelemetryHub CreateHub(string login, string connectionId)
        {
            var user = fixture.Users[login];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Perfil == "Administrador" ? "Admin" : user.Perfil),
                new Claim("TenantId", user.TenantId.ToString())
            }, "service-test"));
            var context = new Mock<HubCallerContext>();
            context.SetupGet(c => c.User).Returns(principal);
            context.SetupGet(c => c.ConnectionId).Returns(connectionId);
            return new TelemetryHub(engine.Object, routing.Object, NullLogger<TelemetryHub>.Instance,
                db, new DriverShiftService(db), subscriptions)
            {
                Context = context.Object, Clients = clients.Object, Groups = groups.Object
            };
        }
    }

    private sealed record Emission(string Event, string[] Recipients, JsonElement Payload);
    private sealed class BoundTenant(Guid tenantId) : ITenantProvider
    {
        public Guid TenantId => tenantId;
        public bool IsTenantBound => true;
    }
}
