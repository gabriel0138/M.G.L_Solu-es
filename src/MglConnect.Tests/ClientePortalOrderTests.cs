using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using MglConnect.Web.Controllers;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.ViewModels;
using Xunit;

namespace MglConnect.Tests;

public class ClientePortalOrderTests
{
    [Fact]
    public void OrderValidationRangesAreCultureIndependentForBrazilianPortuguese()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var validationResults = new List<ValidationResult>();
            var model = CreateValidOrder();
            var isValid = Validator.TryValidateObject(
                model,
                new ValidationContext(model),
                validationResults,
                validateAllProperties: true);

            Assert.True(isValid, string.Join("; ", validationResults.Select(result => result.ErrorMessage)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task OrderIsCreatedForAuthenticatedCustomersOwnAccount()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new MglDbContext(options, new StaticTenantProvider(tenantId));
        var cliente = new Cliente
        {
            RazaoSocial = "Cliente autenticado",
            Cnpj = "CNPJ-100",
            Telefone = "11999999999"
        };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var usuario = new Usuario
        {
            Nome = "Usuário cliente",
            Login = "cliente",
            SenhaHash = "hash",
            Perfil = "Cliente",
            ClienteId = cliente.Id
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        var controller = CreateController(context, usuario.Id);
        var orderRequest = CreateValidOrder();
        var result = await controller.NovoPedido(orderRequest);
        var retry = await controller.NovoPedido(orderRequest);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.IsType<RedirectToActionResult>(retry);
        var pedido = await context.Pedidos.SingleAsync();
        Assert.Equal(cliente.Id, pedido.ClienteId);
        Assert.Equal(tenantId, pedido.TenantId);
        Assert.Equal("Rua de coleta", pedido.OrigemEndereco);
        Assert.Equal("Rua de destino", pedido.DestinoEndereco);
        Assert.Equal("RJ", pedido.DestinoEstado);
        Assert.StartsWith("PED-", pedido.NumeroPedido);
        Assert.Equal("Registrado", pedido.Status);
    }

    [Fact]
    public async Task InactiveCustomerCannotCreateOrder()
    {
        var (context, usuario, cliente) = await CreateCustomerContextAsync();
        await using (context)
        {
            cliente.Ativo = false;
            await context.SaveChangesAsync();

            var controller = CreateController(context, usuario.Id);
            var result = await controller.NovoPedido(CreateValidOrder());

            Assert.IsType<ForbidResult>(result);
            Assert.Empty(await context.Pedidos.ToListAsync());
        }
    }

    [Fact]
    public async Task IntermediaryCanOnlyAccessOrdersForItsLinkedCustomer()
    {
        var (context, user, customer) = await CreateCustomerContextAsync("Intermediador");
        await using (context)
        {
            var otherCustomer = new Cliente
            {
                RazaoSocial = "Outro cliente",
                Cnpj = $"CNPJ-{Guid.NewGuid():N}"[..13],
                Telefone = "11888888888"
            };
            context.Clientes.Add(otherCustomer);
            await context.SaveChangesAsync();

            var ownOrder = new Pedido
            {
                NumeroPedido = "PED-INTERMEDIARIO-1",
                ClienteId = customer.Id,
                Descricao = "Pedido próprio",
                Status = "Registrado"
            };
            var otherOrder = new Pedido
            {
                NumeroPedido = "PED-INTERMEDIARIO-2",
                ClienteId = otherCustomer.Id,
                Descricao = "Pedido alheio",
                Status = "Registrado"
            };
            context.Pedidos.AddRange(ownOrder, otherOrder);
            await context.SaveChangesAsync();

            var controller = CreateController(context, user.Id, "Intermediador");
            var listResult = Assert.IsType<ViewResult>(await controller.Pedidos(null, null));
            var visibleOrders = Assert.IsAssignableFrom<IReadOnlyList<Pedido>>(listResult.Model);
            Assert.Equal(new[] { ownOrder.Id }, visibleOrders.Select(order => order.Id));

            Assert.IsType<NotFoundResult>(await controller.PedidoDetalhes(otherOrder.Id));
        }
    }

    private static NovoPedidoClienteViewModel CreateValidOrder() => new()
    {
        Descricao = "Carga de teste",
        ValorTotal = 1000m,
        PesoKg = 200m,
        TipoCarga = "Carga Geral Seca",
        OrigemEndereco = "Rua de coleta",
        OrigemNumero = "10",
        OrigemCidade = "São Paulo",
        OrigemEstado = "SP",
        DestinoEndereco = "Rua de destino",
        DestinoNumero = "20",
        DestinoCidade = "Rio de Janeiro",
        DestinoEstado = "RJ",
        AlturaCargaMetros = 2m,
        LarguraCargaMetros = 1.5m,
        ComprimentoCargaMetros = 3m
    };

    private static ClientePortalController CreateController(
        MglDbContext context,
        int userId,
        string role = "Cliente")
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            ], "test"))
        };
        var tempDataProvider = new Mock<ITempDataProvider>();
        tempDataProvider.Setup(provider => provider.LoadTempData(httpContext))
            .Returns(new Dictionary<string, object>());

        return new ClientePortalController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, tempDataProvider.Object)
        };
    }

    private static async Task<(MglDbContext Context, Usuario Usuario, Cliente Cliente)> CreateCustomerContextAsync(
        string profile = "Cliente")
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new MglDbContext(options, new StaticTenantProvider(tenantId));
        var cliente = new Cliente
        {
            RazaoSocial = "Cliente autenticado",
            Cnpj = $"CNPJ-{Guid.NewGuid():N}"[..13],
            Telefone = "11999999999"
        };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var usuario = new Usuario
        {
            Nome = "Usuário cliente",
            Login = $"cliente-{Guid.NewGuid():N}",
            SenhaHash = "hash",
            Perfil = profile,
            ClienteId = cliente.Id
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();

        return (context, usuario, cliente);
    }

    private sealed class StaticTenantProvider(Guid tenantId) : ITenantProvider
    {
        public Guid TenantId { get; } = tenantId;
        public bool IsTenantBound => true;
    }
}
