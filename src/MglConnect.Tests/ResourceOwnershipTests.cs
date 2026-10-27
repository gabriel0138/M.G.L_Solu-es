using Xunit;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.Controllers.Api;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Moq;
using MglConnect.Web.Services;
using System.Security.Claims;

namespace MglConnect.Tests;

public class EntregasApiOwnershipTests
{
    private readonly MglDbContext _context;
    private readonly EntregasApiController _controller;

    public EntregasApiOwnershipTests()
    {
        // Setup in-memory database com dados de teste
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new MglDbContext(options);

        // Configurar usuário do controller (simulando Claims do Auth)
        var claims = new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"), // motorista ID 1
            new Claim(ClaimTypes.Role, "Motorista")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        // Mock do HttpContext com User configurado
        var httpContextMock = new Mock<HttpContext>();
        httpContextMock.SetupGet(h => h.User).Returns(claimsPrincipal);

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        httpContextAccessorMock.SetupGet(h => h.HttpContext).Returns(httpContextMock.Object);

        // Seed de dados básicos
        _context.Usuarios.AddRange(
            new MglConnect.Web.Models.Usuario { Id = 1, Login = "motorista1", Perfil = "Motorista", Ativo = true, SenhaHash = "hash" },
            new MglConnect.Web.Models.Usuario { Id = 2, Login = "motorista2", Perfil = "Motorista", Ativo = true, SenhaHash = "hash" }
        );

        var clients = new[]
        {
            new Cliente { RazaoSocial = "Cliente 1", Cnpj = "CNPJ-1", Telefone = "11999999991" },
            new Cliente { RazaoSocial = "Cliente 2", Cnpj = "CNPJ-2", Telefone = "11999999992" }
        };
        _context.Clientes.AddRange(clients);
        _context.SaveChanges();
        var orders = new[]
        {
            new Pedido { Id = 1, NumeroPedido = "PED-1", ClienteId = clients[0].Id, Descricao = "Pedido 1" },
            new Pedido { Id = 2, NumeroPedido = "PED-2", ClienteId = clients[1].Id, Descricao = "Pedido 2" }
        };
        _context.Pedidos.AddRange(orders);
        _context.SaveChanges();

        _context.Entregas.AddRange(
            new MglConnect.Web.Models.Entrega
            {
                Id = 1,
                CodigoRastreio = "MGL-00000001BR",
                MotoristaId = 1,
                StatusEntrega = "Em transporte",
                PedidoId = orders[0].Id,
                Pedido = orders[0],
                DestinatarioNome = "Destino 1",
                EnderecoDestino = "Rua 1",
                CidadeDestino = "Santos",
                EstadoDestino = "SP",
                CepDestino = "11000-000",
                AtribuicaoAtiva = true
            },
            new MglConnect.Web.Models.Entrega
            {
                Id = 2,
                CodigoRastreio = "MGL-00000002BR",
                MotoristaId = 2,
                StatusEntrega = "Pendente",
                PedidoId = orders[1].Id,
                Pedido = orders[1],
                DestinatarioNome = "Destino 2",
                EnderecoDestino = "Rua 2",
                CidadeDestino = "Santos",
                EstadoDestino = "SP",
                CepDestino = "11000-000",
                AtribuicaoAtiva = true
            }
        );
        _context.SaveChanges();

        // Controller - injetando contexto real e serviços mockados
        _controller = new EntregasApiController(_context, Mock.Of<ITrackingService>(),
            Mock.Of<ITrafficMatchService>(), Mock.Of<ITruckRoutingService>(),
            Mock.Of<IPodPdfService>(), Mock.Of<ICo2EsgService>(),
            Mock.Of<IDriverShiftService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContextMock.Object
            }
        };
    }

    [Fact]
    public async Task DriverCannotOpenApiDeliveryDetailsWithoutActiveShift()
    {
        var result = await _controller.GetDetalhes(1);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DriverCannotOpenAnotherDriversDeliveryThroughApi()
    {
        var result = await _controller.GetDetalhes(2);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DriverCannotUpdateAnotherDriversDeliveryThroughApi()
    {
        var result = await _controller.AtualizarStatus(
            2,
            new EntregasApiController.AtualizarStatusRequest("Cancelada", "Tentativa não autorizada.", null));

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Pendente", (await _context.Entregas.SingleAsync(item => item.Id == 2)).StatusEntrega);
    }

    [Fact]
    public async Task IntermediaryApiAccessIsLimitedToLinkedCustomer()
    {
        var firstClient = await _context.Clientes.SingleAsync(client => client.Cnpj == "CNPJ-1");
        var intermediary = new Usuario
        {
            Login = "intermediario",
            Perfil = "Intermediador",
            ClienteId = firstClient.Id,
            Ativo = true,
            SenhaHash = "hash"
        };
        _context.Usuarios.Add(intermediary);
        await _context.SaveChangesAsync();
        _controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, intermediary.Id.ToString()),
                new Claim(ClaimTypes.Role, "Intermediador")
            ], "test"))
        };

        Assert.IsType<OkObjectResult>(await _controller.GetDetalhes(1));
        Assert.IsType<ForbidResult>(await _controller.GetDetalhes(2));
    }
}