using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public sealed class FleetManagementHttpTests
{
    internal static Dictionary<string, string> VehicleData() => new()
    {
        ["Modelo"] = "Veículo cadastrado HTTP", ["Placa"] = "ABC-1D23", ["CapacidadeCargaKg"] = "2500.50",
        ["AlturaMetros"] = "4.20", ["LarguraMetros"] = "2.60", ["ComprimentoMetros"] = "18.60",
        ["PesoToneladas"] = "29.50", ["QtdEixos"] = "6", ["TipoCarga"] = "Carga Geral Seca"
    };
    internal static string Input(string html, string name)
    {
        var input = Regex.Matches(html, "<input\\b[^>]*>").Select(m => m.Value)
            .First(tag => Regex.IsMatch(tag, $"name=\"{Regex.Escape(name)}\"", RegexOptions.IgnoreCase));
        return WebUtility.HtmlDecode(Regex.Match(input, "value=\"([^\"]*)\"").Groups[1].Value);
    }
    internal static async Task<Dictionary<string, string>> Form(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(path);
        fields["__RequestVerificationToken"] = Input(html, "__RequestVerificationToken");
        if (html.Contains("name=\"Selo\"")) fields["Selo"] = Input(html, "Selo");
        if (html.Contains("name=\"Id\"")) fields["Id"] = Input(html, "Id");
        return fields;
    }
    internal static async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string, string> data) =>
        await client.PostAsync(path, new FormUrlEncodedContent(data));

    [Fact]
    public async Task VehicleLifecycleNormalizesPlatePreservesTenantAndHistoryAndRejectsDuplicateSubmissions()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var form = await Form(admin.Client, "/Frota/NovoVeiculo", VehicleData());
        form["TenantId"] = fixture.TenantB.ToString(); form["Ativo"] = "false";
        using var created = await Post(admin.Client, "/Frota/NovoVeiculo", form);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var location = created.Headers.Location!.ToString();
        int id;
        await using (var db = new MglDbContext(fixture.Options))
        {
            var v = await db.Veiculos.SingleAsync(v => v.Placa == "ABC1D23"); id = v.Id;
            Assert.Equal(fixture.TenantA, v.TenantId); Assert.True(v.Ativo);
            Assert.Equal(2500.50m, v.CapacidadeCargaKg);
        }
        using var duplicate = await Post(admin.Client, "/Frota/NovoVeiculo", form);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var edit = await Form(admin.Client, $"/Frota/EditarVeiculo/{id}", VehicleData());
        edit["Placa"] = "ABC1D23"; edit["Modelo"] = "Modelo revisado"; edit["CapacidadeCargaKg"] = "3000";
        using var saved = await Post(admin.Client, $"/Frota/EditarVeiculo/{id}", edit);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        foreach (var active in new[] { false, true })
        {
            var page = await admin.Client.GetStringAsync(location);
            using var response = await Post(admin.Client, "/Frota/SituacaoVeiculo", new()
            {
                ["id"] = id.ToString(), ["ativo"] = active.ToString(), ["selo"] = Input(page, "selo"),
                ["__RequestVerificationToken"] = Input(page, "__RequestVerificationToken")
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            await using var db = new MglDbContext(fixture.Options);
            Assert.Equal(active, (await db.Veiculos.FindAsync(id))!.Ativo);
            Assert.Equal("Modelo revisado", (await db.Veiculos.FindAsync(id))!.Modelo);
        }
        await using var verify = new MglDbContext(fixture.Options);
        Assert.Equal(1, await verify.Veiculos.CountAsync(v => v.Placa == "ABC1D23"));
        Assert.Equal(4, await verify.AuditoriaOperacional.CountAsync(a => a.TipoEntidade == "Veiculo" && a.IdentificadorEntidade == id.ToString()));
    }

    [Fact]
    public async Task InvalidVehicleFieldsAndCanonicalDuplicatePlatesCannotBePersisted()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        foreach (var (field, value) in new[] { ("Placa", "INVALID"), ("Modelo", " "), ("CapacidadeCargaKg", "0"),
            ("CapacidadeCargaKg", "1.001"), ("AlturaMetros", "-2"), ("PesoToneladas", "1"), ("QtdEixos", "0") })
        {
            var data = VehicleData(); data[field] = value;
            var form = await Form(admin.Client, "/Frota/NovoVeiculo", data);
            using var rejected = await Post(admin.Client, "/Frota/NovoVeiculo", form);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        await fixture.ChangeAsync(db => db.Veiculos.Add(new Veiculo { TenantId = fixture.TenantB, Placa = "abc-1d23", Modelo = "Foreign private model" }));
        using var duplicate = await Post(admin.Client, "/Frota/NovoVeiculo", await Form(admin.Client, "/Frota/NovoVeiculo", VehicleData()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.DoesNotContain("Foreign private model", await duplicate.Content.ReadAsStringAsync());
        await using var db = new MglDbContext(fixture.Options);
        Assert.Equal(0, await db.Veiculos.CountAsync(v => v.Modelo == "Veículo cadastrado HTTP"));
    }

    [Fact]
    public async Task DriverCreationUsesExistingAuthenticationAndCatalogueEditsDoNotExposeOrReplaceCredentials()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        var data = new Dictionary<string,string> { ["Nome"] = "Motorista gerenciado", ["Login"] = "NEW.Driver", ["Telefone"] = "+55 11 99999-9999", ["SenhaInicial"] = "Synthetic-driver-only!", ["Perfil"] = "Admin", ["TenantId"] = fixture.TenantB.ToString() };
        using var created = await Post(admin.Client, "/Frota/NovoMotorista", await Form(admin.Client, "/Frota/NovoMotorista", data));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        int id;
        await using (var db = new MglDbContext(fixture.Options))
        {
            var driver = await db.Usuarios.SingleAsync(u => u.Login == "new.driver"); id = driver.Id;
            Assert.Equal(fixture.TenantA, driver.TenantId); Assert.Equal("Motorista", driver.Perfil); Assert.Null(driver.ClienteId);
            Assert.True(new SimplePasswordHasher().VerifyPassword("Synthetic-driver-only!", driver.SenhaHash));
        }
        using var driverClient = fixture.Anonymous();
        using var login = await driverClient.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(driverClient, "/Account/Login"),
            ["Login"] = "NEW.DRIVER", ["Senha"] = "Synthetic-driver-only!"
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Entregas", login.Headers.Location!.ToString());
        var edit = await Form(admin.Client, $"/Frota/EditarMotorista/{id}", new() { ["Nome"] = "Nome revisado", ["Login"] = "new.driver", ["Telefone"] = "11988887777", ["Perfil"] = "Admin", ["SenhaHash"] = "FORGED" });
        using var saved = await Post(admin.Client, $"/Frota/EditarMotorista/{id}", edit);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var details = await admin.Client.GetStringAsync(created.Headers.Location);
        Assert.Contains("Nome revisado", details);
        Assert.DoesNotContain("Synthetic-driver-only!", details); Assert.DoesNotContain("SenhaHash", details);
        using var inactive = await Post(admin.Client, "/Frota/SituacaoMotorista", new() { ["id"] = id.ToString(), ["ativo"] = "false", ["selo"] = Input(details, "selo"), ["__RequestVerificationToken"] = Input(details, "__RequestVerificationToken") });
        using var revoked = await driverClient.GetAsync("/Entregas");
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode); Assert.Equal("/Account/Login", revoked.Headers.Location!.ToString());
        details = await admin.Client.GetStringAsync(created.Headers.Location);
        using var reactivated = await Post(admin.Client, "/Frota/SituacaoMotorista", new() { ["id"] = id.ToString(), ["ativo"] = "true", ["selo"] = Input(details, "selo"), ["__RequestVerificationToken"] = Input(details, "__RequestVerificationToken") });
        await using var verify = new MglDbContext(fixture.Options);
        var account = (await verify.Usuarios.FindAsync(id))!;
        Assert.True(account.Ativo); Assert.Equal("Motorista", account.Perfil);
        Assert.True(new SimplePasswordHasher().VerifyPassword("Synthetic-driver-only!", account.SenhaHash));
    }

    [Fact]
    public async Task InvalidOrRepeatedDriverCreationNeverEchoesPasswordOrAddsAnotherAccount()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        foreach (var (name, login, password) in new[] { (" ", "new.driver", "DO-NOT-ECHO-password"), ("Test", "bad login", "DO-NOT-ECHO-password"), ("Test", "new.driver", "short") })
        {
            using var invalid = await Post(admin.Client, "/Frota/NovoMotorista", await Form(admin.Client, "/Frota/NovoMotorista", new() { ["Nome"] = name, ["Login"] = login, ["SenhaInicial"] = password }));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.DoesNotContain(password, await invalid.Content.ReadAsStringAsync());
        }
        using var duplicate = await Post(admin.Client, "/Frota/NovoMotorista", await Form(admin.Client, "/Frota/NovoMotorista", new() { ["Nome"] = "Test", ["Login"] = "DRIVER-A", ["SenhaInicial"] = "DO-NOT-ECHO-password" }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.DoesNotContain("DO-NOT-ECHO-password", await duplicate.Content.ReadAsStringAsync());
        await using var db = new MglDbContext(fixture.Options);
        Assert.False(await db.Usuarios.AnyAsync(u => u.Login == "new.driver"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ActiveOrPendingTripsBlockInactivationAndOperationalEditsButAllowDescriptionsWithoutRevokingShiftCodes(bool activeShift)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: activeShift);
        var trip = fixture.Deliveries[0];
        await fixture.ChangeAsync(db => { db.Veiculos.Single(v => v.Id == trip.VeiculoId).CapacidadeCargaKg = 2500.50m; if (!activeShift) db.Entregas.Single(e => e.Id == trip.Id).StatusEntrega = "Pendente"; });
        using var admin = await fixture.LoginAsync("admin-a");
        if (!activeShift)
        {
            using var issued = await Post(admin.Client, "/Entregas/EmitirCodigoTurno", new() { ["entregaId"] = trip.Id.ToString(), ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, $"/Entregas/Details/{trip.Id}") });
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        }
        foreach (var (kind, id) in new[] { ("Veiculo", trip.VeiculoId!.Value), ("Motorista", trip.MotoristaId!.Value) })
        {
            var details = await admin.Client.GetStringAsync($"/Frota/{kind}/{id}");
            Assert.Contains("Inativação bloqueada", WebUtility.HtmlDecode(details));
            using var blocked = await Post(admin.Client, $"/Frota/Situacao{kind}", new() { ["id"] = id.ToString(), ["ativo"] = "false", ["selo"] = Input(details, "selo"), ["__RequestVerificationToken"] = Input(details, "__RequestVerificationToken") });
        }
        var vehicleForm = await Form(admin.Client, $"/Frota/EditarVeiculo/{trip.VeiculoId}", VehicleData());
        vehicleForm["Placa"] = fixture.Deliveries[0].Veiculo!.Placa;
        vehicleForm["CapacidadeCargaKg"] = "1000";
        using var incompatible = await Post(admin.Client, $"/Frota/EditarVeiculo/{trip.VeiculoId}", vehicleForm);
        Assert.Equal(HttpStatusCode.Conflict, incompatible.StatusCode);
        vehicleForm["CapacidadeCargaKg"] = "2500.50";
        using var modelEdit = await Post(admin.Client, $"/Frota/EditarVeiculo/{trip.VeiculoId}", vehicleForm);
        Assert.Equal(HttpStatusCode.Redirect, modelEdit.StatusCode);
        var driverForm = await Form(admin.Client, $"/Frota/EditarMotorista/{trip.MotoristaId}", new() { ["Nome"] = "Nome atualizado durante operação", ["Login"] = "driver-a", ["Telefone"] = "11999998888" });
        using var nameEdit = await Post(admin.Client, $"/Frota/EditarMotorista/{trip.MotoristaId}", driverForm);
        Assert.Equal(HttpStatusCode.Redirect, nameEdit.StatusCode);
        await using var db = new MglDbContext(fixture.Options);
        Assert.True((await db.Veiculos.FindAsync(trip.VeiculoId))!.Ativo); Assert.True((await db.Usuarios.FindAsync(trip.MotoristaId))!.Ativo);
        Assert.Equal(trip.Versao, (await db.Entregas.FindAsync(trip.Id))!.Versao);
        Assert.False(await db.CodigosTurnoMotorista.AnyAsync(c => c.EntregaId == trip.Id && c.CanceladoEmUtc != null));
        Assert.Equal(activeShift ? 1 : 0, await db.TurnosMotorista.CountAsync(t => t.EntregaId == trip.Id && t.Ativo));
    }

    [Theory]
    [InlineData("Veiculo")] [InlineData("Motorista")]
    public async Task StaleTamperedAndConcurrentFormsCannotOverwriteAnotherOperatorsChanges(string kind)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        using var first = await fixture.LoginAsync("admin-a"); using var second = await fixture.LoginAsync("admin-a");
        var data = kind == "Veiculo" ? VehicleData() : new() { ["Nome"] = "New driver", ["Login"] = "new-driver", ["SenhaInicial"] = "Synthetic-driver-only!" };
        using var created = await Post(first.Client, $"/Frota/Novo{kind}", await Form(first.Client, $"/Frota/Novo{kind}", data));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = int.Parse(created.Headers.Location!.ToString().Split('/').Last());
        var editData = kind == "Veiculo" ? VehicleData() : new() { ["Nome"] = "Original", ["Login"] = "new-driver" };
        var one = await Form(first.Client, $"/Frota/Editar{kind}/{id}", new(editData));
        var two = await Form(second.Client, $"/Frota/Editar{kind}/{id}", new(editData));
        var key = kind == "Veiculo" ? "Modelo" : "Nome";
        one[key] = "Concurrent winner one"; two[key] = "Concurrent winner two";
        var responses = await Task.WhenAll(Post(first.Client, $"/Frota/Editar{kind}/{id}", one), Post(second.Client, $"/Frota/Editar{kind}/{id}", two));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Redirect)); Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var r in responses) r.Dispose();
        using var stale = await Post(first.Client, $"/Frota/Editar{kind}/{id}", one);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var tampered = await Form(first.Client, $"/Frota/Editar{kind}/{id}", editData); tampered["Selo"] = "forged";
        using var forged = await Post(first.Client, $"/Frota/Editar{kind}/{id}", tampered);
        Assert.Equal(HttpStatusCode.Conflict, forged.StatusCode);
    }

    [Fact]
    public async Task ServerPaginationKeepsStableFiltersSeparatePagesAndOnlyTheRequestedTwentyRows()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db =>
        {
            for (var n = 0; n < 25; n++)
            {
                db.Veiculos.Add(new Veiculo { TenantId = fixture.TenantA, Placa = $"PAGE{n:000}", Modelo = $"PAGE-TEST-{n:00}" });
                db.Usuarios.Add(new Usuario { TenantId = fixture.TenantA, Login = $"page-{n:00}", Nome = $"PAGE-TEST-{n:00}", Perfil = "Motorista", SenhaHash = "not used", Ativo = n != 24 });
            }
        });
        using var admin = await fixture.LoginAsync("admin-a");
        var pageOne = await admin.Client.GetStringAsync("/Frota?termo=page-test");
        Assert.Contains("PAGE-TEST-19", pageOne); Assert.DoesNotContain("PAGE-TEST-20", pageOne);
        Assert.Contains("paginaVeiculos=2", pageOne); Assert.Contains("paginaMotoristas=2", pageOne);
        var pageTwo = await admin.Client.GetStringAsync("/Frota?termo=page-test&paginaVeiculos=2&paginaMotoristas=2");
        Assert.Contains("PAGE-TEST-24", pageTwo); Assert.DoesNotContain("PAGE-TEST-19", pageTwo);
        Assert.Contains("paginaVeiculos=2", WebUtility.HtmlDecode(pageTwo));
        var filtered = await admin.Client.GetStringAsync("/Frota?termo=page-test&status=inativos&paginaVeiculos=999&paginaMotoristas=-2");
        Assert.Contains("PAGE-TEST-24", filtered); Assert.DoesNotContain("PAGE-TEST-23", filtered);
        Assert.Contains("Página 1 de 1", WebUtility.HtmlDecode(filtered));
    }

    [Theory]
    [InlineData("Veiculo")] [InlineData("Motorista")]
    public async Task ForeignResourcesCannotBeReadEditedOrInactivatedAndForgeryIsRejected(string kind)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(); using var admin = await fixture.LoginAsync("admin-a");
        var foreign = kind == "Veiculo" ? fixture.Deliveries[2].VeiculoId!.Value : fixture.Deliveries[2].MotoristaId!.Value;
        foreach (var path in new[] { $"/Frota/{kind}/{foreign}", $"/Frota/Editar{kind}/{foreign}" })
        { using var response = await admin.Client.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client, "/Frota");
        var data = kind == "Veiculo" ? VehicleData() : new() { ["Nome"] = "Forged", ["Login"] = "driver-b" };
        data["__RequestVerificationToken"] = token; data["Id"] = foreign.ToString();
        using var changed = await Post(admin.Client, $"/Frota/Editar{kind}/{foreign}", data);
        Assert.Equal(HttpStatusCode.NotFound, changed.StatusCode);
        using var inactive = await Post(admin.Client, $"/Frota/Situacao{kind}", new() { ["id"] = foreign.ToString(), ["ativo"] = "false", ["selo"] = "foreign", ["__RequestVerificationToken"] = token });
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        using var noCsrf = await Post(admin.Client, $"/Frota/Novo{kind}", new());
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Theory]
    [InlineData("Veiculo")] [InlineData("Motorista")]
    public async Task DeactivationRacingAReassignmentNeverLeavesAnInactiveResourceReserved(string kind)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        await fixture.ChangeAsync(db => db.Veiculos.Single(v => v.Id == fixture.Deliveries[0].VeiculoId).CapacidadeCargaKg = 5000);
        using var one = await fixture.LoginAsync("admin-a"); using var two = await fixture.LoginAsync("admin-a");
        var data = kind == "Veiculo" ? VehicleData() : new Dictionary<string,string> { ["Nome"] = "Race driver", ["Login"] = "race.driver", ["SenhaInicial"] = "Synthetic-only-password!" };
        using var created = await Post(one.Client, $"/Frota/Novo{kind}", await Form(one.Client, $"/Frota/Novo{kind}", data));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = int.Parse(created.Headers.Location!.ToString().Split('/').Last());
        var page = await one.Client.GetStringAsync(created.Headers.Location);
        var trip = fixture.Deliveries[0];
        var status = Post(one.Client, $"/Frota/Situacao{kind}", new() { ["id"] = id.ToString(), ["ativo"] = "false", ["selo"] = Input(page, "selo"), ["__RequestVerificationToken"] = Input(page, "__RequestVerificationToken") });
        var assign = Post(two.Client, kind == "Veiculo" ? "/Entregas/AtribuirVeiculo" : "/Entregas/AtribuirMotorista", new()
        {
            ["entregaId"] = trip.Id.ToString(), [kind == "Veiculo" ? "veiculoId" : "motoristaId"] = id.ToString(),
            ["__RequestVerificationToken"] = await SecurityHttpFixture.AntiForgeryAsync(two.Client, $"/Entregas/Details/{trip.Id}")
        });
        var results = await Task.WhenAll(status, assign);
        foreach (var result in results) { using (result) Assert.Equal(HttpStatusCode.Redirect, result.StatusCode); }
        await using var db = new MglDbContext(fixture.Options);
        var saved = (await db.Entregas.FindAsync(trip.Id))!;
        var assigned = kind == "Veiculo" ? saved.VeiculoId == id : saved.MotoristaId == id;
        var active = kind == "Veiculo" ? (await db.Veiculos.FindAsync(id))!.Ativo : (await db.Usuarios.FindAsync(id))!.Ativo;
        Assert.Equal(assigned, active); // One mutation wins; both cannot succeed together.
    }

    [Fact]
    public async Task DispatchChoicesExcludeInactivePartialReservationsAndOtherShiftsButKeepOwnAssignment()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: false);
        var trip = fixture.Deliveries[0];
        await fixture.ChangeAsync(db =>
        {
            db.Veiculos.AddRange(new Veiculo { TenantId = fixture.TenantA, Placa = "FREE001", Modelo = "ELIGIBLE-VEHICLE", CapacidadeCargaKg = 5000 },
                new Veiculo { TenantId = fixture.TenantA, Placa = "STOP002", Modelo = "INACTIVE-VEHICLE", Ativo = false });
            db.Usuarios.AddRange(new Usuario { TenantId = fixture.TenantA, Login = "free-choice", Nome = "ELIGIBLE-DRIVER", Perfil = "Motorista", SenhaHash = "unused" },
                new Usuario { TenantId = fixture.TenantA, Login = "stop-choice", Nome = "INACTIVE-DRIVER", Perfil = "Motorista", SenhaHash = "unused", Ativo = false });
            var partial = db.Entregas.Single(e => e.Id == fixture.Deliveries[1].Id);
            partial.VeiculoId = null; partial.AtribuicaoAtiva = false; partial.StatusEntrega = "Pendente";
            db.Usuarios.Single(u => u.Id == partial.MotoristaId).Nome = "PARTIAL-RESERVED-DRIVER";
            db.Veiculos.Single(v => v.Id == trip.VeiculoId).Modelo = "OWN-ASSIGNMENT-VEHICLE";
        });
        using var admin = await fixture.LoginAsync("admin-a");
        var create = WebUtility.HtmlDecode(await admin.Client.GetStringAsync("/Entregas/Create"));
        Assert.Contains("ELIGIBLE-VEHICLE", create); Assert.Contains("ELIGIBLE-DRIVER", create);
        Assert.DoesNotContain("OWN-ASSIGNMENT-VEHICLE", create); Assert.DoesNotContain("INACTIVE-VEHICLE", create);
        Assert.DoesNotContain("INACTIVE-DRIVER", create); Assert.DoesNotContain("PARTIAL-RESERVED-DRIVER", create);
        var detail = WebUtility.HtmlDecode(await admin.Client.GetStringAsync($"/Entregas/Details/{trip.Id}"));
        Assert.Contains("OWN-ASSIGNMENT-VEHICLE", detail); Assert.DoesNotContain("PARTIAL-RESERVED-DRIVER", detail);
        Assert.DoesNotContain("INACTIVE-VEHICLE", detail);
    }

    [Fact]
    public async Task FinishedTripsRemainInHistoryAndOrphanActiveShiftsStillBlockInactivation()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(); var trip = fixture.Deliveries[0];
        await fixture.ChangeAsync(db => { var e = db.Entregas.Single(e => e.Id == trip.Id); e.StatusEntrega = "Entregue"; e.AtribuicaoAtiva = false; });
        using var admin = await fixture.LoginAsync("admin-a");
        foreach (var (kind, id) in new[] { ("Veiculo", trip.VeiculoId!.Value), ("Motorista", trip.MotoristaId!.Value) })
        {
            var page = await admin.Client.GetStringAsync($"/Frota/{kind}/{id}");
            Assert.Contains(trip.CodigoRastreio, page); Assert.Contains("Inativação bloqueada", WebUtility.HtmlDecode(page));
            await fixture.ChangeAsync(db => { foreach (var shift in db.TurnosMotorista.Where(t => t.EntregaId == trip.Id)) { shift.Ativo = false; shift.EncerradoEmUtc = DateTime.UtcNow; } });
            page = await admin.Client.GetStringAsync($"/Frota/{kind}/{id}");
            using var inactive = await Post(admin.Client, $"/Frota/Situacao{kind}", new() { ["id"] = id.ToString(), ["ativo"] = "false", ["selo"] = Input(page, "selo"), ["__RequestVerificationToken"] = Input(page, "__RequestVerificationToken") });
            var history = await admin.Client.GetStringAsync($"/Frota/{kind}/{id}");
            Assert.Contains(trip.CodigoRastreio, history); Assert.Contains("Encerrado", history); Assert.Contains("Inativo", history);
            // Restore the synthetic shift for the next resource's independent safety check.
            await fixture.ChangeAsync(db => { foreach (var shift in db.TurnosMotorista.Where(t => t.EntregaId == trip.Id)) shift.Ativo = true; });
        }
        await using var verify = new MglDbContext(fixture.Options);
        Assert.NotNull(await verify.Entregas.FindAsync(trip.Id)); Assert.NotNull(await verify.Veiculos.FindAsync(trip.VeiculoId)); Assert.NotNull(await verify.Usuarios.FindAsync(trip.MotoristaId));
    }

    [Fact]
    public async Task PortugueseSearchIsCaseInsensitiveWithoutMovingFilteringOutOfTheDatabase()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db =>
        {
            db.Veiculos.Add(new Veiculo { TenantId = fixture.TenantA, Placa = "ACC0001", Modelo = "CAMINHÃO ÁGUA" });
            db.Usuarios.Add(new Usuario { TenantId = fixture.TenantA, Login = "accent-driver", Nome = "ÉRICA ÇAVES", Perfil = "Motorista", SenhaHash = "unused" });
        });
        using var admin = await fixture.LoginAsync("admin-a");
        Assert.Contains("CAMINHÃO ÁGUA", WebUtility.HtmlDecode(await admin.Client.GetStringAsync("/Frota?termo=" + Uri.EscapeDataString("caminhão água"))));
        Assert.Contains("ÉRICA ÇAVES", WebUtility.HtmlDecode(await admin.Client.GetStringAsync("/Frota?termo=" + Uri.EscapeDataString("érica çaves"))));
    }

    [Fact]
    public async Task DescriptiveEditsPreserveUnknownLegacyCapacityAndMalformedStatusCannotInactivate()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(); var trip = fixture.Deliveries[0];
        using var admin = await fixture.LoginAsync("admin-a");
        var data = VehicleData(); data["Placa"] = trip.Veiculo!.Placa; data["CapacidadeCargaKg"] = "0"; data["Modelo"] = "Legacy description only";
        using var edited = await Post(admin.Client, $"/Frota/EditarVeiculo/{trip.VeiculoId}", await Form(admin.Client, $"/Frota/EditarVeiculo/{trip.VeiculoId}", data));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        var page = await admin.Client.GetStringAsync(edited.Headers.Location);
        Assert.Contains("Não informada", WebUtility.HtmlDecode(page));
        using var malformed = await Post(admin.Client, "/Frota/SituacaoVeiculo", new() { ["id"] = trip.VeiculoId.ToString()!, ["ativo"] = "wrong", ["selo"] = Input(page, "selo"), ["__RequestVerificationToken"] = Input(page, "__RequestVerificationToken") });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        await using var db = new MglDbContext(fixture.Options);
        var vehicle = (await db.Veiculos.FindAsync(trip.VeiculoId))!;
        Assert.True(vehicle.Ativo); Assert.Equal(0m, vehicle.CapacidadeCargaKg); Assert.Equal("Legacy description only", vehicle.Modelo);
    }

    [Theory]
    [InlineData("client-a")] [InlineData("driver-a")] [InlineData("broker-a")]
    public async Task NonAdministratorsCannotUseAnyManagementRoute(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(); using var actor = await fixture.LoginAsync(login);
        foreach (var kind in new[] { "Veiculo", "Motorista" })
        {
            foreach (var path in new[] { $"/Frota/Novo{kind}", $"/Frota/Editar{kind}/1", $"/Frota/{kind}/1" })
            { using var denied = await actor.Client.GetAsync(path); Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode); Assert.Contains("AccessDenied", denied.Headers.Location!.ToString()); }
            foreach (var path in new[] { $"/Frota/Novo{kind}", $"/Frota/Editar{kind}/1", $"/Frota/Situacao{kind}" })
            { using var denied = await Post(actor.Client, path, new()); Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode); Assert.Contains("AccessDenied", denied.Headers.Location!.ToString()); }
        }
    }
}
