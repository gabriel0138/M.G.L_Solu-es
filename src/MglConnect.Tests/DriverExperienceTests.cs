using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Controllers;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using MglConnect.Web.ViewModels;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public class DriverExperienceTests
{
    [Fact]
    public async Task DriverIndexReturnsOnlyActiveAssignedTripsAndSafeDisplayFields()
    {
        await using var context = CreateContext();
        var driver = new Usuario { Nome = "Motorista", Login = "driver", SenhaHash = "hash", Perfil = "Motorista" };
        var otherDriver = new Usuario { Nome = "Outro", Login = "other", SenhaHash = "hash", Perfil = "Motorista" };
        var vehicle = new Veiculo { Placa = "ABC1D23", Modelo = "Van", Ativo = true };
        context.AddRange(driver, otherDriver, vehicle);
        await context.SaveChangesAsync();

        var assigned = CreateDelivery(driver.Id, vehicle.Id, "São Paulo", "SP", "Pedido confidencial");
        var otherDriversTrip = CreateDelivery(otherDriver.Id, vehicle.Id, "Santos", "SP", "Outro pedido");
        var completed = CreateDelivery(driver.Id, vehicle.Id, "Campinas", "SP", "Concluído");
        completed.StatusEntrega = "Entregue";
        context.AddRange(assigned, otherDriversTrip, completed);
        await context.SaveChangesAsync();

        var controller = CreateController(context, driver.Id);
        var result = Assert.IsType<ViewResult>(await controller.Index(null, null));
        Assert.Equal("IndexMotorista", result.ViewName);

        var trips = Assert.IsAssignableFrom<IReadOnlyList<MotoristaViagemViewModel>>(result.Model);
        var trip = Assert.Single(trips);
        Assert.Equal(assigned.Id, trip.EntregaId);
        Assert.Equal("Van", trip.ModeloVeiculo);
        Assert.Equal("ABC1D23", trip.PlacaVeiculo);
        Assert.True(trip.VeiculoAtribuido);
        Assert.Equal("São Paulo", trip.CidadeDestino);
        Assert.Equal("SP", trip.EstadoDestino);
        Assert.Equal("Em transporte", trip.Status);
    }

    [Fact]
    public async Task DriverDetailsRedirectsToNavigationInsteadOfRenderingSensitiveDetails()
    {
        await using var context = CreateContext();
        var driver = new Usuario { Nome = "Motorista", Login = "driver", SenhaHash = "hash", Perfil = "Motorista" };
        context.Usuarios.Add(driver);
        await context.SaveChangesAsync();
        var delivery = CreateDelivery(driver.Id, null, "São Paulo", "SP", "Pedido confidencial");
        context.Entregas.Add(delivery);
        await context.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(
            await CreateController(context, driver.Id).Details(delivery.Id));

        Assert.Equal(nameof(EntregasController.Navegar), result.ActionName);
        Assert.Equal(delivery.Id, result.RouteValues!["id"]);
    }

    [Fact]
    public async Task DriverCannotOpenAnotherDriversDetails()
    {
        await using var context = CreateContext();
        var owner = new Usuario { Nome = "Dono", Login = "owner", SenhaHash = "hash", Perfil = "Motorista" };
        context.Usuarios.Add(owner);
        await context.SaveChangesAsync();
        var delivery = CreateDelivery(owner.Id, null, "São Paulo", "SP", "Pedido confidencial");
        context.Entregas.Add(delivery);
        await context.SaveChangesAsync();

        var result = await CreateController(context, owner.Id + 1).Details(delivery.Id);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DriverCannotOpenRoutePreviewWithoutPersistedActiveShift()
    {
        await using var context = CreateContext();
        var driver = new Usuario { Nome = "Motorista", Login = "driver", SenhaHash = "hash", Perfil = "Motorista" };
        var vehicle = new Veiculo { Placa = "ABC1D23", Modelo = "Van", Ativo = true };
        context.AddRange(driver, vehicle);
        await context.SaveChangesAsync();
        var delivery = CreateDelivery(driver.Id, vehicle.Id, "São Paulo", "SP", "Pedido confidencial");
        context.Entregas.Add(delivery);
        await context.SaveChangesAsync();

        var routeService = new Mock<ITruckRoutingService>();
        var shiftService = new Mock<IDriverShiftService>();
        shiftService.Setup(service => service.HasActiveShiftAsync(driver.Id, delivery.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateController(context, driver.Id, shiftService.Object, routeService.Object)
            .Navegar(delivery.Id);

        Assert.IsType<ForbidResult>(result);
        routeService.Verify(
            service => service.CalcularRotaParaEntregaAsync(It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DriverProfileShowsOnlyTheirServerProfileWithMaskedCpf()
    {
        await using var context = CreateContext();
        var driver = new Usuario
        {
            Nome = "Motorista",
            Login = "driver",
            SenhaHash = "hash",
            Perfil = "Motorista",
            Cpf = "222.333.444-55",
            DataNascimento = new DateTime(1988, 4, 15)
        };
        context.Usuarios.Add(driver);
        await context.SaveChangesAsync();

        var result = Assert.IsType<ViewResult>(
            await CreateController(context, driver.Id).PerfilMotorista());
        var profile = Assert.IsType<MotoristaPerfilViewModel>(result.Model);

        Assert.Equal("Motorista", profile.Nome);
        Assert.Equal(new DateTime(1988, 4, 15), profile.DataNascimento);
        Assert.Equal("***.***.***-55", profile.CpfMascarado);
    }

    private static MglDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MglDbContext(options);
    }

    private static EntregasController CreateController(
        MglDbContext context,
        int driverId,
        IDriverShiftService? shiftService = null,
        ITruckRoutingService? routeService = null)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, driverId.ToString()),
            new Claim(ClaimTypes.Role, "Motorista")
        ], "test"));
        var httpContext = new DefaultHttpContext { User = user };

        return new EntregasController(
            context,
            Mock.Of<ITrackingService>(),
            routeService ?? Mock.Of<ITruckRoutingService>(),
            Mock.Of<IFuelCostService>(),
            shiftService ?? Mock.Of<IDriverShiftService>(),
            Mock.Of<IEmergencyAlertService>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static Entrega CreateDelivery(
        int driverId,
        int? vehicleId,
        string city,
        string state,
        string orderDescription) => new()
    {
        PedidoId = Random.Shared.Next(1, int.MaxValue),
        MotoristaId = driverId,
        VeiculoId = vehicleId,
        CodigoRastreio = $"MGL-{Guid.NewGuid():N}"[..20],
        DestinatarioNome = orderDescription,
        EnderecoDestino = "Rua de teste",
        CidadeDestino = city,
        EstadoDestino = state,
        CepDestino = "01000-000",
        StatusEntrega = "Em transporte",
        AtribuicaoAtiva = true
    };
}
