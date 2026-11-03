using System.Net;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Models;
using Xunit;

namespace MglConnect.Tests;

public class FleetRosterTests
{
    [Fact]
    public async Task TenantAdministratorSeesOnlyTheirCatalogueAndActualAssignmentsWithoutPrivateAccountFields()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db =>
        {
            db.Veiculos.Single(v => v.Id == fixture.Deliveries[0].VeiculoId).Modelo = "FROTA-OWN";
            db.Veiculos.Single(v => v.Id == fixture.Deliveries[2].VeiculoId).Modelo = "FROTA-FOREIGN";
            db.Usuarios.Single(u => u.Login == "driver-b").Nome = "FOREIGN DRIVER";
            var own = db.Usuarios.Single(u => u.Login == "driver-a");
            own.Cpf = "PRIVATE-CPF";
            own.Telefone = "PRIVATE-PHONE";
        });
        using var admin = await fixture.LoginAsync("admin-a");
        foreach (var path in new[] { "/Frota", "/Frota/Painel" })
        {
            using var response = await admin.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("FROTA-OWN", html);
            Assert.Contains($"href=\"/Entregas/Details/{fixture.Deliveries[0].Id}\"", html);
            Assert.Contains("driver-a", html);
            Assert.DoesNotContain("FROTA-FOREIGN", html);
            Assert.DoesNotContain("FOREIGN DRIVER", html);
            Assert.DoesNotContain("PRIVATE-", html);
            Assert.DoesNotContain("SenhaHash", html);
            Assert.DoesNotContain("Disponível para viagem", html);
        }
    }

    [Fact]
    public async Task FiltersDistinguishInactiveUnassignedAndAssignedResourcesUsingPersistedData()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db =>
        {
            db.Veiculos.AddRange(
                new Veiculo { TenantId = fixture.TenantA, Placa = "POOL01", Modelo = "FROTA-SPARE", CapacidadeCargaKg = 1250.5m },
                new Veiculo { TenantId = fixture.TenantA, Placa = "STOP01", Modelo = "FROTA-INACTIVE", Ativo = false });
            db.Usuarios.Add(new Usuario { TenantId = fixture.TenantA, Login = "inactive-roster", Nome = "DRIVER-INACTIVE",
                Perfil = "Motorista", Ativo = false, SenhaHash = "Unused test hash" });
        });
        using var admin = await fixture.LoginAsync("admin-a");
        var spare = await Page(admin.Client, "/Frota?status=sem-atribuicao&termo=frota");
        Assert.Contains("FROTA-SPARE", spare);
        Assert.Contains("1.250,50 kg", WebUtility.HtmlDecode(spare));
        Assert.DoesNotContain("FROTA-INACTIVE", spare);
        Assert.DoesNotContain(fixture.Deliveries[0].CodigoRastreio, spare);
        var inactive = await Page(admin.Client, "/Frota?status=inativos");
        Assert.Contains("FROTA-INACTIVE", inactive);
        Assert.Contains("DRIVER-INACTIVE", inactive);
        Assert.DoesNotContain("FROTA-SPARE", inactive);
        var assigned = await Page(admin.Client, "/Frota?status=atribuidos");
        Assert.Contains(fixture.Deliveries[0].CodigoRastreio, assigned);
        Assert.DoesNotContain("FROTA-SPARE", assigned);
        var empty = await Page(admin.Client, "/Frota?termo=ROSTER-NO-MATCH");
        Assert.Contains("Nenhum veículo encontrado", empty);
        Assert.Contains("Nenhum motorista encontrado", empty);
    }

    [Fact]
    public async Task AuthorizedRefreshReflectsCompletedAssignmentAndEndedShiftWithoutRetainingOldTripLinks()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var delivery = fixture.Deliveries[0];
        Assert.Contains(delivery.CodigoRastreio, await Page(admin.Client, "/Frota/Painel"));
        await fixture.ChangeAsync(db =>
        {
            db.Entregas.Single(e => e.Id == delivery.Id).StatusEntrega = "Entregue";
            foreach (var shift in db.TurnosMotorista.Where(t => t.EntregaId == delivery.Id)) shift.Ativo = false;
        });
        var refreshed = await Page(admin.Client, "/Frota/Painel");
        Assert.DoesNotContain(delivery.CodigoRastreio, refreshed);
        Assert.Contains("Sem atribuição ativa", refreshed);
        var page = await Page(admin.Client, "/Frota");
        Assert.Contains("id=\"operationalPanel\"", page);
        Assert.Contains("/js/operational-dashboard.js", page);
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    [InlineData("driver-a")]
    public async Task NonAdministratorsCannotReadTheCatalogueOrItsRefresh(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var actor = await fixture.LoginAsync(login);
        foreach (var path in new[] { "/Frota", "/Frota/Painel" })
        {
            using var response = await actor.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    private static async Task<string> Page(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
