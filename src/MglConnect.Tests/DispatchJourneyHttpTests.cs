using System.Net;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class DispatchJourneyHttpTests
{
    [Theory]
    [InlineData("inactive-driver", "motorista ativo")]
    [InlineData("inactive-vehicle", "veículo ativo")]
    [InlineData("capacity", "capacidade nominal")]
    [InlineData("foreign-driver", "motorista ativo")]
    [InlineData("foreign-vehicle", "veículo ativo")]
    [InlineData("wrong-profile", "motorista ativo")]
    public async Task InvalidDispatchResourcesAreRejectedByHttpWithoutCreatingDeliveryOrChangingOrder(string defect, string error)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var order = new Pedido { TenantId = fixture.TenantA, ClienteId = fixture.Customers[0].Id, NumeroPedido = "INVALID-DISPATCH", Descricao = "Synthetic dispatch validation", PesoKg = 10 };
        var vehicle = new Veiculo { TenantId = fixture.TenantA, Placa = "VALID01", Modelo = "Fixture own", CapacidadeCargaKg = defect == "capacity" ? 1 : 1000, Ativo = defect != "inactive-vehicle" };
        var driver = new Usuario { TenantId = fixture.TenantA, Login = "valid-driver", Nome = "Own fixture driver", Perfil = "Motorista", Ativo = defect != "inactive-driver", SenhaHash = fixture.Users["driver-a"].SenhaHash };
        var foreignVehicle = new Veiculo { TenantId = fixture.TenantB, Placa = "OTHER01", Modelo = "Fixture foreign", CapacidadeCargaKg = 1000 };
        var foreignDriver = new Usuario { TenantId = fixture.TenantB, Login = "foreign-driver", Nome = "Foreign fixture driver", Perfil = "Motorista", SenhaHash = fixture.Users["driver-b"].SenhaHash };
        await fixture.ChangeAsync(db => db.AddRange(order, vehicle, driver, foreignVehicle, foreignDriver));
        using var admin = await fixture.LoginAsync("admin-a");
        var form = new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Entregas/Create"),
            ["PedidoId"] = order.Id.ToString(),
            ["MotoristaId"] = (defect == "foreign-driver" ? foreignDriver.Id : defect == "wrong-profile" ? fixture.Users["admin-a"].Id : driver.Id).ToString(),
            ["VeiculoId"] = (defect == "foreign-vehicle" ? foreignVehicle.Id : vehicle.Id).ToString(),
            ["DestinatarioNome"] = "Synthetic receiver", ["CepDestino"] = "00000-000", ["EnderecoDestino"] = "Fixture street",
            ["NumeroDestino"] = "1", ["BairroDestino"] = "Fixture", ["CidadeDestino"] = "Santos", ["EstadoDestino"] = "SP", ["PrevisaoDias"] = "1"
        };
        using var response = await admin.Client.PostAsync("/Entregas/Create", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(error, html);
        Assert.Contains("validation-summary-errors", html);
        await using var db = new MglDbContext(fixture.Options);
        Assert.False(await db.Entregas.AnyAsync(e => e.PedidoId == order.Id));
        Assert.Equal("Registrado", (await db.Pedidos.FindAsync(order.Id))!.Status);
        Assert.Equal(0, (await db.Pedidos.FindAsync(order.Id))!.Versao);
    }
}
