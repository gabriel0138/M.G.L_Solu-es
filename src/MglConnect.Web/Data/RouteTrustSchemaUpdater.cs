using System.Data.Common;
namespace MglConnect.Web.Data;
public static class RouteTrustSchemaUpdater
{
    public static async Task EnsureAsync(DbConnection connection,string provider,DbTransaction transaction)
    {
        bool sql=provider.Contains("SqlServer"),pg=provider.Contains("Npgsql");
        await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"Veiculos","PerfilOperacionalJson","text",nullable:true,transaction:transaction);
        await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"Veiculos","PerfilOperacionalVersao","integer",transaction:transaction);
        await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"Entregas","RotaCalculoAtualId","guid",nullable:true,transaction:transaction);
        await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"Entregas","PerfilViagemJson","text",nullable:true,transaction:transaction);
        await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"Entregas","PerfilViagemVersao","integer",transaction:transaction);
        var guid=sql?"uniqueidentifier":pg?"uuid":"TEXT";var date=sql?"datetime2":pg?"timestamp with time zone":"TEXT";
        var text=sql?"nvarchar(max)":"TEXT";var boolean=sql?"bit":pg?"boolean":"INTEGER";
        await using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText=$"""
            {(sql?"IF OBJECT_ID(N'RotaAvaliacoes',N'U') IS NULL CREATE TABLE":"CREATE TABLE IF NOT EXISTS")} "RotaAvaliacoes" (
            "Id" {guid} NOT NULL PRIMARY KEY,"TenantId" {guid} NOT NULL,"EntregaId" INTEGER NOT NULL,"VeiculoId" INTEGER NULL,"AtorUsuarioId" INTEGER NULL,
            "VinculoHash" VARCHAR(64) NOT NULL,"RegrasHash" VARCHAR(64) NOT NULL,"CriadoEmUtc" {date} NOT NULL,"ExpiraEmUtc" {date} NOT NULL,"Obsoleta" {boolean} NOT NULL,"ResultadoJson" {text} NOT NULL,
            CONSTRAINT "FK_RotaAvaliacao_Entrega" FOREIGN KEY ("EntregaId") REFERENCES "Entregas"("Id"),
            CONSTRAINT "FK_RotaAvaliacao_Veiculo" FOREIGN KEY ("VeiculoId") REFERENCES "Veiculos"("Id"),
            CONSTRAINT "FK_RotaAvaliacao_Ator" FOREIGN KEY ("AtorUsuarioId") REFERENCES "Usuarios"("Id"));
            {(sql?"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RotaAvaliacoes_TenantId_EntregaId_CriadoEmUtc' AND object_id=OBJECT_ID('RotaAvaliacoes')) CREATE INDEX":"CREATE INDEX IF NOT EXISTS")} "IX_RotaAvaliacoes_TenantId_EntregaId_CriadoEmUtc" ON "RotaAvaliacoes" ("TenantId","EntregaId","CriadoEmUtc");
            {(sql?"IF OBJECT_ID(N'PerfisViagemRevisoes',N'U') IS NULL CREATE TABLE":"CREATE TABLE IF NOT EXISTS")} "PerfisViagemRevisoes" (
            "Id" {guid} NOT NULL PRIMARY KEY,"TenantId" {guid} NOT NULL,"EntregaId" INTEGER NOT NULL,"VeiculoId" INTEGER NOT NULL,"AtorUsuarioId" INTEGER NOT NULL,
            "Revisao" INTEGER NOT NULL,"CriadoEmUtc" {date} NOT NULL,"PerfilJson" {text} NOT NULL,"CadastroVeiculoJson" {text} NOT NULL,
            CONSTRAINT "FK_PerfilViagem_Entrega" FOREIGN KEY ("EntregaId") REFERENCES "Entregas"("Id"),
            CONSTRAINT "FK_PerfilViagem_Veiculo" FOREIGN KEY ("VeiculoId") REFERENCES "Veiculos"("Id"),
            CONSTRAINT "FK_PerfilViagem_Ator" FOREIGN KEY ("AtorUsuarioId") REFERENCES "Usuarios"("Id"));
            {(sql?"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_PerfisViagemRevisoes_TenantId_EntregaId_Revisao' AND object_id=OBJECT_ID('PerfisViagemRevisoes')) CREATE UNIQUE INDEX":"CREATE UNIQUE INDEX IF NOT EXISTS")} "IX_PerfisViagemRevisoes_TenantId_EntregaId_Revisao" ON "PerfisViagemRevisoes" ("TenantId","EntregaId","Revisao");
            {(sql?"IF OBJECT_ID(N'DeliveryRoadPlans',N'U') IS NULL CREATE TABLE":"CREATE TABLE IF NOT EXISTS")} "DeliveryRoadPlans" (
            "Id" {guid} NOT NULL PRIMARY KEY,"TenantId" {guid} NOT NULL,"EntregaId" INTEGER NOT NULL,
            "CreatedAtUtc" {date} NOT NULL,"ValidUntilUtc" {date} NOT NULL,"BindingHash" VARCHAR(64) NOT NULL,
            "LocationsJson" {text} NOT NULL,"PlanJson" {text} NOT NULL,
            CONSTRAINT "FK_DeliveryRoadPlan_Entrega" FOREIGN KEY ("EntregaId") REFERENCES "Entregas"("Id"));
            {(sql?"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DeliveryRoadPlans_TenantId_EntregaId_CreatedAtUtc' AND object_id=OBJECT_ID('DeliveryRoadPlans')) CREATE INDEX":"CREATE INDEX IF NOT EXISTS")} "IX_DeliveryRoadPlans_TenantId_EntregaId_CreatedAtUtc" ON "DeliveryRoadPlans" ("TenantId","EntregaId","CreatedAtUtc");
            """;
        await command.ExecuteNonQueryAsync();
        await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"Entregas","RoadPlanRevision","integer",transaction:transaction);
        await DatabaseSchemaUpdater.EnsureColumnAsync(connection,provider,"DeliveryRoadPlans","Revision","integer",transaction:transaction);
    }
}
