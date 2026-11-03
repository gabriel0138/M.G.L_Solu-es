using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using Xunit;

namespace MglConnect.Tests;

public class RelationalIntegrityTests
{
    [Fact]
    public async Task FreshSqliteEnforcesAllModelForeignKeysAndPreservesRowsOnRestart()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        await VerifyFreshAsync(context);
    }

    [Fact]
    public async Task LegacySqliteGuardsRejectInvalidReferencesAndDoNotDuplicate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(context);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        var triggers = await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger'").SingleAsync();
        Assert.True(triggers >= 68);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        Assert.Equal(triggers, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger'").SingleAsync());
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("UPDATE Pedidos SET ClienteId = 9999 WHERE Id = 1"));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("INSERT INTO Entregas (Id, TenantId, PedidoId, StatusEntrega) VALUES (1, '', 9999, 'Pendente')"));
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Pedidos").SingleAsync());
        // Model uses CASCADE for Cliente -> Pedido; guards preserve this behavior.
        await context.Database.ExecuteSqlRawAsync("DELETE FROM Clientes WHERE Id = 1");
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Pedidos").SingleAsync());
    }

    [Fact]
    public async Task LegacyOrphansBlockUpgradeWithoutChangingDataOrLeavingPartialSchema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(context);
        await context.Database.ExecuteSqlRawAsync("UPDATE Pedidos SET ClienteId = 9999 WHERE Id = 1");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context));
        Assert.Contains("órfã", error.Message);
        Assert.Equal(9999, await context.Database.SqlQueryRaw<int>("SELECT ClienteId AS Value FROM Pedidos").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Entregas') WHERE name = 'AtribuicaoAtiva'").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = 'TurnosMotorista'").SingleAsync());
    }

    [Fact]
    public async Task ConflictingLegacyAssignmentsRollbackBackfillAndPreserveBothRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(context);
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO Pedidos VALUES (2, '00000000-0000-0000-0000-000000000000', 1, 'ComEntregaAtribuida');
            INSERT INTO Entregas VALUES (1, '00000000-0000-0000-0000-000000000000', 1, 'Pendente', 1, 1);
            INSERT INTO Entregas VALUES (2, '00000000-0000-0000-0000-000000000000', 2, 'Pendente', 1, 1);
            """);
        var error = await Assert.ThrowsAsync<SqliteException>(() => DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context));
        Assert.True(error.Message.Contains("Entregas.MotoristaId", StringComparison.Ordinal)
            || error.Message.Contains("Entregas.VeiculoId", StringComparison.Ordinal), error.Message);
        Assert.Equal(2, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Entregas").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Entregas') WHERE name = 'AtribuicaoAtiva'").SingleAsync());
    }

    [Fact]
    public async Task LegacySqliteGuardsRejectImplicitReplaceDeletesWithoutRecursiveTriggers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(context);
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE Clientes ADD Cnpj TEXT; UPDATE Clientes SET Cnpj = 'FIXTURE-1'; CREATE UNIQUE INDEX IX_Clientes_Cnpj ON Clientes (Cnpj); PRAGMA recursive_triggers = OFF;");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("INSERT OR REPLACE INTO Clientes (Id, TenantId, Cnpj) VALUES (2, '', 'FIXTURE-1')"));
        await context.Database.ExecuteSqlRawAsync("INSERT INTO Clientes (Id, TenantId, Cnpj) VALUES (2, '', 'FIXTURE-2')");
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("UPDATE OR REPLACE Clientes SET Cnpj = 'FIXTURE-1' WHERE Id = 2"));
        Assert.Equal(2, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Clientes").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT ClienteId AS Value FROM Pedidos").SingleAsync());
    }

    [Fact]
    public async Task LegacyOrdersWithoutIdempotencyKeysRemainNullAndDoNotConflict()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(context);
        await context.Database.ExecuteSqlRawAsync("INSERT INTO Pedidos VALUES (2, '00000000-0000-0000-0000-000000000000', 1, 'ComEntregaAtribuida')");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        Assert.Equal(2, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Pedidos WHERE ChaveIdempotencia IS NULL").SingleAsync());
    }

    internal static async Task VerifyFreshAsync(MglDbContext context)
    {
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        var admin = new Usuario { Nome = "Admin teste", Login = "fase1a-admin", SenhaHash = "test-only", Perfil = "Admin" };
        var driver = new Usuario { Nome = "Motorista teste", Login = "fase1a-driver", SenhaHash = "test-only", Perfil = "Motorista" };
        var client = new Cliente { RazaoSocial = "Fixture Fase1A", Cnpj = "00.000.000/0000-00", Telefone = "0000000000" };
        var vehicle = new Veiculo { Placa = "TST1A01", Modelo = "Fixture", CapacidadeCargaKg = 1000 };
        context.AddRange(admin, driver, client, vehicle);
        await context.SaveChangesAsync();
        var order = new Pedido { ClienteId = client.Id, NumeroPedido = "FASE1A-0001", Descricao = "Fixture", Status = "ComEntregaAtribuida" };
        context.Add(order);
        await context.SaveChangesAsync();
        var delivery = new Entrega
        {
            PedidoId = order.Id, MotoristaId = driver.Id, VeiculoId = vehicle.Id,
            CodigoRastreio = "MGL-FASE1A-0001BR", DestinatarioNome = "Fixture", EnderecoDestino = "Rua teste",
            CidadeDestino = "Teste", EstadoDestino = "SP", CepDestino = "00000-000", AtribuicaoAtiva = true
        };
        context.Add(delivery);
        await context.SaveChangesAsync();
        var service = new DriverShiftService(context);
        var issued = await service.IssueCodeAsync(delivery.Id, admin.Id);
        Assert.True(issued.Success, issued.Error);
        Assert.Equal(DriverShiftStartStatus.Started, (await service.StartAsync(driver.Id, delivery.Id, issued.Code!)).Status);
        var alert = await new EmergencyAlertService(context).CreateAsync(driver.Id,
            new EmergencyAlertRequest(Guid.NewGuid(), delivery.Id, null, null, null, null));
        Assert.True(alert.Success);

        var gps = await new GpsIngestionService(context).AcceptAsync(driver.Id, new GpsEventInput(Guid.NewGuid(), delivery.Id,
            (await context.TurnosMotorista.SingleAsync(t => t.EntregaId == delivery.Id && t.Ativo)).Id,
            -23.5, -46.6, 10, DateTimeOffset.UtcNow));
        Assert.Equal("confirmado", gps.Estado);

        // A separate closed synthetic trip exercises archive FKs without changing
        // the ongoing operation whose state is checked below.
        var historicalOrder=new Pedido{ClienteId=client.Id,NumeroPedido="ARCHIVE-FK-ONLY",Descricao="Synthetic historical trip",Status="Concluido"};
        context.Add(historicalOrder);await context.SaveChangesAsync();
        var historicalTrip=new Entrega{PedidoId=historicalOrder.Id,MotoristaId=driver.Id,VeiculoId=vehicle.Id,CodigoRastreio="ARCHIVE-FK-ONLY",StatusEntrega="Entregue",AtribuicaoAtiva=false};
        context.Add(historicalTrip);await context.SaveChangesAsync();
        context.PosicoesGps.Add(new PosicaoGps{EventoId=Guid.NewGuid(),EntregaId=historicalTrip.Id,MotoristaId=driver.Id,AtorUsuarioId=driver.Id,VeiculoId=vehicle.Id,
            Latitude=-23,Longitude=-46,RecebidoEmUtc=DateTime.UtcNow,CapturadoEmUtc=DateTime.UtcNow,OrdenadoEmUtc=DateTime.UtcNow});
        await context.SaveChangesAsync();
        Assert.True((await new GpsArchiveService(context).ArchiveNextAsync(admin.Id,historicalTrip.Id)).Success);

        var trust=new MglConnect.Web.Services.RouteTrust.RouteTrustService(context,new TestRouteSource(new()),new MglConnect.Web.Services.RouteTrust.RestrictionEngine());
        await trust.FinishAsync(await trust.BeginAsync(delivery.Id,admin.Id),new TruckRoutePlan());

        context.PerfisViagemRevisoes.Add(new(){TenantId=delivery.TenantId,EntregaId=delivery.Id,VeiculoId=vehicle.Id,AtorUsuarioId=admin.Id,Revisao=1,CriadoEmUtc=DateTime.UtcNow,PerfilJson=System.Text.Json.JsonSerializer.Serialize(new MglConnect.Web.Services.RouteTrust.OperationalTruckProfile{TripId=delivery.Id,VehicleId=vehicle.Id}),CadastroVeiculoJson=MglConnect.Web.Services.RouteTrust.TripProfiles.VehicleSnapshot(vehicle)});
        await context.SaveChangesAsync();
        // Every declared relationship is exercised against a real relational engine.
        var keys = context.Model.GetRelationalModel().Tables.SelectMany(t => t.ForeignKeyConstraints).ToList();
        Assert.Equal(37, keys.Count);
        foreach (var key in keys)
        {
            object invalid = key.Columns[0].ProviderClrType == typeof(Guid) ? Guid.NewGuid() : int.MaxValue;
            var sql = $"UPDATE {Quote(key.Table.Name)} SET {Quote(key.Columns[0].Name)} = {{0}}";
            await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => context.Database.ExecuteSqlRawAsync(sql, invalid));
        }
        var otherOrder = new Pedido { ClienteId = client.Id, NumeroPedido = "FASE1A-0002", Descricao = "Fixture reserva concorrente" };
        context.Add(otherOrder);
        await context.SaveChangesAsync();
        var conflict = new Entrega
        {
            PedidoId = otherOrder.Id, MotoristaId = driver.Id, VeiculoId = vehicle.Id,
            CodigoRastreio = "MGL-FASE1A-0002BR", DestinatarioNome = "Fixture", EnderecoDestino = "Rua teste",
            CidadeDestino = "Teste", EstadoDestino = "SP", CepDestino = "00000-000", AtribuicaoAtiva = true
        };
        context.Add(conflict);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.Entry(conflict).State = EntityState.Detached;
        var counts = await CountsAsync(context);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        Assert.Equal(counts, await CountsAsync(context));
        context.ChangeTracker.Clear();
        var persisted = await context.Entregas.SingleAsync(e=>e.Id==delivery.Id);
        Assert.True(persisted.AtribuicaoAtiva);
        Assert.Equal(driver.Id, persisted.MotoristaId);
        Assert.Equal(vehicle.Id, persisted.VeiculoId);
        Assert.Equal("Em transporte", persisted.StatusEntrega);
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM \"Usuarios\" WHERE \"Login\" = 'fase1a-driver'"));
        Assert.Equal(counts, await CountsAsync(context));

        static string Quote(string name) => "\"" + name + "\"";
        static async Task<string> CountsAsync(MglDbContext db) => string.Join(",", await db.Usuarios.CountAsync(),
            await db.Clientes.CountAsync(), await db.Pedidos.CountAsync(), await db.Entregas.CountAsync(),
            await db.CodigosTurnoMotorista.CountAsync(), await db.TurnosMotorista.CountAsync(),
            await db.AlertasEmergencia.CountAsync(), await db.AuditoriaOperacional.CountAsync(), await db.EventosRastreio.CountAsync(),
            await db.PosicoesGps.CountAsync(), await db.GpsArquivos.CountAsync(), await db.GpsEventosArquivados.CountAsync());
    }
}
