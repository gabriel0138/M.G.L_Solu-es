using System.Data.Common;
namespace MglConnect.Web.Data;

public static class GpsArchiveSchemaUpdater
{
    public static async Task EnsureAsync(DbConnection connection,string provider,DbTransaction transaction)
    {
        var sqlServer=provider.Contains("SqlServer",StringComparison.Ordinal);
        var postgres=provider.Contains("Npgsql",StringComparison.Ordinal);
        var guid=sqlServer?"uniqueidentifier":postgres?"uuid":"TEXT";
        var time=sqlServer?"datetime2":postgres?"timestamp with time zone":"TEXT";
        var boolean=sqlServer?"bit":postgres?"boolean":"INTEGER";
        var number=sqlServer?"float":postgres?"DOUBLE PRECISION":"REAL";
        var binary=sqlServer?"varbinary(max)":postgres?"bytea":"BLOB";
        var added=await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"PosicoesGps","OrdenadoEmUtc","datetime",nullable:true,transaction:transaction);
        await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"PosicoesGps","ForaDeOrdem","boolean",nullable:true,transaction:transaction);
        if(added)await Execute("UPDATE \"PosicoesGps\" SET \"OrdenadoEmUtc\" = COALESCE(\"CapturadoEmUtc\", \"RecebidoEmUtc\") WHERE \"OrdenadoEmUtc\" IS NULL;");
        await Execute($"""
            {Create("GpsArquivos")} "GpsArquivos" (
                "Id" {guid} NOT NULL PRIMARY KEY, "TenantId" {guid} NOT NULL, "EntregaId" INTEGER NOT NULL,
                "CriadoPorUsuarioId" INTEGER NOT NULL, "CriadoEmUtc" {time} NOT NULL,
                "CompactadoPorUsuarioId" INTEGER NULL, "CompactadoEmUtc" {time} NULL,
                "Quantidade" INTEGER NOT NULL, "PrimeiraSequencia" bigint NOT NULL, "UltimaSequencia" bigint NOT NULL,
                "Sha256" VARCHAR(64) NOT NULL, "ConteudoGzip" {binary} NOT NULL, "Versao" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "FK_GpsArquivos_Entrega" FOREIGN KEY ("EntregaId") REFERENCES "Entregas" ("Id"),
                CONSTRAINT "FK_GpsArquivos_Criador" FOREIGN KEY ("CriadoPorUsuarioId") REFERENCES "Usuarios" ("Id"),
                CONSTRAINT "FK_GpsArquivos_Compactador" FOREIGN KEY ("CompactadoPorUsuarioId") REFERENCES "Usuarios" ("Id")
            );
            """);
        await Execute($"""
            {Create("GpsEventosArquivados")} "GpsEventosArquivados" (
                "Id" bigint NOT NULL PRIMARY KEY, "EventoId" {guid} NOT NULL, "TenantId" {guid} NOT NULL,
                "EntregaId" INTEGER NOT NULL, "MotoristaId" INTEGER NOT NULL, "VeiculoId" INTEGER NOT NULL,
                "AtorUsuarioId" INTEGER NOT NULL, "TurnoId" {guid} NULL, "ArquivoId" {guid} NOT NULL,
                "CapturadoEmUtc" {time} NULL, "RecebidoEmUtc" {time} NOT NULL, "OrdenadoEmUtc" {time} NOT NULL,
                "PrecisaoMetros" {number} NULL, "Origem" VARCHAR(12) NOT NULL, "CapturaInformada" {boolean} NOT NULL,
                "ForaDeOrdem" {boolean} NULL, "ImpressaoRequisicao" VARCHAR(64) NOT NULL,
                CONSTRAINT "FK_GpsEventos_Arquivo" FOREIGN KEY ("ArquivoId") REFERENCES "GpsArquivos" ("Id"),
                CONSTRAINT "FK_GpsEventos_Entrega" FOREIGN KEY ("EntregaId") REFERENCES "Entregas" ("Id"),
                CONSTRAINT "FK_GpsEventos_Motorista" FOREIGN KEY ("MotoristaId") REFERENCES "Usuarios" ("Id"),
                CONSTRAINT "FK_GpsEventos_Ator" FOREIGN KEY ("AtorUsuarioId") REFERENCES "Usuarios" ("Id"),
                CONSTRAINT "FK_GpsEventos_Veiculo" FOREIGN KEY ("VeiculoId") REFERENCES "Veiculos" ("Id"),
                CONSTRAINT "FK_GpsEventos_Turno" FOREIGN KEY ("TurnoId") REFERENCES "TurnosMotorista" ("Id")
            );
            """);
        foreach(var table in new[]{"PosicoesGps","GpsEventosArquivados"})
        {
            await Index(table,$"IX_{table}_TenantId_EntregaId_OrdenadoEmUtc_Id","\"TenantId\",\"EntregaId\",\"OrdenadoEmUtc\",\"Id\"");
            await Index(table,$"IX_{table}_TenantId_EntregaId_RecebidoEmUtc","\"TenantId\",\"EntregaId\",\"RecebidoEmUtc\"");
            await Index(table,$"IX_{table}_TenantId_RecebidoEmUtc","\"TenantId\",\"RecebidoEmUtc\"");
        }
        await Index("GpsEventosArquivados","IX_GpsEventosArquivados_ArquivoId","\"ArquivoId\"");
        await Index("GpsEventosArquivados","IX_GpsEventosArquivados_TenantId_EntregaId_CapturadoEmUtc","\"TenantId\",\"EntregaId\",\"CapturadoEmUtc\"");
        await Index("GpsEventosArquivados","IX_GpsEventosArquivados_EventoId","\"EventoId\"",true);
        await Index("GpsArquivos","IX_GpsArquivos_TenantId_EntregaId_CriadoEmUtc","\"TenantId\",\"EntregaId\",\"CriadoEmUtc\"");
        string Create(string table)=>sqlServer?$"IF OBJECT_ID(N'{table}',N'U') IS NULL CREATE TABLE":"CREATE TABLE IF NOT EXISTS";
        Task Index(string table,string name,string columns,bool unique=false)=>Execute(sqlServer
            ?$"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='{name}' AND object_id=OBJECT_ID('{table}')) CREATE {(unique?"UNIQUE ":"")}INDEX \"{name}\" ON \"{table}\" ({columns});"
            :$"CREATE {(unique?"UNIQUE ":"")}INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({columns});");
        async Task Execute(string sql){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText=sql;await command.ExecuteNonQueryAsync();}
    }
}
