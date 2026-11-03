using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using Xunit;

namespace MglConnect.Tests;

public class AssignmentBackfillTests
{
    [Theory]
    [InlineData("Pendente", null, null, 0)]
    [InlineData("Pendente", 1, null, 0)]
    [InlineData("Pendente", null, 1, 0)]
    [InlineData("Pendente", 1, 1, 1)]
    [InlineData("Em transporte", 1, 1, 1)]
    [InlineData("Em transporte", null, 1, 0)]
    [InlineData("Entregue", 1, 1, 0)]
    [InlineData("Cancelada", 1, 1, 0)]
    [InlineData("Desconhecido", 1, 1, 0)]
    public async Task LegacyBackfillRequiresCompleteAssignment(string status, int? driver, int? vehicle, int expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(context);
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Entregas (Id, PedidoId, TenantId, StatusEntrega, MotoristaId, VeiculoId) VALUES (1, 1, {Guid.Empty}, {status}, {driver}, {vehicle})");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        Assert.Equal(expected, await context.Database.SqlQueryRaw<int>("SELECT AtribuicaoAtiva AS Value FROM Entregas").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Entregas").SingleAsync());
    }

    [Fact]
    public async Task StartupPreservesExplicitlyInactiveCompleteAssignment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(context);
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE Entregas ADD AtribuicaoAtiva INTEGER NOT NULL DEFAULT 0; INSERT INTO Entregas (Id, PedidoId, TenantId, StatusEntrega, MotoristaId, VeiculoId) VALUES (1, 1, '00000000-0000-0000-0000-000000000000', 'Pendente', 1, 1);");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT AtribuicaoAtiva AS Value FROM Entregas").SingleAsync());
    }

    [Fact]
    public async Task StartupDoesNotSilentlySanitizePreviouslyCorruptedFlag()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite(connection).Options);
        await SchemaFixture.CreateLegacyAsync(context);
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE Entregas ADD AtribuicaoAtiva INTEGER NOT NULL DEFAULT 0; INSERT INTO Entregas (Id, PedidoId, TenantId, StatusEntrega, AtribuicaoAtiva) VALUES (1, 1, '00000000-0000-0000-0000-000000000000', 'Pendente', 1);");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT AtribuicaoAtiva AS Value FROM Entregas").SingleAsync());
    }
}

internal static class SchemaFixture
{
    // Deliberately no FKs: an old database must be upgraded without recreating tables.
    public static Task CreateLegacyAsync(MglDbContext context) => context.Database.ExecuteSqlRawAsync("""
        CREATE TABLE Clientes (Id INTEGER PRIMARY KEY, TenantId TEXT NOT NULL);
        CREATE TABLE Usuarios (Id INTEGER PRIMARY KEY, TenantId TEXT NOT NULL, ClienteId INTEGER NULL);
        CREATE TABLE Veiculos (Id INTEGER PRIMARY KEY, TenantId TEXT NOT NULL);
        CREATE TABLE Pedidos (Id INTEGER PRIMARY KEY, TenantId TEXT NOT NULL, ClienteId INTEGER NOT NULL, Status TEXT NOT NULL);
        CREATE TABLE Entregas (Id INTEGER PRIMARY KEY, TenantId TEXT NOT NULL, PedidoId INTEGER NOT NULL, StatusEntrega TEXT NOT NULL, MotoristaId INTEGER NULL, VeiculoId INTEGER NULL);
        CREATE TABLE EventosRastreio (Id INTEGER PRIMARY KEY, EntregaId INTEGER NOT NULL);
        INSERT INTO Clientes VALUES (1, '00000000-0000-0000-0000-000000000000');
        INSERT INTO Usuarios VALUES (1, '00000000-0000-0000-0000-000000000000', NULL);
        INSERT INTO Veiculos VALUES (1, '00000000-0000-0000-0000-000000000000');
        INSERT INTO Pedidos VALUES (1, '00000000-0000-0000-0000-000000000000', 1, 'ComEntregaAtribuida');
        """);
}
