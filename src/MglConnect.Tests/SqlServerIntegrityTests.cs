using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using Xunit;
using Xunit.Abstractions;

namespace MglConnect.Tests;

// Optional external SQL Server; no Docker API, automatic install or database cleanup.
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MGLCONNECT_SQLSERVER_TEST_CONNECTION")))
            Skip = "SQL Server opcional: configure MGLCONNECT_SQLSERVER_TEST_CONNECTION para master em instância de testes autorizada.";
    }
}

public class SqlServerIntegrityTests(ITestOutputHelper output)
{
    [SqlServerFact]
    [Trait("Provider", "SqlServer")]
    public async Task FreshDatabaseRunsOperationalServicesAndEnforcesAllModelForeignKeys()
    {
        await using var context = await CreateDatabaseAsync("Fresh", legacy: false);
        await RelationalIntegrityTests.VerifyFreshAsync(context);
        await AssertTrustedForeignKeysAsync(context);
        await context.Database.ExecuteSqlRawAsync("EXEC sp_rename N'dbo.FK_Entregas_Pedidos_PedidoId', N'FK_Fase1A_Renamed'; ALTER TABLE Entregas NOCHECK CONSTRAINT FK_Fase1A_Renamed;");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        await AssertTrustedForeignKeysAsync(context);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE name = 'FK_Fase1A_Renamed'").SingleAsync());
        await context.Database.ExecuteSqlRawAsync("DBCC CHECKDB WITH NO_INFOMSGS");
    }

    [SqlServerFact]
    [Trait("Provider", "SqlServer")]
    public async Task LegacyUpgradeBackfillsOnlyCompleteAssignmentsAndPreservesExplicitFlags()
    {
        await using var context = await CreateDatabaseAsync("Legacy", legacy: true);
        var cases = new (string Status, bool Driver, bool Vehicle, bool Expected)[]
        {
            ("Pendente", false, false, false), ("Pendente", true, false, false),
            ("Pendente", false, true, false), ("Pendente", true, true, true),
            ("Em transporte", true, true, true), ("Em transporte", false, true, false),
            ("Entregue", true, true, false), ("Cancelada", true, true, false),
            ("Desconhecido", true, true, false)
        };
        for (var i = 0; i < cases.Length; i++)
        {
            var item = cases[i];
            var client = new Cliente { Cnpj = $"TESTE-{i}", RazaoSocial = "Fixture", Telefone = "0000" };
            var driver = new Usuario { Login = $"legacy-driver-{i}", Nome = "Fixture", SenhaHash = "test-only", Perfil = "Motorista" };
            var vehicle = new Veiculo { Placa = $"TST1A{i:00}", Modelo = "Fixture" };
            context.AddRange(client, driver, vehicle);
            await context.SaveChangesAsync();
            var order = new Pedido { NumeroPedido = $"LEGACY-{i}", ClienteId = client.Id, Descricao = "Fixture" };
            await InsertLegacyEntityAsync(context, order, "Pedidos", "ChaveIdempotencia");
            order.Id = await context.Database.SqlQueryRaw<int>("SELECT MAX(Id) AS Value FROM Pedidos").SingleAsync();
            var delivery = new Entrega
            {
                PedidoId = order.Id, CodigoRastreio = $"MGL-LEGACY-{i}BR", DestinatarioNome = "Fixture",
                EnderecoDestino = "Rua teste", CidadeDestino = "Teste", EstadoDestino = "SP", CepDestino = "00000-000",
                MotoristaId = item.Driver ? driver.Id : null, VeiculoId = item.Vehicle ? vehicle.Id : null,
                StatusEntrega = item.Status
            };
            await InsertLegacyEntityAsync(context, delivery, "Entregas", "AtribuicaoAtiva");
        }
        await DatabaseSchemaUpdater.EnsurePedidoRouteColumnsAsync(context);
        await DatabaseSchemaUpdater.EnsureDriverProfileColumnsAsync(context);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        var deliveries = await context.Entregas.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(cases.Select(c => c.Expected), deliveries.Select(d => d.AtribuicaoAtiva));
        Assert.Equal(cases.Select(c => c.Status), deliveries.Select(d => d.StatusEntrega));
        await AssertTrustedForeignKeysAsync(context);
        var indexCount = await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables)").SingleAsync();
        // Explicitly deactivating a valid assignment is not undone by the next startup.
        await context.Database.ExecuteSqlRawAsync("UPDATE Entregas SET AtribuicaoAtiva = 0 WHERE Id = 4");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);
        Assert.False(await context.Entregas.Where(e => e.Id == 4).Select(e => e.AtribuicaoAtiva).SingleAsync());
        Assert.Equal(9, await context.Entregas.CountAsync());
        Assert.Equal(9, await context.Pedidos.CountAsync());
        Assert.Equal(9, await context.Pedidos.CountAsync(p => p.ChaveIdempotencia == null));
        Assert.Equal(indexCount, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables)").SingleAsync());
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync("UPDATE Entregas SET PedidoId = 2147483647 WHERE Id = 1"));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync("UPDATE Entregas SET MotoristaId = 2147483647 WHERE Id = 1"));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync("UPDATE Entregas SET VeiculoId = 2147483647 WHERE Id = 1"));
        await context.Database.ExecuteSqlRawAsync("DBCC CHECKDB WITH NO_INFOMSGS");
    }

    [SqlServerFact]
    [Trait("Provider", "SqlServer")]
    public async Task LegacyOrphanBlocksAndRollsBackOperationalUpgrade()
    {
        await using var context = await CreateDatabaseAsync("Orphan", legacy: true);
        var order = new Pedido { NumeroPedido = "ORPHAN-TEST", ClienteId = int.MaxValue, Descricao = "Fixture órfã controlada" };
        await InsertLegacyEntityAsync(context, order, "Pedidos", "ChaveIdempotencia"); // Fixture intentionally has no FKs.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context));
        Assert.Contains("órfã", error.Message);
        Assert.Equal(int.MaxValue, await context.Database.SqlQueryRaw<int>("SELECT ClienteId AS Value FROM Pedidos").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.columns WHERE object_id = OBJECT_ID('Entregas') AND name = 'AtribuicaoAtiva'").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'TurnosMotorista'").SingleAsync());
    }

    [SqlServerFact]
    [Trait("Provider", "SqlServer")]
    public async Task IncompatibleExistingConstraintIsNotSilentlyAcceptedOrRemoved()
    {
        await using var context = await CreateDatabaseAsync("Incompatible", legacy: true);
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE Entregas ADD CONSTRAINT FK_Entregas_Usuarios_MotoristaId FOREIGN KEY (MotoristaId) REFERENCES Veiculos(Id)");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context));
        Assert.Contains("incompatível", error.Message);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE name = 'FK_Entregas_Usuarios_MotoristaId' AND referenced_object_id = OBJECT_ID('Veiculos')").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'TurnosMotorista'").SingleAsync());
    }

    private async Task<MglDbContext> CreateDatabaseAsync(string scenario, bool legacy)
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("MGLCONNECT_SQLSERVER_TEST_CONNECTION"));
        if (!string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A conexão de integração deve apontar para master; os testes criam bancos novos MGLConnect_Fase1A_* e nunca reutilizam bancos existentes.");
        var name = $"MGLConnect_Fase1A_{scenario}_{Guid.NewGuid():N}";
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync();
            await using var command = master.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]";
            await command.ExecuteNonQueryAsync();
        }
        output.WriteLine($"Banco isolado preservado (sem limpeza automática): {name}");
        builder.InitialCatalog = name;
        var context = new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlServer(builder.ConnectionString).Options);
        if (!legacy)
        {
            await context.Database.EnsureCreatedAsync();
            return context;
        }

        // Derive an old schema fixture from EF, omitting operational tables, FKs
        // and the assignment flag. No table/database is dropped or recreated.
        var script = context.Database.GenerateCreateScript();
        var excluded = "CodigosTurnoMotorista|TurnosMotorista|AlertasEmergencia|AuditoriaOperacional|PosicoesGps|GpsArquivos|GpsEventosArquivados|RotaAvaliacoes|PerfisViagemRevisoes";
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline))
        {
            if (string.IsNullOrWhiteSpace(batch)
                || Regex.IsMatch(batch, $@"(?:CREATE TABLE|ON) \[(?:{excluded})\]")
                || batch.Contains("CREATE UNIQUE INDEX [IX_Entregas_AtribuicaoAtiva_", StringComparison.Ordinal)
                || batch.Contains("CREATE UNIQUE INDEX [IX_Pedidos_ChaveIdempotencia]", StringComparison.Ordinal)) continue;
            var sql = Regex.Replace(batch, @"^\s*CONSTRAINT \[FK_[^\r\n]+\r?$", "", RegexOptions.Multiline);
            sql = Regex.Replace(sql, @"^\s*\[AtribuicaoAtiva\][^\r\n]+\r?$", "", RegexOptions.Multiline);
            sql = Regex.Replace(sql, @"^\s*\[ChaveIdempotencia\][^\r\n]+\r?$", "", RegexOptions.Multiline);
            sql = Regex.Replace(sql, @",\s*\);", "\n);");
            await context.Database.ExecuteSqlRawAsync(sql);
        }
        return context;
    }

    private static async Task InsertLegacyEntityAsync(MglDbContext context, object entity, string table, string excludedColumn)
    {
        var properties = context.Entry(entity).Properties.Where(p => p.Metadata.Name != "Id" && p.Metadata.Name != excludedColumn).ToList();
        var columns = string.Join(",", properties.Select(p => $"[{p.Metadata.GetColumnName()}]"));
        var parameters = properties.Select((p, i) => new SqlParameter
        {
            ParameterName = $"@p{i}",
            DbType = p.Metadata.GetRelationalTypeMapping().DbType ?? System.Data.DbType.Object,
            Value = p.CurrentValue ?? DBNull.Value
        }).ToArray();
        var placeholders = string.Join(",", parameters.Select(p => p.ParameterName));
        // Identifiers originate only in EF metadata; ALL values are parameters.
        var sql = $"INSERT INTO [{table}] ({columns}) VALUES ({placeholders})";
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    private static async Task AssertTrustedForeignKeysAsync(MglDbContext context)
    {
        Assert.Equal(38, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.foreign_keys").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1").SingleAsync());
        Assert.Equal(6, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.indexes WHERE has_filter = 1").SingleAsync());
    }
}
