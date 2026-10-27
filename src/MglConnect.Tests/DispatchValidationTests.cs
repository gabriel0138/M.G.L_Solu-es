using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using MglConnect.Web.Controllers;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using MglConnect.Web.ViewModels;
using Xunit;

namespace MglConnect.Tests;

public class DispatchValidationTests
{
    [Fact]
    public async Task DispatchRejectsOrderThatAlreadyHasDelivery()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.Context.Entregas.Add(CreateExistingDelivery(fixture.Pedido.Id, "MGL-EXISTING"));
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Controller.Create(CreateValidDispatch(fixture));

        Assert.IsType<ViewResult>(result);
        AssertModelError(fixture.Controller, nameof(NovaEntregaViewModel.PedidoId), "já possui uma entrega");
        Assert.Single(await fixture.Context.Entregas.ToListAsync());
    }

    [Fact]
    public async Task DispatchRejectsInactiveDriver()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.Motorista.Ativo = false;
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Controller.Create(CreateValidDispatch(fixture));

        Assert.IsType<ViewResult>(result);
        AssertModelError(fixture.Controller, nameof(NovaEntregaViewModel.MotoristaId), "motorista ativo");
        Assert.Empty(await fixture.Context.Entregas.ToListAsync());
    }

    [Fact]
    public async Task DispatchRejectsInactiveVehicle()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.Veiculo.Ativo = false;
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Controller.Create(CreateValidDispatch(fixture));

        Assert.IsType<ViewResult>(result);
        AssertModelError(fixture.Controller, nameof(NovaEntregaViewModel.VeiculoId), "veículo ativo");
        Assert.Empty(await fixture.Context.Entregas.ToListAsync());
    }

    [Fact]
    public async Task DispatchRejectsVehicleWithInsufficientCapacity()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.Veiculo.CapacidadeCargaKg = 100m;
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Controller.Create(CreateValidDispatch(fixture));

        Assert.IsType<ViewResult>(result);
        AssertModelError(fixture.Controller, nameof(NovaEntregaViewModel.VeiculoId), "capacidade nominal");
        Assert.Empty(await fixture.Context.Entregas.ToListAsync());
    }

    [Fact]
    public async Task DispatchRejectsDriverWithAnotherActiveDelivery()
    {
        await using var fixture = await CreateFixtureAsync();
        var otherOrder = await AddOtherOrderAsync(fixture.Context);
        fixture.Context.Entregas.Add(CreateExistingDelivery(
            otherOrder.Id,
            "MGL-DRIVER-BUSY",
            motoristaId: fixture.Motorista.Id));
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Controller.Create(CreateValidDispatch(fixture));

        Assert.IsType<ViewResult>(result);
        AssertModelError(fixture.Controller, nameof(NovaEntregaViewModel.MotoristaId), "entrega ativa");
        Assert.Single(await fixture.Context.Entregas.ToListAsync());
    }

    [Fact]
    public async Task DispatchRejectsVehicleWithAnotherActiveDelivery()
    {
        await using var fixture = await CreateFixtureAsync();
        var otherOrder = await AddOtherOrderAsync(fixture.Context);
        fixture.Context.Entregas.Add(CreateExistingDelivery(
            otherOrder.Id,
            "MGL-VEHICLE-BUSY",
            veiculoId: fixture.Veiculo.Id));
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Controller.Create(CreateValidDispatch(fixture));

        Assert.IsType<ViewResult>(result);
        AssertModelError(fixture.Controller, nameof(NovaEntregaViewModel.VeiculoId), "reservado");
        Assert.Single(await fixture.Context.Entregas.ToListAsync());
    }

    [Fact]
    public async Task ValidDispatchCreatesDeliveryAndAssignsOrder()
    {
        await using var fixture = await CreateFixtureAsync();
        var result = await fixture.Controller.Create(CreateValidDispatch(fixture));

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(EntregasController.Details), redirect.ActionName);
        var entrega = await fixture.Context.Entregas.SingleAsync();
        Assert.Equal(fixture.Motorista.Id, entrega.MotoristaId);
        Assert.Equal(fixture.Veiculo.Id, entrega.VeiculoId);
        Assert.Equal("Pendente", entrega.StatusEntrega);
        Assert.Equal("ComEntregaAtribuida", fixture.Pedido.Status);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new MglDbContext(options);
        var cliente = new Cliente
        {
            RazaoSocial = "Cliente teste",
            Cnpj = $"CNPJ-{Guid.NewGuid():N}"[..13],
            Telefone = "11999999999"
        };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var pedido = new Pedido
        {
            NumeroPedido = $"PED-{Guid.NewGuid():N}"[..18],
            ClienteId = cliente.Id,
            Descricao = "Pedido teste",
            PesoKg = 500m,
            Status = "Registrado"
        };
        var motorista = new Usuario
        {
            Nome = "Motorista teste",
            Login = $"motorista-{Guid.NewGuid():N}",
            SenhaHash = "hash",
            Perfil = "Motorista",
            Ativo = true
        };
        var veiculo = new Veiculo
        {
            Placa = "ABC1D23",
            Modelo = "Van de teste",
            CapacidadeCargaKg = 1000m,
            Ativo = true
        };
        context.AddRange(pedido, motorista, veiculo);
        await context.SaveChangesAsync();

        var trackingService = new Mock<ITrackingService>();
        trackingService.Setup(service => service.GenerateTrackingCode())
            .Returns($"MGL-{Guid.NewGuid():N}"[..20]);
        trackingService.Setup(service => service.AddCheckpointAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(new EventoRastreio());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, "Admin")],
                "test"))
        };
        var tempDataProvider = new Mock<ITempDataProvider>();
        tempDataProvider.Setup(provider => provider.LoadTempData(httpContext))
            .Returns(new Dictionary<string, object>());

        var controller = new EntregasController(
            context,
            trackingService.Object,
            Mock.Of<ITruckRoutingService>(),
            Mock.Of<IFuelCostService>(),
            Mock.Of<IDriverShiftService>(),
            Mock.Of<IEmergencyAlertService>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, tempDataProvider.Object)
        };

        return new Fixture(context, controller, pedido, motorista, veiculo);
    }

    private static NovaEntregaViewModel CreateValidDispatch(Fixture fixture) => new()
    {
        PedidoId = fixture.Pedido.Id,
        MotoristaId = fixture.Motorista.Id,
        VeiculoId = fixture.Veiculo.Id,
        DestinatarioNome = "Destinatário",
        DestinatarioDocumento = "123456789",
        CepDestino = "01000-000",
        EnderecoDestino = "Rua de destino",
        NumeroDestino = "10",
        BairroDestino = "Centro",
        CidadeDestino = "São Paulo",
        EstadoDestino = "SP"
    };

    private static Entrega CreateExistingDelivery(
        int pedidoId,
        string trackingCode,
        int? motoristaId = null,
        int? veiculoId = null) => new()
    {
        PedidoId = pedidoId,
        MotoristaId = motoristaId,
        VeiculoId = veiculoId,
        CodigoRastreio = trackingCode,
        DestinatarioNome = "Destinatário",
        EnderecoDestino = "Rua",
        CidadeDestino = "São Paulo",
        EstadoDestino = "SP",
        CepDestino = "01000-000",
        StatusEntrega = "Em transporte",
        AtribuicaoAtiva = true
    };

    private static async Task<Pedido> AddOtherOrderAsync(MglDbContext context)
    {
        var cliente = await context.Clientes.FirstAsync();
        var pedido = new Pedido
        {
            NumeroPedido = $"PED-{Guid.NewGuid():N}"[..18],
            ClienteId = cliente.Id,
            Descricao = "Outro pedido",
            PesoKg = 100m,
            Status = "ComEntregaAtribuida"
        };
        context.Pedidos.Add(pedido);
        await context.SaveChangesAsync();
        return pedido;
    }

    private static void AssertModelError(Controller controller, string key, string expectedMessagePart)
    {
        Assert.Contains(
            controller.ModelState[key]!.Errors,
            error => error.ErrorMessage.Contains(expectedMessagePart, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Fixture(
        MglDbContext context,
        EntregasController controller,
        Pedido pedido,
        Usuario motorista,
        Veiculo veiculo) : IAsyncDisposable
    {
        public MglDbContext Context { get; } = context;
        public EntregasController Controller { get; } = controller;
        public Pedido Pedido { get; } = pedido;
        public Usuario Motorista { get; } = motorista;
        public Veiculo Veiculo { get; } = veiculo;

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
