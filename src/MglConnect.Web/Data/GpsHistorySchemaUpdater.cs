using System.Data.Common;

namespace MglConnect.Web.Data;

// Additive only. Never backfill imaginary samples from snapshots or delete history.
public static class GpsHistorySchemaUpdater
{
    public static async Task EnsureAsync(DbConnection connection, string provider, DbTransaction transaction)
    {
        bool sqlServer = provider.Contains("SqlServer", StringComparison.Ordinal);
        bool postgres = provider.Contains("Npgsql", StringComparison.Ordinal);
        var identity = sqlServer ? "bigint IDENTITY(1,1) PRIMARY KEY" : postgres ? "bigserial PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT";
        var guid = sqlServer ? "uniqueidentifier" : postgres ? "uuid" : "TEXT";
        var time = sqlServer ? "datetime2" : postgres ? "timestamp with time zone" : "TEXT";
        var number = sqlServer ? "float" : postgres ? "DOUBLE PRECISION" : "REAL";
        var boolean = sqlServer ? "bit" : postgres ? "boolean" : "INTEGER";
        var create = sqlServer ? "IF OBJECT_ID(N'PosicoesGps', N'U') IS NULL CREATE TABLE" : "CREATE TABLE IF NOT EXISTS";
        await Execute($"""
            {create} "PosicoesGps" (
                "Id" {identity}, "EventoId" {guid} NOT NULL, "TenantId" {guid} NOT NULL,
                "EntregaId" INTEGER NOT NULL, "MotoristaId" INTEGER NOT NULL, "VeiculoId" INTEGER NOT NULL,
                "TurnoId" {guid} NULL, "AtorUsuarioId" INTEGER NOT NULL,
                "Latitude" {number} NOT NULL, "Longitude" {number} NOT NULL, "PrecisaoMetros" {number} NULL,
                "VelocidadeKmH" {number} NULL, "Direcao" {number} NULL,
                "CapturadoEmUtc" {time} NULL, "RecebidoEmUtc" {time} NOT NULL,
                "Origem" VARCHAR(12) NOT NULL, "CapturaInformada" {boolean} NOT NULL,
                CONSTRAINT "FK_PosicoesGps_Entrega" FOREIGN KEY ("EntregaId") REFERENCES "Entregas" ("Id"),
                CONSTRAINT "FK_PosicoesGps_Motorista" FOREIGN KEY ("MotoristaId") REFERENCES "Usuarios" ("Id"),
                CONSTRAINT "FK_PosicoesGps_Veiculo" FOREIGN KEY ("VeiculoId") REFERENCES "Veiculos" ("Id"),
                CONSTRAINT "FK_PosicoesGps_Turno" FOREIGN KEY ("TurnoId") REFERENCES "TurnosMotorista" ("Id"),
                CONSTRAINT "FK_PosicoesGps_Ator" FOREIGN KEY ("AtorUsuarioId") REFERENCES "Usuarios" ("Id")
            );
            """);
        foreach (var (name, columns, unique) in new[] {
            ("IX_PosicoesGps_EventoId", "\"EventoId\"", true),
            ("IX_PosicoesGps_TenantId_EntregaId_CapturadoEmUtc_Id", "\"TenantId\", \"EntregaId\", \"CapturadoEmUtc\", \"Id\"", false) })
        {
            var prefix = sqlServer
                ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('PosicoesGps')) CREATE"
                : "CREATE";
            await Execute($"{prefix} {(unique ? "UNIQUE " : "")}INDEX {(sqlServer ? "" : "IF NOT EXISTS ")}\"{name}\" ON \"PosicoesGps\" ({columns});");
        }
        async Task Execute(string sql)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
    }
}
