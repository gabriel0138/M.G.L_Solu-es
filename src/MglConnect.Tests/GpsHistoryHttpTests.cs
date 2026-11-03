using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public sealed class GpsHistoryHttpTests
{
    private static async Task<SecurityHttpFixture> Fixture(bool shifts = true)
    {
        var fixture = await SecurityHttpFixture.CreateAsync(activeShifts: shifts);
        await fixture.ChangeAsync(db => { foreach (var shift in db.TurnosMotorista) shift.IniciadoEmUtc = DateTime.UtcNow.AddHours(-2); });
        return fixture;
    }
    private static async Task<GpsEventInput> Event(SecurityHttpFixture fixture, int delivery = 0, DateTimeOffset? at = null)
    {
        await using var db = new MglDbContext(fixture.Options);
        var id = fixture.Deliveries[delivery].Id;
        var shift = await db.TurnosMotorista.Where(t => t.EntregaId == id && t.Ativo).Select(t => (Guid?)t.Id).SingleOrDefaultAsync();
        return new(Guid.NewGuid(), id, shift, -23.5505, -46.6333, 8, at ?? DateTimeOffset.UtcNow, 42, 90);
    }
    private static async Task<JsonElement> Send(SecurityHttpFixture.Session driver, params GpsEventInput[] events)
    {
        using var request = driver.Api(HttpMethod.Post, "/api/gps/sincronizar", new { eventos = events });
        using var response = await driver.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }
    private static JsonElement Receipt(JsonElement result, int index = 0) => result.GetProperty("receipts")[index];

    [Fact]
    public async Task ValidGpsPersistsAuthenticatedActorShiftVehicleAndBothClocksWithoutChangingBusinessVersion()
    {
        await using var fixture = await Fixture(); using var driver = await fixture.LoginAsync("driver-a");
        var input = await Event(fixture, at: DateTimeOffset.UtcNow.AddMinutes(-10));
        var receipt = Receipt(await Send(driver, input)); Assert.Equal("confirmado", receipt.GetProperty("estado").GetString());
        await using var db = new MglDbContext(fixture.Options);
        var saved = await db.PosicoesGps.SingleAsync();
        Assert.Equal(input.EventoId, saved.EventoId); Assert.Equal(fixture.TenantA, saved.TenantId);
        Assert.Equal(fixture.Users["driver-a"].Id, saved.AtorUsuarioId); Assert.Equal(saved.AtorUsuarioId, saved.MotoristaId);
        Assert.Equal(fixture.Deliveries[0].VeiculoId, saved.VeiculoId); Assert.Equal(input.TurnoId, saved.TurnoId);
        Assert.Equal(input.CapturadoEmUtc.UtcDateTime, saved.CapturadoEmUtc); Assert.True(saved.RecebidoEmUtc > saved.CapturadoEmUtc!.Value.AddMinutes(9));
        Assert.Equal(8, saved.PrecisaoMetros); Assert.Equal("GPS", saved.Origem); Assert.True(saved.CapturaInformada);
        var trip = (await db.Entregas.FindAsync(input.EntregaId))!;
        Assert.Equal(fixture.Deliveries[0].Versao, trip.Versao); Assert.Equal(saved.CapturadoEmUtc, trip.UltimoGpsCapturadoEmUtc);
        Assert.Equal(saved.RecebidoEmUtc, trip.UltimoGpsRecebidoEmUtc);
        using var admin = await fixture.LoginAsync("admin-a");
        var positions = await admin.Client.GetFromJsonAsync<JsonElement>("/Operacoes/Posicoes");
        var row = positions.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == input.EntregaId);
        Assert.Equal(input.CapturadoEmUtc.UtcDateTime, row.GetProperty("capturadoEmUtc").GetDateTime());
        Assert.EndsWith("Z", row.GetProperty("capturadoEmUtc").GetString());
    }

    [Fact]
    public async Task PartialInvalidBatchAndDuplicateIdsHaveIndividualReceiptsWithoutCorruptingTheNewestPosition()
    {
        await using var fixture = await Fixture(); using var driver = await fixture.LoginAsync("driver-a");
        var newest = await Event(fixture);
        var old = newest with { EventoId = Guid.NewGuid(), Latitude = -23.7, CapturadoEmUtc = newest.CapturadoEmUtc.AddMinutes(-5) };
        var result = await Send(driver, newest, newest with { EventoId = Guid.NewGuid(), Latitude = 91 }, old,
            newest with { EventoId = Guid.NewGuid(), PrecisaoMetros = -1 }, newest);
        Assert.Equal(new[] { "confirmado", "rejeitado", "confirmado", "rejeitado", "duplicado" }, result.GetProperty("receipts").EnumerateArray().Select(r => r.GetProperty("estado").GetString()));
        Assert.False(Receipt(result, 2).GetProperty("atualizouPosicao").GetBoolean());
        await using var db = new MglDbContext(fixture.Options);
        Assert.Equal(2, await db.PosicoesGps.CountAsync());
        var snapshot = (await db.Entregas.FindAsync(newest.EntregaId))!; Assert.Equal(newest.Latitude, snapshot.UltimaLatitudeGps);
        Assert.Equal(newest.CapturadoEmUtc.UtcDateTime, snapshot.UltimoGpsCapturadoEmUtc);
        using var sameIdDifferentBody = driver.Api(HttpMethod.Post, "/api/gps/sincronizar", new { eventos = new[] { newest with { Latitude = -22 } } });
        using var response = await driver.Client.SendAsync(sameIdDifferentBody);
        Assert.Equal("evento_conflitante", Receipt(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task NoActiveShiftOrWrongShiftCannotRecordGps()
    {
        await using var fixture = await Fixture(false); using var driver = await fixture.LoginAsync("driver-a");
        await fixture.ChangeAsync(db => db.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id).StatusEntrega = "Em transporte");
        var input = await Event(fixture);
        Assert.Equal("turno_indisponivel", Receipt(await Send(driver, input)).GetProperty("motivo").GetString());
        await using var db = new MglDbContext(fixture.Options); Assert.Empty(await db.PosicoesGps.ToListAsync());
    }

    [Fact]
    public async Task OldFuturePreShiftAndSimulatedDeviceEventsAreRejected()
    {
        await using var fixture = await Fixture(); using var driver = await fixture.LoginAsync("driver-a"); var input = await Event(fixture);
        var result = await Send(driver, input with { EventoId = Guid.NewGuid(), CapturadoEmUtc = DateTimeOffset.UtcNow.AddHours(-25) },
            input with { EventoId = Guid.NewGuid(), CapturadoEmUtc = DateTimeOffset.UtcNow.AddMinutes(2) },
            input with { EventoId = Guid.NewGuid(), CapturadoEmUtc = DateTimeOffset.UtcNow.AddHours(-3) },
            input with { EventoId = Guid.NewGuid(), TurnoId = Guid.NewGuid() }, input with { EventoId = Guid.NewGuid(), Simulado = true });
        Assert.All(result.GetProperty("receipts").EnumerateArray(), r => Assert.Equal("rejeitado", r.GetProperty("estado").GetString()));
        await using var db = new MglDbContext(fixture.Options); Assert.Equal(0, await db.PosicoesGps.CountAsync());
    }

    [Fact]
    public async Task OtherDriverOrTenantCannotRecordGpsAndHistoryRemainsPrivateToOwningCustomer()
    {
        await using var fixture = await Fixture(); using var driver = await fixture.LoginAsync("driver-a");
        Assert.Equal("rejeitado", Receipt(await Send(driver, await Event(fixture, 1))).GetProperty("estado").GetString());
        Assert.Equal("rejeitado", Receipt(await Send(driver, await Event(fixture, 2))).GetProperty("estado").GetString());
        var own = await Event(fixture); await Send(driver, own);
        foreach (var (login, allowed) in new[] { ("admin-a",true),("client-a",true),("broker-a",true),("client-a-other",false),("client-b",false),("admin-b",false),("driver-a-other",false) })
        {
            using var actor = await fixture.LoginAsync(login);
            using var response = await actor.Client.GetAsync($"/api/gps/historico/{own.EntregaId}");
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
            if (allowed)
            {
                var html = await response.Content.ReadAsStringAsync(); Assert.Contains(own.EventoId.ToString(), html);
                Assert.DoesNotContain("tenantId", html); Assert.DoesNotContain("motoristaId", html); Assert.DoesNotContain("veiculoId", html); Assert.DoesNotContain("token", html);
            }
        }
        using var anonymous = fixture.Anonymous(); using var denied = await anonymous.GetAsync($"/api/gps/historico/{own.EntregaId}");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task IgnoringTheResponseAndRetryingAfterCompletionStillAcknowledgesOnlyTheOriginalEvent()
    {
        await using var fixture = await Fixture(); using var driver = await fixture.LoginAsync("driver-a"); var input = await Event(fixture);
        await Send(driver, input); // Deliberately discard the receipt; the outbox would still be pending.
        await fixture.ChangeAsync(db =>
        {
            var trip = db.Entregas.Single(e => e.Id == input.EntregaId); trip.StatusEntrega = "Entregue"; trip.AtribuicaoAtiva = false;
            foreach (var shift in db.TurnosMotorista.Where(t => t.EntregaId == input.EntregaId)) shift.Ativo = false;
        });
        Assert.Equal("duplicado", Receipt(await Send(driver, input)).GetProperty("estado").GetString());
        Assert.Equal("rejeitado", Receipt(await Send(driver, input with { EventoId = Guid.NewGuid() })).GetProperty("estado").GetString());
        await using var db = new MglDbContext(fixture.Options); Assert.Equal(1, await db.PosicoesGps.CountAsync());
    }

    [Fact]
    public async Task ConcurrentRetriesHaveOnePersistedEventAndRecoverAnyTransientConflict()
    {
        await using var fixture = await Fixture(); using var one = await fixture.LoginAsync("driver-a"); using var two = await fixture.LoginAsync("driver-a"); var input = await Event(fixture);
        var results = await Task.WhenAll(Send(one,input),Send(two,input));
        Assert.Contains(results, result => Receipt(result).GetProperty("estado").GetString() == "confirmado");
        Assert.Equal("duplicado", Receipt(await Send(two,input)).GetProperty("estado").GetString());
        await using var db = new MglDbContext(fixture.Options); Assert.Equal(1, await db.PosicoesGps.CountAsync());
    }

    [Fact]
    public async Task CommittedHistoryAndIdempotencySurviveAnActualServerRestart()
    {
        await using var fixture = await Fixture(); var input = await Event(fixture);
        using (var driver = await fixture.LoginAsync("driver-a")) await Send(driver,input);
        await fixture.RestartAsync();
        using var reconnected = await fixture.LoginAsync("driver-a");
        Assert.Equal("duplicado", Receipt(await Send(reconnected,input)).GetProperty("estado").GetString());
        using var client = await fixture.LoginAsync("client-a");
        var history = await client.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{input.EntregaId}");
        Assert.Equal(1, history.GetProperty("total").GetInt32());
        Assert.Equal(input.CapturadoEmUtc.UtcDateTime, history.GetProperty("items")[0].GetProperty("capturadoEmUtc").GetDateTime());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StorageLimitsRejectGrowthButNeverRejectAnAlreadyCommittedReceipt(bool perTrip)
    {
        await using var fixture = await Fixture(); var input = await Event(fixture);
        await using var db = new MglDbContext(fixture.Options);
        var service = new GpsIngestionService(db, options: Options.Create(new GpsHistoryOptions { MaximoPorViagem = perTrip?1:20000, MaximoPorOrganizacao = perTrip?1000000:1 }));
        var actor = fixture.Users["driver-a"].Id;
        Assert.Equal("confirmado", (await service.AcceptAsync(actor,input)).Estado);
        Assert.Equal("limite_diario", (await service.AcceptAsync(actor,input with { EventoId = Guid.NewGuid() })).Motivo);
        Assert.Equal("duplicado", (await service.AcceptAsync(actor,input)).Estado);
        Assert.Equal(1, await db.PosicoesGps.CountAsync());
    }

    [Fact]
    public async Task PostRequiresDriverBearerAndRejectsOversizedBatchesWithoutAnyMutation()
    {
        await using var fixture = await Fixture(); using var driver = await fixture.LoginAsync("driver-a"); var input = await Event(fixture);
        using var cookieOnly = await driver.Client.PostAsJsonAsync("/api/gps/sincronizar", new { eventos = new[] {input} }); Assert.Equal(HttpStatusCode.Unauthorized,cookieOnly.StatusCode);
        using var batch = driver.Api(HttpMethod.Post,"/api/gps/sincronizar",new { eventos = Enumerable.Repeat(input,51) });
        using var tooMany = await driver.Client.SendAsync(batch); Assert.Equal(HttpStatusCode.BadRequest,tooMany.StatusCode);
        using var client = await fixture.LoginAsync("client-a"); using var forbidden = client.Api(HttpMethod.Post,"/api/gps/sincronizar",new { eventos = new[] {input} });
        using var denied = await client.Client.SendAsync(forbidden); Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await fixture.ChangeAsync(db=>db.Usuarios.Single(u=>u.Id==fixture.Users["driver-a"].Id).Ativo=false);
        using var inactive = driver.Api(HttpMethod.Post,"/api/gps/sincronizar",new { eventos = new[] {input} });
        using var revoked = await driver.Client.SendAsync(inactive); Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        await using var verify = new MglDbContext(fixture.Options); Assert.Equal(0,await verify.PosicoesGps.CountAsync());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task GenuineReassignmentClearsTheSnapshotButPreservesTheOriginalResourceHistory(bool vehicle)
    {
        await using var fixture = await Fixture(); var trip = fixture.Deliveries[0];
        var otherVehicle = new Veiculo { TenantId = fixture.TenantA, Placa = "GPS1A11", Modelo = "Synthetic alternate vehicle", CapacidadeCargaKg = 1000 };
        var otherDriver = new Usuario { TenantId = fixture.TenantA, Login = "gps-alternate-driver", Nome = "Synthetic alternate driver", Perfil = "Motorista", SenhaHash = "not used" };
        await fixture.ChangeAsync(db => { db.AddRange(otherVehicle,otherDriver); db.Veiculos.Single(v=>v.Id==trip.VeiculoId).CapacidadeCargaKg=1000; });
        using var admin = await fixture.LoginAsync("admin-a");
        await using(var hub = await SecurityHubConnection.ConnectAsync(fixture,admin))
            Assert.False((await hub.InvokeAsync("SendVehicleTelemetry",trip.CodigoRastreio,-23d,-46d,40d,90d,10d,false)).TryGetProperty("error",out _));
        await fixture.ChangeAsync(db => {
            foreach(var shift in db.TurnosMotorista.Where(t=>t.EntregaId==trip.Id))shift.Ativo=false;
            db.Entregas.Single(e=>e.Id==trip.Id).StatusEntrega="Pendente";
        });
        var token = await SecurityHttpFixture.AntiForgeryAsync(admin.Client,$"/Entregas/Details/{trip.Id}");
        var path = vehicle?"/Entregas/AtribuirVeiculo":"/Entregas/AtribuirMotorista";
        foreach(var id in new[] { vehicle?trip.VeiculoId!.Value:trip.MotoristaId!.Value,vehicle?otherVehicle.Id:otherDriver.Id })
        {
            using var response = await admin.Client.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>
            { ["__RequestVerificationToken"]=token,["entregaId"]=trip.Id.ToString(),[vehicle?"veiculoId":"motoristaId"]=id.ToString() }));
            Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
            await using var verify = new MglDbContext(fixture.Options); var saved=(await verify.Entregas.FindAsync(trip.Id))!;
            var unchanged = id == (vehicle?trip.VeiculoId:trip.MotoristaId);
            if(unchanged)Assert.Equal(-23d,saved.UltimaLatitudeGps);else { Assert.Null(saved.UltimaLatitudeGps);Assert.Null(saved.UltimoGpsCapturadoEmUtc);Assert.Null(saved.UltimoGpsRecebidoEmUtc); }
            var history = await admin.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{trip.Id}");
            Assert.Equal(unchanged ? JsonValueKind.Object : JsonValueKind.Null, history.GetProperty("ultimaPosicao").ValueKind);
            var original=await verify.PosicoesGps.SingleAsync();Assert.Null(original.CapturadoEmUtc);Assert.False(original.CapturaInformada);Assert.Equal(trip.VeiculoId,original.VeiculoId);Assert.Equal(trip.MotoristaId,original.MotoristaId);
        }
    }

    [Fact]
    public async Task ConfirmedHistoryIsChronologicalAndPaginatedWithoutReturningAnUnboundedTrack()
    {
        await using var fixture = await Fixture(); var sample = await Event(fixture);
        await fixture.ChangeAsync(db => {
            for(var i=0;i<205;i++)db.PosicoesGps.Add(new PosicaoGps {
                EventoId=Guid.NewGuid(),TenantId=fixture.TenantA,EntregaId=sample.EntregaId,MotoristaId=fixture.Users["driver-a"].Id,
                AtorUsuarioId=fixture.Users["driver-a"].Id,VeiculoId=fixture.Deliveries[0].VeiculoId!.Value,TurnoId=sample.TurnoId,
                Latitude=-23+i*.0001,Longitude=-46,PrecisaoMetros=8,CapturadoEmUtc=sample.CapturadoEmUtc.UtcDateTime.AddSeconds(i-205),
                RecebidoEmUtc=DateTime.UtcNow,Origem="GPS",CapturaInformada=true });
        });
        using var client = await fixture.LoginAsync("client-a");
        var recent=await client.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{sample.EntregaId}");
        var previous=await client.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{sample.EntregaId}?pagina=2");
        Assert.Equal(205,recent.GetProperty("total").GetInt32());Assert.Equal(200,recent.GetProperty("items").GetArrayLength());Assert.Equal(5,previous.GetProperty("items").GetArrayLength());
        var points=recent.GetProperty("items").EnumerateArray().Select(p=>p.GetProperty("capturadoEmUtc").GetDateTime()).ToArray();
        Assert.Equal(points.OrderBy(t=>t),points);
        Assert.True(previous.GetProperty("items")[4].GetProperty("capturadoEmUtc").GetDateTime()<points[0]);
    }

    [Fact]
    public async Task AdditiveUpgradePreservesLegacyRowsAndDoesNotInventGpsHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
        await using var db = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(db);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Entregas VALUES (1, '00000000-0000-0000-0000-000000000000', 1, 'Pendente', 1, 1)");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db); await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        Assert.Equal(0,await db.PosicoesGps.CountAsync());
        Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Entregas WHERE UltimoGpsCapturadoEmUtc IS NULL").SingleAsync());
        Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Clientes").SingleAsync());
        Assert.Equal(5,(await db.Database.SqlQueryRaw<string>("SELECT [table] AS Value FROM pragma_foreign_key_list('PosicoesGps')").ToListAsync()).Count);
    }
}
