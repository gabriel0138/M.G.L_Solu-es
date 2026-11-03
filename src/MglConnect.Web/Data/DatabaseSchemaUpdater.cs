using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace MglConnect.Web.Data;

public static class DatabaseSchemaUpdater
{
    private static readonly IReadOnlyDictionary<string, string> PedidoColumns = new Dictionary<string, string>
    {
        ["OrigemEndereco"] = "string:200",
        ["OrigemNumero"] = "string:20",
        ["OrigemBairro"] = "string:80",
        ["OrigemCidade"] = "string:80",
        ["OrigemEstado"] = "string:2",
        ["OrigemCep"] = "string:10",
        ["DestinoEndereco"] = "string:200",
        ["DestinoNumero"] = "string:20",
        ["DestinoBairro"] = "string:80",
        ["DestinoCidade"] = "string:80",
        ["DestinoEstado"] = "string:2",
        ["DestinoCep"] = "string:10",
        ["AlturaCargaMetros"] = "decimal",
        ["LarguraCargaMetros"] = "decimal",
        ["ComprimentoCargaMetros"] = "decimal",
        ["TipoCarga"] = "string:50"
    };

    public static async Task EnsurePedidoRouteColumnsAsync(MglDbContext context)
    {
        var provider = context.Database.ProviderName;
        if (provider is null || provider.Contains("InMemory", StringComparison.Ordinal))
        {
            return;
        }

        var connection = context.Database.GetDbConnection();
        var closeAfterUpdate = connection.State != ConnectionState.Open;
        if (closeAfterUpdate)
        {
            await connection.OpenAsync();
        }

        try
        {
            var existingColumns = provider.Contains("Sqlite", StringComparison.Ordinal)
                ? await ReadSqliteColumnsAsync(connection)
                : null;

            foreach (var (column, type) in PedidoColumns)
            {
                if (existingColumns?.Contains(column) == true)
                {
                    continue;
                }

                var sql = provider switch
                {
                    var name when name.Contains("Sqlite", StringComparison.Ordinal) =>
                        $"ALTER TABLE \"Pedidos\" ADD COLUMN \"{column}\" {GetSqliteType(type)} NULL;",
                    var name when name.Contains("Npgsql", StringComparison.Ordinal) =>
                        $"ALTER TABLE \"Pedidos\" ADD COLUMN IF NOT EXISTS \"{column}\" {GetPostgresType(type)} NULL;",
                    var name when name.Contains("SqlServer", StringComparison.Ordinal) =>
                        $"IF COL_LENGTH('Pedidos', '{column}') IS NULL ALTER TABLE [Pedidos] ADD [{column}] {GetSqlServerType(type)} NULL;",
                    _ => throw new InvalidOperationException($"Provedor de banco não suportado para atualização de esquema: {provider}.")
                };

                await ExecuteSqlAsync(connection, sql);
            }

            await BackfillPedidoTypeCargaAsync(connection, provider);
            await EnsureUniqueOrderNumberIndexAsync(connection, provider);
        }
        finally
        {
            if (closeAfterUpdate)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static async Task EnsureDriverProfileColumnsAsync(MglDbContext context)
    {
        var provider = context.Database.ProviderName;
        if (provider is null || provider.Contains("InMemory", StringComparison.Ordinal))
        {
            return;
        }

        await EnsureColumnAsync(context.Database.GetDbConnection(), provider, "Usuarios", "DataNascimento", "date", nullable: true);
    }

        public static async Task EnsureOperationalSecuritySchemaAsync(MglDbContext context)
        {
            var provider = context.Database.ProviderName;
            if (provider is null || provider.Contains("InMemory", StringComparison.Ordinal))
            {
                return;
            }

            var connection = context.Database.GetDbConnection();
            var closeAfterUpdate = connection.State != ConnectionState.Open;
            if (closeAfterUpdate)
            {
                await connection.OpenAsync();
            }

            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                await EnsureOperationalTablesAsync(connection, provider, transaction);
                await EnsureColumnAsync(connection, provider, "Pedidos", "ChaveIdempotencia", "string:36", nullable: true, transaction: transaction);
                await EnsureColumnAsync(connection, provider, "Pedidos", "Versao", "integer", transaction: transaction);
                var assignmentColumnAdded = await EnsureColumnAsync(connection, provider, "Entregas", "AtribuicaoAtiva", "boolean", transaction: transaction);
                await EnsureColumnAsync(connection, provider, "Entregas", "Versao", "integer", transaction: transaction);
                foreach (var column in new[] { "UltimaLatitudeGps", "UltimaLongitudeGps", "UltimaPrecisaoGps", "UltimaVelocidadeGps", "UltimaDirecaoGps" })
                {
                    await EnsureColumnAsync(connection, provider, "Entregas", column, "double", nullable: true, transaction: transaction);
                }
                await EnsureColumnAsync(connection, provider, "Entregas", "UltimoGpsRecebidoEmUtc", "datetime", nullable: true, transaction: transaction);
                await EnsureColumnAsync(connection, provider, "Entregas", "UltimoGpsCapturadoEmUtc", "datetime", nullable: true, transaction: transaction);
                await GpsHistorySchemaUpdater.EnsureAsync(connection, provider, transaction);
                await GpsArchiveSchemaUpdater.EnsureAsync(connection, provider, transaction);
                await RouteTrustSchemaUpdater.EnsureAsync(connection,provider,transaction);
                // Only infer old assignments when the flag is first introduced. A later
                // startup must never reactivate an explicitly deactivated assignment.
                if (assignmentColumnAdded)
                {
                    await ExecuteSqlAsync(connection, provider switch
                {
                    var name when name.Contains("Sqlite", StringComparison.Ordinal) =>
                        "UPDATE \"Entregas\" SET \"AtribuicaoAtiva\" = 1 WHERE \"AtribuicaoAtiva\" = 0 AND \"MotoristaId\" IS NOT NULL AND \"VeiculoId\" IS NOT NULL AND \"StatusEntrega\" IN ('Pendente', 'Em transporte');",
                    var name when name.Contains("Npgsql", StringComparison.Ordinal) =>
                        "UPDATE \"Entregas\" SET \"AtribuicaoAtiva\" = TRUE WHERE \"AtribuicaoAtiva\" = FALSE AND \"MotoristaId\" IS NOT NULL AND \"VeiculoId\" IS NOT NULL AND \"StatusEntrega\" IN ('Pendente', 'Em transporte');",
                    var name when name.Contains("SqlServer", StringComparison.Ordinal) =>
                        "UPDATE [Entregas] SET [AtribuicaoAtiva] = 1 WHERE [AtribuicaoAtiva] = 0 AND [MotoristaId] IS NOT NULL AND [VeiculoId] IS NOT NULL AND [StatusEntrega] IN (N'Pendente', N'Em transporte');",
                    _ => throw new InvalidOperationException($"Provedor de banco não suportado para atualização de esquema: {provider}.")
                }, transaction);
                }
                await EnsureOperationalIndexesAsync(connection, provider, transaction);
                await RelationalIntegrityUpdater.EnsureAsync(context, transaction);
                await transaction.CommitAsync();
            }
            finally
            {
                if (closeAfterUpdate)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private static async Task EnsureOperationalTablesAsync(
            System.Data.Common.DbConnection connection,
            string provider,
            DbTransaction transaction)
        {
            var (guid, dateTime, boolean, identity, createPrefix, floating) = provider switch
            {
                var name when name.Contains("Sqlite", StringComparison.Ordinal) =>
                    ("TEXT", "TEXT", "INTEGER", "INTEGER PRIMARY KEY AUTOINCREMENT", "CREATE TABLE IF NOT EXISTS", "REAL"),
                var name when name.Contains("Npgsql", StringComparison.Ordinal) =>
                    ("uuid", "timestamp with time zone", "boolean", "bigserial PRIMARY KEY", "CREATE TABLE IF NOT EXISTS", "DOUBLE PRECISION"),
                var name when name.Contains("SqlServer", StringComparison.Ordinal) =>
                    ("uniqueidentifier", "datetime2", "bit", "bigint IDENTITY(1,1) PRIMARY KEY",
                        "IF OBJECT_ID('{0}', 'U') IS NULL CREATE TABLE", "float"),
                _ => throw new InvalidOperationException($"Provedor de banco não suportado para atualização de esquema: {provider}.")
            };
            var tables = new[]
            {
                $"""
                {createPrefix} "CodigosTurnoMotorista" (
                    "Id" {guid} NOT NULL PRIMARY KEY, "TenantId" {guid} NOT NULL, "EntregaId" INTEGER NOT NULL,
                    "MotoristaId" INTEGER NOT NULL, "CodigoHash" VARCHAR(64) NOT NULL, "EmitidoPorUsuarioId" INTEGER NOT NULL,
                    "EmitidoEmUtc" {dateTime} NOT NULL, "ExpiraEmUtc" {dateTime} NOT NULL, "UsadoEmUtc" {dateTime} NULL,
                    "CanceladoEmUtc" {dateTime} NULL, "Tentativas" INTEGER NOT NULL DEFAULT 0, "Versao" INTEGER NOT NULL DEFAULT 0
                );
                """,
                $"""
                {createPrefix} "TurnosMotorista" (
                    "Id" {guid} NOT NULL PRIMARY KEY, "TenantId" {guid} NOT NULL, "EntregaId" INTEGER NOT NULL,
                    "MotoristaId" INTEGER NOT NULL, "VeiculoId" INTEGER NOT NULL, "CodigoTurnoId" {guid} NOT NULL,
                    "IniciadoEmUtc" {dateTime} NOT NULL, "EncerradoEmUtc" {dateTime} NULL, "Ativo" {boolean} NOT NULL,
                    "Versao" INTEGER NOT NULL DEFAULT 0
                );
                """,
                $"""
                {createPrefix} "AlertasEmergencia" (
                    "Id" {guid} NOT NULL PRIMARY KEY, "TenantId" {guid} NOT NULL, "ChaveIdempotencia" {guid} NOT NULL,
                    "EntregaId" INTEGER NOT NULL, "MotoristaId" INTEGER NOT NULL, "VeiculoId" INTEGER NULL,
                    "CriadoEmUtc" {dateTime} NOT NULL, "Status" VARCHAR(30) NOT NULL, "SituacaoLocalizacao" VARCHAR(30) NOT NULL,
                    "Latitude" {floating} NULL, "Longitude" {floating} NULL, "PrecisaoMetros" {floating} NULL,
                    "LocalizacaoCapturadaEmUtc" {dateTime} NULL, "ReconhecidoPorUsuarioId" INTEGER NULL,
                    "ReconhecidoEmUtc" {dateTime} NULL, "AtualizadoEmUtc" {dateTime} NULL, "Versao" INTEGER NOT NULL DEFAULT 0
                );
                """,
                $"""
                {createPrefix} "AuditoriaOperacional" (
                    "Id" {identity}, "TenantId" {guid} NOT NULL, "AtorUsuarioId" INTEGER NOT NULL,
                    "Evento" VARCHAR(60) NOT NULL, "TipoEntidade" VARCHAR(40) NOT NULL,
                    "IdentificadorEntidade" VARCHAR(80) NOT NULL, "Detalhes" VARCHAR(200) NULL,
                    "OcorridoEmUtc" {dateTime} NOT NULL
                );
                """
            };

            foreach (var tableSql in tables)
            {
                var sql = tableSql;
                if (provider.Contains("SqlServer", StringComparison.Ordinal))
                {
                    var tableName = tableSql.Contains("\"TurnosMotorista\"", StringComparison.Ordinal) ? "TurnosMotorista"
                        : tableSql.Contains("\"AlertasEmergencia\"", StringComparison.Ordinal) ? "AlertasEmergencia"
                        : tableSql.Contains("\"AuditoriaOperacional\"", StringComparison.Ordinal) ? "AuditoriaOperacional"
                        : "CodigosTurnoMotorista";
                    sql = tableSql.Replace("IF OBJECT_ID('{0}', 'U') IS NULL", $"IF OBJECT_ID(N'{tableName}', N'U') IS NULL", StringComparison.Ordinal);
                }
                await ExecuteSqlAsync(connection, sql, transaction);
            }
        }

        internal static async Task<bool> EnsureColumnAsync(
            System.Data.Common.DbConnection connection,
            string provider,
            string table,
            string column,
            string type,
            bool nullable = false,
            DbTransaction? transaction = null)
        {
            var closeAfterUpdate = connection.State != ConnectionState.Open;
            if (closeAfterUpdate)
            {
                await connection.OpenAsync();
            }

            try
            {
                if (await ColumnExistsAsync(connection, provider, table, column, transaction))
                {
                    return false;
                }

                var nullSql = nullable ? "NULL" : "NOT NULL";
                var sql = provider switch
                {
                    var name when name.Contains("Sqlite", StringComparison.Ordinal) =>
                        $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {GetSqliteType(type)} {nullSql}{(nullable ? string.Empty : " DEFAULT 0")};",
                    var name when name.Contains("Npgsql", StringComparison.Ordinal) =>
                        $"ALTER TABLE \"{table}\" ADD COLUMN IF NOT EXISTS \"{column}\" {GetPostgresType(type)} {nullSql}{(nullable ? string.Empty : type == "boolean" ? " DEFAULT FALSE" : " DEFAULT 0")};",
                    var name when name.Contains("SqlServer", StringComparison.Ordinal) =>
                        nullable
                            ? $"IF COL_LENGTH('{table}', '{column}') IS NULL ALTER TABLE [{table}] ADD [{column}] {GetSqlServerType(type)} NULL;"
                            : $"IF COL_LENGTH('{table}', '{column}') IS NULL ALTER TABLE [{table}] ADD [{column}] {GetSqlServerType(type)} NOT NULL CONSTRAINT [DF_{table}_{column}] DEFAULT 0;",
                    _ => throw new InvalidOperationException($"Provedor de banco não suportado para atualização de esquema: {provider}.")
                };
                await ExecuteSqlAsync(connection, sql, transaction);
                return true;
            }
            finally
            {
                if (closeAfterUpdate)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private static async Task EnsureOperationalIndexesAsync(
            System.Data.Common.DbConnection connection,
            string provider,
            DbTransaction transaction)
        {
            var statements = provider switch
            {
                var name when name.Contains("Sqlite", StringComparison.Ordinal) =>
                    new[]
                    {
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Entregas_PedidoId\" ON \"Entregas\" (\"PedidoId\");",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TurnosMotorista_Ativo_MotoristaId\" ON \"TurnosMotorista\" (\"MotoristaId\") WHERE \"Ativo\" = 1;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TurnosMotorista_Ativo_EntregaId\" ON \"TurnosMotorista\" (\"EntregaId\") WHERE \"Ativo\" = 1;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_CodigosTurnoMotorista_Ativo_EntregaId\" ON \"CodigosTurnoMotorista\" (\"EntregaId\") WHERE \"UsadoEmUtc\" IS NULL AND \"CanceladoEmUtc\" IS NULL;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AlertasEmergencia_ChaveIdempotencia\" ON \"AlertasEmergencia\" (\"ChaveIdempotencia\");",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Pedidos_ChaveIdempotencia\" ON \"Pedidos\" (\"ChaveIdempotencia\") WHERE \"ChaveIdempotencia\" IS NOT NULL;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Entregas_AtribuicaoAtiva_MotoristaId\" ON \"Entregas\" (\"MotoristaId\") WHERE \"AtribuicaoAtiva\" = 1 AND \"MotoristaId\" IS NOT NULL;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Entregas_AtribuicaoAtiva_VeiculoId\" ON \"Entregas\" (\"VeiculoId\") WHERE \"AtribuicaoAtiva\" = 1 AND \"VeiculoId\" IS NOT NULL;"
                    },
                var name when name.Contains("Npgsql", StringComparison.Ordinal) =>
                    new[]
                    {
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Entregas_PedidoId\" ON \"Entregas\" (\"PedidoId\");",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TurnosMotorista_Ativo_MotoristaId\" ON \"TurnosMotorista\" (\"MotoristaId\") WHERE \"Ativo\" = TRUE;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TurnosMotorista_Ativo_EntregaId\" ON \"TurnosMotorista\" (\"EntregaId\") WHERE \"Ativo\" = TRUE;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_CodigosTurnoMotorista_Ativo_EntregaId\" ON \"CodigosTurnoMotorista\" (\"EntregaId\") WHERE \"UsadoEmUtc\" IS NULL AND \"CanceladoEmUtc\" IS NULL;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AlertasEmergencia_ChaveIdempotencia\" ON \"AlertasEmergencia\" (\"ChaveIdempotencia\");",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Pedidos_ChaveIdempotencia\" ON \"Pedidos\" (\"ChaveIdempotencia\") WHERE \"ChaveIdempotencia\" IS NOT NULL;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Entregas_AtribuicaoAtiva_MotoristaId\" ON \"Entregas\" (\"MotoristaId\") WHERE \"AtribuicaoAtiva\" = TRUE AND \"MotoristaId\" IS NOT NULL;",
                        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Entregas_AtribuicaoAtiva_VeiculoId\" ON \"Entregas\" (\"VeiculoId\") WHERE \"AtribuicaoAtiva\" = TRUE AND \"VeiculoId\" IS NOT NULL;"
                    },
                var name when name.Contains("SqlServer", StringComparison.Ordinal) =>
                    new[]
                    {
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Entregas_PedidoId' AND object_id = OBJECT_ID('Entregas')) CREATE UNIQUE INDEX [IX_Entregas_PedidoId] ON [Entregas] ([PedidoId]);",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TurnosMotorista_Ativo_MotoristaId') CREATE UNIQUE INDEX [IX_TurnosMotorista_Ativo_MotoristaId] ON [TurnosMotorista] ([MotoristaId]) WHERE [Ativo] = 1;",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TurnosMotorista_Ativo_EntregaId') CREATE UNIQUE INDEX [IX_TurnosMotorista_Ativo_EntregaId] ON [TurnosMotorista] ([EntregaId]) WHERE [Ativo] = 1;",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CodigosTurnoMotorista_Ativo_EntregaId') CREATE UNIQUE INDEX [IX_CodigosTurnoMotorista_Ativo_EntregaId] ON [CodigosTurnoMotorista] ([EntregaId]) WHERE [UsadoEmUtc] IS NULL AND [CanceladoEmUtc] IS NULL;",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AlertasEmergencia_ChaveIdempotencia') CREATE UNIQUE INDEX [IX_AlertasEmergencia_ChaveIdempotencia] ON [AlertasEmergencia] ([ChaveIdempotencia]);",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Pedidos_ChaveIdempotencia') CREATE UNIQUE INDEX [IX_Pedidos_ChaveIdempotencia] ON [Pedidos] ([ChaveIdempotencia]) WHERE [ChaveIdempotencia] IS NOT NULL;",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Entregas_AtribuicaoAtiva_MotoristaId') CREATE UNIQUE INDEX [IX_Entregas_AtribuicaoAtiva_MotoristaId] ON [Entregas] ([MotoristaId]) WHERE [AtribuicaoAtiva] = 1 AND [MotoristaId] IS NOT NULL;",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Entregas_AtribuicaoAtiva_VeiculoId') CREATE UNIQUE INDEX [IX_Entregas_AtribuicaoAtiva_VeiculoId] ON [Entregas] ([VeiculoId]) WHERE [AtribuicaoAtiva] = 1 AND [VeiculoId] IS NOT NULL;"
                    },
                _ => throw new InvalidOperationException($"Provedor de banco não suportado para atualização de esquema: {provider}.")
            };

            foreach (var statement in statements)
            {
                var sql = provider.Contains("Npgsql", StringComparison.Ordinal)
                    ? statement.Replace("\"Ativo\" = 1", "\"Ativo\" = TRUE", StringComparison.Ordinal)
                        .Replace("\"AtribuicaoAtiva\" = 1", "\"AtribuicaoAtiva\" = TRUE", StringComparison.Ordinal)
                    : statement;
                await ExecuteSqlAsync(connection, sql, transaction);
            }
        }

    private static Task BackfillPedidoTypeCargaAsync(
        System.Data.Common.DbConnection connection,
        string provider)
    {
        var sql = provider switch
        {
            var name when name.Contains("Sqlite", StringComparison.Ordinal) =>
                "UPDATE \"Pedidos\" SET \"TipoCarga\" = 'Carga Geral Seca' WHERE \"TipoCarga\" IS NULL;",
            var name when name.Contains("Npgsql", StringComparison.Ordinal) =>
                "UPDATE \"Pedidos\" SET \"TipoCarga\" = 'Carga Geral Seca' WHERE \"TipoCarga\" IS NULL;",
            var name when name.Contains("SqlServer", StringComparison.Ordinal) =>
                "UPDATE [Pedidos] SET [TipoCarga] = N'Carga Geral Seca' WHERE [TipoCarga] IS NULL;",
            _ => throw new InvalidOperationException($"Provedor de banco não suportado para atualização de esquema: {provider}.")
        };

        return ExecuteSqlAsync(connection, sql);
    }

    private static async Task<HashSet<string>> ReadSqliteColumnsAsync(
        System.Data.Common.DbConnection connection,
        string table = "Pedidos",
        DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info('{table}');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task EnsureUniqueOrderNumberIndexAsync(
        System.Data.Common.DbConnection connection,
        string provider)
    {
        var sql = provider switch
        {
            var name when name.Contains("Sqlite", StringComparison.Ordinal) =>
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Pedidos_TenantId_NumeroPedido\" ON \"Pedidos\" (\"TenantId\", \"NumeroPedido\");",
            var name when name.Contains("Npgsql", StringComparison.Ordinal) =>
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Pedidos_TenantId_NumeroPedido\" ON \"Pedidos\" (\"TenantId\", \"NumeroPedido\");",
            var name when name.Contains("SqlServer", StringComparison.Ordinal) =>
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Pedidos_TenantId_NumeroPedido' AND object_id = OBJECT_ID('Pedidos')) CREATE UNIQUE INDEX [IX_Pedidos_TenantId_NumeroPedido] ON [Pedidos] ([TenantId], [NumeroPedido]);",
            _ => throw new InvalidOperationException($"Provedor de banco não suportado para atualização de esquema: {provider}.")
        };

        await ExecuteSqlAsync(connection, sql);
    }

    private static async Task ExecuteSqlAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection connection, string provider,
        string table, string column, DbTransaction? transaction)
    {
        if (provider.Contains("Sqlite", StringComparison.Ordinal))
            return (await ReadSqliteColumnsAsync(connection, table, transaction)).Contains(column);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = provider.Contains("SqlServer", StringComparison.Ordinal)
            ? "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND name = @column"
            : "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = @column";
        foreach (var (name, value) in new[] { ("@table", table), ("@column", column) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static string GetSqliteType(string type) =>
        type.StartsWith("string:", StringComparison.Ordinal) ? "TEXT"
        : type == "guid" ? "TEXT"
        : type == "text" ? "TEXT"
        : type == "boolean" ? "INTEGER"
        : type == "integer" ? "INTEGER"
        : type == "date" ? "DATE"
        : type == "double" ? "REAL"
        : type == "datetime" ? "TEXT"
        : "NUMERIC";

    private static string GetPostgresType(string type) =>
        type.StartsWith("string:", StringComparison.Ordinal)
            ? $"character varying({type["string:".Length..]})"
            : type == "guid" ? "uuid"
            : type == "text" ? "text"
            : type == "boolean" ? "boolean"
            : type == "integer" ? "integer"
            : type == "date" ? "date"
            : type == "double" ? "DOUBLE PRECISION"
            : type == "datetime" ? "timestamp with time zone"
            : "numeric(5,2)";

    private static string GetSqlServerType(string type) =>
        type.StartsWith("string:", StringComparison.Ordinal)
            ? $"nvarchar({type["string:".Length..]})"
            : type == "guid" ? "uniqueidentifier"
            : type == "text" ? "nvarchar(max)"
            : type == "boolean" ? "bit"
            : type == "integer" ? "int"
            : type == "date" ? "date"
            : type == "double" ? "float"
            : type == "datetime" ? "datetime2"
            : "decimal(5,2)";
}
