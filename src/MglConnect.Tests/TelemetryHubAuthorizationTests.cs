using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public class TelemetryHubAuthorizationTests
{
    [Fact]
    public async Task ClienteNaoPodeEnviarTelemetria()
    {
        await using var context = CriarContexto();
        var hub = CriarHub(context, "12", "Cliente");

        var action = () => hub.SendVehicleTelemetry("MGL-00000001BR", -23, -46, 40, 90);

        await action.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task MotoristaNaoPodeAssinarEntregaDeOutroMotorista()
    {
        await using var context = CriarContexto();
        context.Entregas.Add(new Entrega
        {
            CodigoRastreio = "MGL-00000001BR",
            MotoristaId = 99
        });
        await context.SaveChangesAsync();

        var hub = CriarHub(context, "12", "Motorista");

        var action = () => hub.JoinLiveTracking("MGL-00000001BR");

        await action.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task MotoristaNaoPodeExecutarFerramentaDeSimulacao()
    {
        await using var context = CriarContexto();
        context.Entregas.Add(new Entrega
        {
            CodigoRastreio = "MGL-00000001BR",
            MotoristaId = 12
        });
        await context.SaveChangesAsync();
        var hub = CriarHub(context, "12", "Motorista");

        var action = () => hub.SimulateDeviationTrigger("MGL-00000001BR", 0.001, 0.001);

        await action.Should().ThrowAsync<HubException>()
            .WithMessage("*administrador*");
    }

    private static MglDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MglDbContext(options);
    }

    private static TelemetryHub CriarHub(MglDbContext context, string userId, string role)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        }, "test"));
        var callerContext = new Mock<HubCallerContext>();
        callerContext.SetupGet(c => c.User).Returns(principal);

        var deviationEngine = new Mock<IRouteDeviationEngine>();
        var routingService = new Mock<ITruckRoutingService>();
        var logger = new Mock<ILogger<TelemetryHub>>();
        var hub = new TelemetryHub(
            deviationEngine.Object,
            routingService.Object,
            logger.Object,
            context,
            Mock.Of<IDriverShiftService>())
        {
            Context = callerContext.Object
        };
        return hub;
    }
}
