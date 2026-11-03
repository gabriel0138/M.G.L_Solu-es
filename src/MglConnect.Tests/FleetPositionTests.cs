using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public class FleetPositionTests
{
    [Fact]
    public async Task AcceptedDriverGpsSurvivesContextAndIsVisibleOnlyToItsTenantAdministrator()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var driver = await fixture.LoginAsync("driver-a");
        using var adminA = await fixture.LoginAsync("admin-a");
        using var adminB = await fixture.LoginAsync("admin-b");
        var delivery = fixture.Deliveries[0];
        var before = DateTime.UtcNow;
        await using (var hub = await SecurityHubConnection.ConnectAsync(fixture, driver))
        {
            var result = await hub.InvokeAsync("SendVehicleTelemetry", delivery.CodigoRastreio,
                -23.5505d, -46.6333d, 42.5d, 95d, 12d, false);
            Assert.False(result.TryGetProperty("error", out _));
        }
        var after = DateTime.UtcNow;

        using var response = await adminA.Client.GetAsync("/Operacoes/Posicoes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        var item = Assert.Single(items, item => item.GetProperty("id").GetInt32() == delivery.Id);
        Assert.Equal(delivery.CodigoRastreio, item.GetProperty("codigoRastreio").GetString());
        Assert.Equal(-23.5505d, item.GetProperty("latitude").GetDouble());
        Assert.Equal(-46.6333d, item.GetProperty("longitude").GetDouble());
        Assert.Equal(12d, item.GetProperty("precisaoMetros").GetDouble());
        Assert.Equal(42.5d, item.GetProperty("velocidadeKmH").GetDouble());
        Assert.Equal(95d, item.GetProperty("direcao").GetDouble());
        Assert.Equal("driver-a", item.GetProperty("motorista").GetString());
        Assert.Equal("Fixture", item.GetProperty("veiculo").GetString());
        Assert.EndsWith("Z", item.GetProperty("recebidoEmUtc").GetString());
        Assert.InRange(item.GetProperty("recebidoEmUtc").GetDateTime(), before, after);
        Assert.EndsWith("Z", json.RootElement.GetProperty("consultadoEmUtc").GetString());
        var waiting = Assert.Single(items, row => row.GetProperty("id").GetInt32() == fixture.Deliveries[1].Id);
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("latitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("recebidoEmUtc").ValueKind);

        // A completely new DbContext sees the committed snapshot; no hub-memory fallback.
        await using var persisted = new MglDbContext(fixture.Options);
        var snapshot = await persisted.Entregas.AsNoTracking().SingleAsync(e => e.Id == delivery.Id);
        Assert.Equal(-23.5505d, snapshot.UltimaLatitudeGps);
        Assert.Equal(12d, snapshot.UltimaPrecisaoGps);
        Assert.Equal(delivery.Versao, snapshot.Versao);
        Assert.Equal("Em transporte", snapshot.StatusEntrega);
        Assert.True(snapshot.AtribuicaoAtiva);

        using var otherResponse = await adminB.Client.GetAsync("/Operacoes/Posicoes");
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        var otherJson = await otherResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(delivery.CodigoRastreio, otherJson);
        Assert.Contains(fixture.Deliveries[2].CodigoRastreio, otherJson);
        Assert.DoesNotContain("-23.5505", otherJson);
    }

    [Fact]
    public async Task SimulationAndImpreciseGpsNeverOverwriteTheLastRealSnapshot()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var admin = await fixture.LoginAsync("admin-a");
        await using var hub = await SecurityHubConnection.ConnectAsync(fixture, admin);
        var code = fixture.Deliveries[0].CodigoRastreio;
        Assert.False((await hub.InvokeAsync("SendVehicleTelemetry", code, -23d, -46d, 40d, 90d, 10d, false)).TryGetProperty("error", out _));

        DateTime? acceptedAt;
        await using (var before = new MglDbContext(fixture.Options))
            acceptedAt = (await before.Entregas.AsNoTracking().SingleAsync(e => e.CodigoRastreio == code)).UltimoGpsRecebidoEmUtc;
        Assert.NotNull(acceptedAt);

        Assert.False((await hub.InvokeAsync("SendVehicleTelemetry", code, -23.001d, -46.001d, 60d, 120d, 10d, true)).TryGetProperty("error", out _));
        Assert.False((await hub.InvokeAsync("SendVehicleTelemetry", code, -23.002d, -46.002d, 80d, 140d, 100d, false)).TryGetProperty("error", out _));
        await using var after = new MglDbContext(fixture.Options);
        var snapshot = await after.Entregas.AsNoTracking().SingleAsync(e => e.CodigoRastreio == code);
        Assert.Equal(-23d, snapshot.UltimaLatitudeGps);
        Assert.Equal(-46d, snapshot.UltimaLongitudeGps);
        Assert.Equal(40d, snapshot.UltimaVelocidadeGps);
        Assert.Equal(90d, snapshot.UltimaDirecaoGps);
        Assert.Equal(10d, snapshot.UltimaPrecisaoGps);
        Assert.Equal(acceptedAt, snapshot.UltimoGpsRecebidoEmUtc);
    }

    [Fact]
    public async Task CompletedCancelledAndInactiveAssignmentsAreExcludedAndCannotReceiveNewSnapshots()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        await fixture.ChangeAsync(db =>
        {
            var completed = db.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id);
            completed.StatusEntrega = "Entregue";
            completed.AtribuicaoAtiva = false;
            var cancelled = db.Entregas.Single(e => e.Id == fixture.Deliveries[1].Id);
            cancelled.StatusEntrega = "Cancelada";
            cancelled.AtribuicaoAtiva = false;
        });
        using var admin = await fixture.LoginAsync("admin-a");
        await using var hub = await SecurityHubConnection.ConnectAsync(fixture, admin);
        foreach (var delivery in fixture.Deliveries.Take(2))
            Assert.True((await hub.InvokeAsync("SendVehicleTelemetry", delivery.CodigoRastreio, -23d, -46d, 20d, 90d, 10d, false)).TryGetProperty("error", out _));
        using var response = await admin.Client.GetAsync("/Operacoes/Posicoes");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        await using var db = new MglDbContext(fixture.Options);
        Assert.All(await db.Entregas.Where(e => e.TenantId == fixture.TenantA).ToListAsync(),
            delivery => Assert.Null(delivery.UltimoGpsRecebidoEmUtc));

        // Also exclude a still-pending delivery whose assignment was explicitly deactivated.
        await fixture.ChangeAsync(database =>
        {
            var pending = database.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id);
            pending.StatusEntrega = "Pendente";
        });
        Assert.True((await hub.InvokeAsync("SendVehicleTelemetry", fixture.Deliveries[0].CodigoRastreio, -23d, -46d, 20d, 90d, 10d, false)).TryGetProperty("error", out _));
        using var inactiveResponse = await admin.Client.GetAsync("/Operacoes/Posicoes");
        using var inactiveJson = JsonDocument.Parse(await inactiveResponse.Content.ReadAsStringAsync());
        Assert.Empty(inactiveJson.RootElement.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("client-a")]
    [InlineData("broker-a")]
    [InlineData("driver-a")]
    public async Task NonAdministratorsCannotReadOperationalMapOrPositions(string login)
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync();
        using var session = await fixture.LoginAsync(login);
        foreach (var path in new[] { "/Operacoes", "/Operacoes/Posicoes" })
        {
            using var response = await session.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task SnapshotRecorderKeepsTenantFilterAndBusinessVersion()
    {
        await using var fixture = await SecurityHttpFixture.CreateAsync(startServer: false);
        await fixture.ChangeAsync(db => db.Entregas.Single(e => e.Id == fixture.Deliveries[0].Id).Versao = 7);
        await using var scoped = new MglDbContext(fixture.Options, new BoundTenant(fixture.TenantA));
        var recorder = new DeliveryPositionRecorder(scoped);
        Assert.Null(await recorder.RecordAsync(fixture.Deliveries[2].CodigoRastreio, -23d, -46d, 40d, 90d, 10d, false));
        Assert.NotNull(await recorder.RecordAsync(fixture.Deliveries[0].CodigoRastreio, -23d, -46d, 40d, 90d, 10d, false));
        await using var verify = new MglDbContext(fixture.Options);
        var own = await verify.Entregas.SingleAsync(e => e.Id == fixture.Deliveries[0].Id);
        var foreign = await verify.Entregas.SingleAsync(e => e.Id == fixture.Deliveries[2].Id);
        Assert.Equal(7, own.Versao);
        Assert.Equal(-23d, own.UltimaLatitudeGps);
        Assert.Null(foreign.UltimaLatitudeGps);
    }

    [Fact]
    public async Task AdditiveGpsColumnsUpgradeLegacySqliteIdempotentlyWithoutInventingPositions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(db);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE Entregas ADD Versao INTEGER NOT NULL DEFAULT 0;
            INSERT INTO Entregas (Id, PedidoId, TenantId, StatusEntrega, MotoristaId, VeiculoId, Versao)
                VALUES (1, 1, '00000000-0000-0000-0000-000000000000', 'Pendente', 1, 1, 7);
            """);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        var gpsColumns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Entregas') WHERE name IN ('UltimaLatitudeGps','UltimaLongitudeGps','UltimaPrecisaoGps','UltimaVelocidadeGps','UltimaDirecaoGps','UltimoGpsRecebidoEmUtc')").ToListAsync();
        Assert.Equal(6, gpsColumns.Count);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Entregas").SingleAsync());
        Assert.Equal(7, await db.Database.SqlQueryRaw<int>("SELECT Versao AS Value FROM Entregas WHERE Id = 1").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Entregas WHERE UltimaLatitudeGps IS NULL AND UltimaLongitudeGps IS NULL AND UltimoGpsRecebidoEmUtc IS NULL").SingleAsync());
        await db.Database.ExecuteSqlRawAsync("UPDATE Entregas SET UltimaLatitudeGps = -23.5, UltimaLongitudeGps = -46.6 WHERE Id = 1");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        Assert.Equal(-23.5d, await db.Database.SqlQueryRaw<double>("SELECT UltimaLatitudeGps AS Value FROM Entregas WHERE Id = 1").SingleAsync());
        Assert.Equal(7, await db.Database.SqlQueryRaw<int>("SELECT Versao AS Value FROM Entregas WHERE Id = 1").SingleAsync());
    }

    private sealed class BoundTenant(Guid tenant) : ITenantProvider
    {
        public Guid TenantId => tenant;
        public bool IsTenantBound => true;
    }
}
