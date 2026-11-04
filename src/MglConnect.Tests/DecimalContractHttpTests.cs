using System.Net;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services.RouteTrust;
using Xunit;

namespace MglConnect.Tests;

// SQLite files and Kestrel loopback owned by SecurityHttpFixture only.
public sealed class DecimalContractHttpTests
{
    [Theory, InlineData("pt_BR.UTF-8"), InlineData("en_US.UTF-8"), InlineData("C")]
    public async Task FleetRejectsAmbiguousNumbersWithoutWritesAndRendersExactInvariantValues(string locale)
    {
        await using var f = await SecurityHttpFixture.CreateAsync(hostingConfiguration: Locale(locale));
        using var admin = await f.LoginAsync("admin-a");
        int before, audits;
        await using (var db = new MglDbContext(f.Options))
        { before = await db.Veiculos.CountAsync(); audits = await db.AuditoriaOperacional.CountAsync(); }
        var fields = new[] { "CapacidadeCargaKg", "AlturaMetros", "LarguraMetros", "ComprimentoMetros", "PesoToneladas" };
        foreach (var field in fields)
        {
            var missing = FleetManagementHttpTests.VehicleData(); missing.Remove(field);
            using var rejected = await FleetManagementHttpTests.Post(admin.Client, "/Frota/NovoVeiculo",
                await FleetManagementHttpTests.Form(admin.Client, "/Frota/NovoVeiculo", missing));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        foreach (var field in fields)
        foreach (var value in new[] { "18,60", "1,234.56", "1.234", "7922816251426433759354395033.51", "", "-1", "1e2" })
        {
            var data = FleetManagementHttpTests.VehicleData(); data[field] = value;
            var form = await FleetManagementHttpTests.Form(admin.Client, "/Frota/NovoVeiculo", data);
            using var rejected = await FleetManagementHttpTests.Post(admin.Client, "/Frota/NovoVeiculo", form);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(value, FleetManagementHttpTests.Input(await rejected.Content.ReadAsStringAsync(), field));
        }
        await using (var db = new MglDbContext(f.Options))
        { Assert.Equal(before, await db.Veiculos.CountAsync()); Assert.Equal(audits, await db.AuditoriaOperacional.CountAsync()); }

        var accepted = FleetManagementHttpTests.VehicleData();
        accepted["AlturaMetros"] = "0.01"; accepted["LarguraMetros"] = "999.99";
        using var saved = await FleetManagementHttpTests.Post(admin.Client, "/Frota/NovoVeiculo",
            await FleetManagementHttpTests.Form(admin.Client, "/Frota/NovoVeiculo", accepted));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        await using var verify = new MglDbContext(f.Options);
        var vehicle = await verify.Veiculos.SingleAsync(v => v.Placa == "ABC1D23");
        Assert.Equal(2500.50m, vehicle.CapacidadeCargaKg); Assert.Equal(.01m, vehicle.AlturaMetros);
        Assert.Equal(999.99m, vehicle.LarguraMetros); Assert.Equal(18.60m, vehicle.ComprimentoMetros); Assert.Equal(29.50m, vehicle.PesoToneladas);
        var page = await admin.Client.GetStringAsync($"/Frota/EditarVeiculo/{vehicle.Id}");
        foreach (var field in fields) Assert.Equal(accepted[field], FleetManagementHttpTests.Input(page, field));
        var foreign = f.Deliveries[2].VeiculoId;
        using var hidden = await admin.Client.GetAsync($"/Frota/EditarVeiculo/{foreign}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var forged = FleetManagementHttpTests.VehicleData(); forged["Id"] = foreign!.Value.ToString(CultureInfo.InvariantCulture);
        forged["AlturaMetros"] = "18,60";
        forged["__RequestVerificationToken"] = FleetManagementHttpTests.Input(page, "__RequestVerificationToken");
        using var crossTenant = await FleetManagementHttpTests.Post(admin.Client, $"/Frota/EditarVeiculo/{foreign}", forged);
        // The existing fleet action validates malformed forms before loading the resource.
        // A valid form must still preserve the cross-tenant 404 contract.
        Assert.Equal(HttpStatusCode.BadRequest, crossTenant.StatusCode);
        forged["AlturaMetros"] = "4.20";
        using var crossTenantValid = await FleetManagementHttpTests.Post(admin.Client, $"/Frota/EditarVeiculo/{foreign}", forged);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantValid.StatusCode);
    }

    [Theory, InlineData("pt_BR.UTF-8"), InlineData("en_US.UTF-8"), InlineData("C")]
    public async Task TripMeasuresAndAxleLoadsAreExactNullableAndNeverWrittenOnInvalidInput(string locale)
    {
        await using var f = await SecurityHttpFixture.CreateAsync(hostingConfiguration: Locale(locale));
        using var admin = await f.LoginAsync("admin-a");
        var trip = f.Deliveries[0].Id; var path = $"/Rotas/Entrega/{trip}";
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, path);
        Dictionary<string, string> Data() => new()
        {
            ["__RequestVerificationToken"] = token, ["Revision"] = "0", ["Height"] = "4.20", ["Width"] = "",
            ["Length"] = "18.60", ["Tare"] = "", ["Pbt"] = "30000.02", ["Pbtc"] = "", ["Payload"] = "0",
            ["Axles"] = "2", ["AxleLoadsKg"] = "15000.01;15000.01"
        };
        int audits;
        await using (var db = new MglDbContext(f.Options)) audits = await db.AuditoriaOperacional.CountAsync();
        foreach (var field in new[] { "Height", "Width", "Length", "Tare", "Pbt", "Pbtc", "Payload" })
        foreach (var value in new[] { "18,60", "1,234.56", "1.234", "-1", "1000000.01", "1e2" })
        {
            var data = Data(); data[field] = value;
            using var rejected = await admin.Client.PostAsync(path + "/Perfil", new FormUrlEncodedContent(data));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(value, FleetManagementHttpTests.Input(await rejected.Content.ReadAsStringAsync(), field));
        }
        foreach (var value in new[] { "18,60", "1,234.56", "1.234", "1;;2", "1;", "1; 2", "-1;30001.02", "0;30000.02", "1e2;29900.02" })
        {
            var data = Data(); data["AxleLoadsKg"] = value;
            using var rejected = await admin.Client.PostAsync(path + "/Perfil", new FormUrlEncodedContent(data));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(value, FleetManagementHttpTests.Input(await rejected.Content.ReadAsStringAsync(), "AxleLoadsKg"));
        }
        var fractionalAxles = Data(); fractionalAxles["Axles"] = "2.5";
        using (var rejected = await admin.Client.PostAsync(path + "/Perfil", new FormUrlEncodedContent(fractionalAxles)))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using (var db = new MglDbContext(f.Options))
        {
            var unchanged = await db.Entregas.FindAsync(trip);
            Assert.Null(unchanged!.PerfilViagemJson); Assert.Equal(0, unchanged.PerfilViagemVersao);
            Assert.Empty(await db.PerfisViagemRevisoes.ToListAsync()); Assert.Empty(await db.RotaAvaliacoes.ToListAsync());
            Assert.Equal(audits, await db.AuditoriaOperacional.CountAsync());
        }
        var foreignPath = $"/Rotas/Entrega/{f.Deliveries[2].Id}";
        using (var hidden = await admin.Client.GetAsync(foreignPath)) Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var foreignData = Data(); foreignData["Height"] = "18,60";
        using (var hidden = await admin.Client.PostAsync(foreignPath + "/Perfil", new FormUrlEncodedContent(foreignData)))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var saved = await admin.Client.PostAsync(path + "/Perfil", new FormUrlEncodedContent(Data())))
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        await using var verify = new MglDbContext(f.Options);
        var delivery = await verify.Entregas.FindAsync(trip);
        var profile = OperationalTruckProfile.Read(delivery!.PerfilViagemJson);
        Assert.Equal(4.20m, profile.Height!.Value); Assert.Null(profile.Width); Assert.Null(profile.Tare); Assert.Null(profile.Pbtc);
        Assert.Equal(18.60m, profile.Length!.Value); Assert.Equal(30000.02m, profile.Pbt!.Value); Assert.Equal(0m, profile.Payload!.Value);
        Assert.Equal(new[] { 15000.01m, 15000.01m }, profile.AxleLoads.Select(m => m.Value));
        var revision = Assert.Single(await verify.PerfisViagemRevisoes.ToListAsync());
        Assert.Equal(delivery.PerfilViagemJson, revision.PerfilJson); Assert.Equal(f.TenantA, revision.TenantId);
        var page = await admin.Client.GetStringAsync(path);
        Assert.Equal("4.20", FleetManagementHttpTests.Input(page, "Height"));
        Assert.Equal("", FleetManagementHttpTests.Input(page, "Width"));
        Assert.Equal("15000.01;15000.01", FleetManagementHttpTests.Input(page, "AxleLoadsKg"));
        // The history formats a human-facing measure with CurrentCulture. This
        // proves the child server locale changed while input values stayed invariant.
        Assert.Contains(locale.StartsWith("pt", StringComparison.Ordinal) ? "4,20 m" : "4.20 m", WebUtility.HtmlDecode(page));
    }

    private static IReadOnlyDictionary<string, string> Locale(string locale) => new Dictionary<string, string>
    { ["LANG"] = locale, ["LC_ALL"] = locale };
}
