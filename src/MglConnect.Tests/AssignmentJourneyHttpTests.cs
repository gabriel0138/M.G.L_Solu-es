using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class AssignmentJourneyHttpTests
{
    [Theory]
    [InlineData("vehicle", "Pendente")]
    [InlineData("driver", "Pendente")]
    [InlineData("vehicle", "Em transporte")]
    [InlineData("driver", "Em transporte")]
    public async Task RepeatingTheCurrentAssignmentPreservesTheIssuedCodeAndDeliveryUntilTheDriverStarts(string resource, string status)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var delivery = fixture.Deliveries[0];
        await fixture.ChangeAsync(db =>
        {
            db.Entregas.Single(e => e.Id == delivery.Id).StatusEntrega = status;
            db.Veiculos.Single(v => v.Id == delivery.VeiculoId).CapacidadeCargaKg = 1000;
        });
        using var admin = await fixture.LoginAsync("admin-a");
        using var driver = await fixture.LoginAsync("driver-a");
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Entregas/Details/{delivery.Id}");
        var code = await IssueCode(admin.Client, delivery.Id, token);
        for (var retry = 0; retry < 2; retry++)
        {
            using var response = await admin.Client.PostAsync(resource == "vehicle" ? "/Entregas/AtribuirVeiculo" : "/Entregas/AtribuirMotorista", new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["__RequestVerificationToken"] = token, ["entregaId"] = delivery.Id.ToString(),
                [resource == "vehicle" ? "veiculoId" : "motoristaId"] = (resource == "vehicle" ? delivery.VeiculoId : delivery.MotoristaId)!.Value.ToString()
            }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        await using (var db = new MglDbContext(fixture.Options))
        {
            var saved = (await db.Entregas.FindAsync(delivery.Id))!;
            Assert.Equal(status, saved.StatusEntrega);
            Assert.Equal(delivery.Versao, saved.Versao);
            Assert.Null((await db.CodigosTurnoMotorista.SingleAsync(c => c.EntregaId == delivery.Id)).CanceladoEmUtc);
            Assert.Equal(0, await db.EventosRastreio.CountAsync(e => e.EntregaId == delivery.Id));
        }
        using var started = await driver.Client.PostAsync("/Entregas/IniciarTurno", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(driver.Client, "/Entregas"),
            ["entregaId"] = delivery.Id.ToString(), ["codigo"] = code
        }));
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        Assert.Equal($"/Entregas/Navegar/{delivery.Id}", started.Headers.Location!.ToString());
        await using var verify = new MglDbContext(fixture.Options);
        Assert.Equal(1, await verify.TurnosMotorista.CountAsync(t => t.EntregaId == delivery.Id && t.Ativo));
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("vehicle")]
    public async Task CodeIssuanceRequiresCurrentlyActiveAssignedResources(string resource)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var delivery = fixture.Deliveries[0];
        await fixture.ChangeAsync(db =>
        {
            if (resource == "vehicle") db.Veiculos.Single(v => v.Id == delivery.VeiculoId).Ativo = false;
            else db.Usuarios.Single(u => u.Id == delivery.MotoristaId).Ativo = false;
        });
        using var admin = await fixture.LoginAsync("admin-a");
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Entregas/Details/{delivery.Id}");
        using var response = await admin.Client.PostAsync("/Entregas/EmitirCodigoTurno", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = token, ["entregaId"] = delivery.Id.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = new MglDbContext(fixture.Options);
        Assert.False(await db.CodigosTurnoMotorista.AnyAsync(c => c.EntregaId == delivery.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DriverCannotStartWithAVehicleThatBecameInactiveOrForeignAfterCodeIssuance(bool foreign)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var delivery = fixture.Deliveries[0];
        using var admin = await fixture.LoginAsync("admin-a");
        using var driver = await fixture.LoginAsync("driver-a");
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Entregas/Details/{delivery.Id}");
        var code = await IssueCode(admin.Client, delivery.Id, token);
        await fixture.ChangeAsync(db =>
        {
            var vehicle = db.Veiculos.Single(v => v.Id == delivery.VeiculoId);
            if (foreign) vehicle.TenantId = fixture.TenantB;
            else vehicle.Ativo = false;
        });
        using var response = await driver.Client.PostAsync("/Entregas/IniciarTurno", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(driver.Client, "/Entregas"),
            ["entregaId"] = delivery.Id.ToString(), ["codigo"] = code
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Entregas", response.Headers.Location!.ToString());
        await using var db = new MglDbContext(fixture.Options);
        Assert.False(await db.TurnosMotorista.AnyAsync(t => t.EntregaId == delivery.Id));
        Assert.Null((await db.CodigosTurnoMotorista.SingleAsync(c => c.EntregaId == delivery.Id)).UsadoEmUtc);
    }

    [Theory]
    [InlineData("vehicle")]
    [InlineData("driver")]
    public async Task ConcurrentReassignmentsCannotReserveOneResourceForTwoDeliveries(string resource)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var vehicle = new Veiculo { TenantId = fixture.TenantA, Placa = "REAS001", Modelo = "Synthetic reassignment", CapacidadeCargaKg = 1000 };
        var driver = new Usuario { TenantId = fixture.TenantA, Nome = "Synthetic reassignment", Login = "reassign-driver", Perfil = "Motorista", SenhaHash = fixture.Users["driver-a"].SenhaHash };
        await fixture.ChangeAsync(db =>
        {
            db.AddRange(vehicle, driver);
            foreach (var delivery in db.Entregas.Where(e => e.TenantId == fixture.TenantA)) delivery.StatusEntrega = "Pendente";
            // This test isolates contention for a driver; each existing vehicle must also be eligible.
            foreach (var existing in db.Veiculos.Where(v => v.TenantId == fixture.TenantA)) existing.CapacidadeCargaKg = 1000;
        });
        using var adminOne = await fixture.LoginAsync("admin-a");
        using var adminTwo = await fixture.LoginAsync("admin-a");
        var forms = new List<Dictionary<string,string>>();
        foreach (var (actor, delivery) in new[] { (adminOne, fixture.Deliveries[0]), (adminTwo, fixture.Deliveries[1]) })
            forms.Add(new Dictionary<string,string>
            {
                ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(actor.Client, $"/Entregas/Details/{delivery.Id}"),
                ["entregaId"] = delivery.Id.ToString(), [resource == "vehicle" ? "veiculoId" : "motoristaId"] = (resource == "vehicle" ? vehicle.Id : driver.Id).ToString()
            });
        var path = resource == "vehicle" ? "/Entregas/AtribuirVeiculo" : "/Entregas/AtribuirMotorista";
        var responses = await Task.WhenAll(adminOne.Client.PostAsync(path, new FormUrlEncodedContent(forms[0])), adminTwo.Client.PostAsync(path, new FormUrlEncodedContent(forms[1])));
        foreach (var response in responses) using (response) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = new MglDbContext(fixture.Options);
        Assert.Equal(1, await db.Entregas.CountAsync(e => e.AtribuicaoAtiva && (resource == "vehicle" ? e.VeiculoId == vehicle.Id : e.MotoristaId == driver.Id)));
        if (resource == "driver")
        {
            var changed = Assert.Single(await db.EventosRastreio.Where(e => e.Titulo == "Atribuição de motorista atualizada").ToListAsync());
            Assert.Equal("Localização não informada na alteração de atribuição", changed.Localidade);
        }
    }

    private static async Task<string> IssueCode(HttpClient client, int id, string token)
    {
        using var response = await client.PostAsync("/Entregas/EmitirCodigoTurno", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = token, ["entregaId"] = id.ToString()
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Regex.Match(await response.Content.ReadAsStringAsync(), "aria-label=\"Código temporário\">([^<]+)</output>");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
