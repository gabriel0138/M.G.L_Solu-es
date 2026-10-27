using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using Xunit;

namespace MglConnect.Tests;

public class DatabaseSchemaUpdaterTests
{
    [Fact]
    public async Task UpgradesExistingSqliteOrdersAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new MglDbContext(options);

        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE \"Pedidos\" (\"Id\" INTEGER PRIMARY KEY, \"TenantId\" TEXT NOT NULL, \"NumeroPedido\" TEXT NOT NULL, \"TipoCarga\" TEXT NULL);");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Pedidos\" (\"Id\", \"TenantId\", \"NumeroPedido\", \"TipoCarga\") VALUES (1, '00000000-0000-0000-0000-000000000000', 'PED-LEGADO', NULL);");

        await DatabaseSchemaUpdater.EnsurePedidoRouteColumnsAsync(context);
        await DatabaseSchemaUpdater.EnsurePedidoRouteColumnsAsync(context);

        var columns = await context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Pedidos')")
            .ToListAsync();

        Assert.Contains("OrigemEndereco", columns);
        Assert.Contains("DestinoEndereco", columns);
        Assert.Contains("AlturaCargaMetros", columns);
        Assert.Contains("TipoCarga", columns);

        var tipoCarga = await context.Database
            .SqlQueryRaw<string>("SELECT \"TipoCarga\" AS Value FROM \"Pedidos\" WHERE \"Id\" = 1")
            .SingleAsync();
        Assert.Equal("Carga Geral Seca", tipoCarga);
    }

    [Fact]
    public async Task AddsDriverBirthDateToExistingSqliteUsersIdempotently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new MglDbContext(options);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE \"Usuarios\" (\"Id\" INTEGER PRIMARY KEY, \"Nome\" TEXT NOT NULL);");

        await DatabaseSchemaUpdater.EnsureDriverProfileColumnsAsync(context);
        await DatabaseSchemaUpdater.EnsureDriverProfileColumnsAsync(context);

        var columns = await context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Usuarios')")
            .ToListAsync();
        Assert.Contains("DataNascimento", columns);
    }

    [Fact]
    public async Task UpgradesLegacyOperationalTablesAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new MglDbContext(options);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE \"Pedidos\" (\"Id\" INTEGER PRIMARY KEY, \"Status\" TEXT NOT NULL);");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE \"Entregas\" (\"Id\" INTEGER PRIMARY KEY, \"StatusEntrega\" TEXT NOT NULL, \"MotoristaId\" INTEGER NULL, \"VeiculoId\" INTEGER NULL);");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Pedidos\" (\"Id\", \"Status\") VALUES (1, 'ComEntregaAtribuida');");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Entregas\" (\"Id\", \"StatusEntrega\", \"MotoristaId\", \"VeiculoId\") VALUES (1, 'Pendente', 8, 3);");

        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);

        var deliveryAssignmentActive = await context.Database
            .SqlQueryRaw<int>("SELECT \"AtribuicaoAtiva\" AS Value FROM \"Entregas\" WHERE \"Id\" = 1")
            .SingleAsync();
        Assert.Equal(1, deliveryAssignmentActive);

        var operationalTables = await context.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name IN ('CodigosTurnoMotorista', 'TurnosMotorista', 'AlertasEmergencia', 'AuditoriaOperacional')")
            .ToListAsync();
        Assert.Equal(4, operationalTables.Count);

        var uniqueIndexes = await context.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND name IN ('IX_TurnosMotorista_Ativo_MotoristaId', 'IX_CodigosTurnoMotorista_Ativo_EntregaId', 'IX_AlertasEmergencia_ChaveIdempotencia', 'IX_Entregas_AtribuicaoAtiva_MotoristaId')")
            .ToListAsync();
        Assert.Equal(4, uniqueIndexes.Count);
    }
}
