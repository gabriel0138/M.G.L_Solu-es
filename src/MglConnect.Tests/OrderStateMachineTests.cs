using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Controllers;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public class OrderStateMachineTests
{
    [Fact]
    public async Task OrderCannotBeCompletedOutsideDeliveryPodFlow()
    {
        await using var context = CreateContext();
        var order = await AddOrderAsync(context);
        var controller = CreateController(context);

        var result = await controller.AtualizarStatus(order.Id, "Concluido");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Registrado", (await context.Pedidos.SingleAsync()).Status);
    }

    [Fact]
    public async Task OrderCanOnlyEnterProcessingFromRegisteredState()
    {
        await using var context = CreateContext();
        var order = await AddOrderAsync(context);
        var controller = CreateController(context);

        Assert.IsType<RedirectToActionResult>(
            await controller.AtualizarStatus(order.Id, "EmProcessamento"));
        Assert.Equal("EmProcessamento", (await context.Pedidos.SingleAsync()).Status);

        Assert.IsType<RedirectToActionResult>(
            await controller.AtualizarStatus(order.Id, "Registrado"));
        Assert.Equal("EmProcessamento", (await context.Pedidos.SingleAsync()).Status);
    }

    private static MglDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MglDbContext(options);
    }

    private static async Task<Pedido> AddOrderAsync(MglDbContext context)
    {
        var customer = new Cliente
        {
            RazaoSocial = "Cliente teste",
            Cnpj = $"CNPJ-{Guid.NewGuid():N}"[..13],
            Telefone = "11999999999"
        };
        context.Clientes.Add(customer);
        await context.SaveChangesAsync();
        var order = new Pedido
        {
            NumeroPedido = $"PED-{Guid.NewGuid():N}"[..18],
            ClienteId = customer.Id,
            Descricao = "Pedido teste",
            Status = "Registrado"
        };
        context.Pedidos.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    private static PedidosController CreateController(MglDbContext context)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Role, "Admin")
            ], "test"))
        };
        var provider = new Mock<ITempDataProvider>();
        provider.Setup(item => item.LoadTempData(httpContext)).Returns(new Dictionary<string, object>());
        return new PedidosController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, provider.Object)
        };
    }
}
