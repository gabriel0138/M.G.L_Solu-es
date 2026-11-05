using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MglConnect.Web.Services;
using MglConnect.Web.Models;
using MglConnect.Web.Data;
using Npgsql;
using Xunit;

namespace MglConnect.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MGLCONNECT_POSTGRES_TEST_CONNECTION")))
            Skip = "Execute scripts/test-postgres-local.sh para usar uma instância local exclusivamente de testes.";
    }
}

public sealed class PostgresIntegrityTests
{
    [PostgresFact,Trait("Provider","Postgres")]
    public async Task StageSevenATripProfileUpgradeIsAdditiveAndKeepsTheLegacyDeclaration()
    { await using var db=await CreateAsync();await db.Database.EnsureCreatedAsync();await TripProfileIsolationTests.VerifyStageSevenAUpgrade(db); }
    [PostgresFact, Trait("Provider", "Postgres")]
    public async Task DeliveryFiveRevisionUpgradePreservesHistoricalPlanningAndCanRunTwice()
    {
        await using var db = await CreateAsync();
        await using var fixture = await SecurityHttpFixture.CreateAsync(postgresConnection: db.Database.GetConnectionString());
        var trip = fixture.Deliveries[0];
        var history = new DeliveryRoadPlan { Id = Guid.NewGuid(), TenantId = fixture.TenantA,
            EntregaId = trip.Id, CreatedAtUtc = DateTime.UtcNow, ValidUntilUtc = DateTime.UtcNow.AddHours(1),
            BindingHash = "historical-test-only", LocationsJson = "[]", PlanJson = "{\"historical\":true}" };
        db.DeliveryRoadPlans.Add(history); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Entregas\" DROP COLUMN \"RoadPlanRevision\"; ALTER TABLE \"DeliveryRoadPlans\" DROP COLUMN \"Revision\";");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        db.ChangeTracker.Clear();
        var preserved = await db.DeliveryRoadPlans.SingleAsync();
        Assert.Equal(history.Id, preserved.Id); Assert.Equal(history.PlanJson, preserved.PlanJson);
        Assert.Equal(0, preserved.Revision);
        Assert.Equal(0, (await db.Entregas.SingleAsync(e => e.Id == trip.Id)).RoadPlanRevision);
        // Match the atomic revision reservation used by DeliveryRoadPlanning on a real provider.
        Assert.Equal(1, await db.Entregas.Where(e => e.Id == trip.Id).ExecuteUpdateAsync(set => set.SetProperty(e => e.RoadPlanRevision, e => e.RoadPlanRevision + 1)));
        Assert.Equal(1, (await db.Entregas.AsNoTracking().SingleAsync(e => e.Id == trip.Id)).RoadPlanRevision);
    }

    internal static async Task<MglDbContext> CreateAsync()
    {
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("MGLCONNECT_POSTGRES_TEST_CONNECTION"));
        // Never reuse any application database, even when a connection is supplied.
        if (cs.Database != "postgres") throw new InvalidOperationException("A conexão de ensaio deve apontar para postgres.");
        var name = "mgl_stage6_" + Guid.NewGuid().ToString("N");
        await using (var control = new NpgsqlConnection(cs.ConnectionString))
        {
            await control.OpenAsync();
            await using var create = control.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync();
        }
        cs.Database = name;
        return new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseNpgsql(cs.ConnectionString).Options);
    }

    [PostgresFact, Trait("Provider", "Postgres")]
    public async Task FreshDatabaseExercisesAllRelationshipsOperationsAndAdditiveRestart()
    {
        await using var db = await CreateAsync();
        await db.Database.EnsureCreatedAsync();
        await RelationalIntegrityTests.VerifyFreshAsync(db);
    }
    [PostgresFact, Trait("Provider", "Postgres")]
    public async Task HttpGpsPodArchiveAndNativeDumpRestoreKeepPermissionsAndPermanentIdentity()
    {
        await using var original=await CreateAsync();
        await using var f=await SecurityHttpFixture.CreateAsync(postgresConnection:original.Database.GetConnectionString());
        var sample=await RecoveryTests.Prepare(f);
        using(var driverBefore=await f.LoginAsync("driver-a"))
        using(var request=driverBefore.Api(HttpMethod.Post,"/api/gps/sincronizar",new{eventos=new[]{sample}}))
        using(var response=await driverBefore.Client.SendAsync(request))
        {
            var receipt=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipts")[0];
            Assert.Equal("duplicado",receipt.GetProperty("estado").GetString());
            Assert.Equal(0,receipt.GetProperty("capturadoEmUtc").GetDateTime().Ticks%10);
        }
        var archive=new GpsArchiveService(original,Options.Create(new GpsHistoryOptions{DiasAntesCompactar=0,TamanhoLoteArquivo=1}));
        var file=await archive.ArchiveNextAsync(f.Users["admin-a"].Id,sample.EntregaId);Assert.True(file.Success);
        Assert.True((await archive.CompactAsync(f.Users["admin-a"].Id,file.ArquivoId!.Value,true)).Success);
        var trust=new MglConnect.Web.Services.RouteTrust.RouteTrustService(original,new TestRouteSource(new()),new());
        await trust.FinishAsync(await trust.BeginAsync(sample.EntregaId,f.Users["admin-a"].Id),new TruckRoutePlan());
        var revisionProfile=RouteTestData.Profile();revisionProfile.TripId=sample.EntregaId;revisionProfile.VehicleId=f.Deliveries[0].VeiculoId;
        var revisionJson=JsonSerializer.Serialize(revisionProfile);
        var declaredTrip=await original.Entregas.SingleAsync(e=>e.Id==sample.EntregaId);declaredTrip.PerfilViagemJson=revisionJson;declaredTrip.PerfilViagemVersao=1;
        original.PerfisViagemRevisoes.Add(new(){TenantId=f.TenantA,EntregaId=sample.EntregaId,VeiculoId=revisionProfile.VehicleId!.Value,AtorUsuarioId=f.Users["admin-a"].Id,Revisao=1,CriadoEmUtc=DateTime.UtcNow,PerfilJson=revisionJson,CadastroVeiculoJson=MglConnect.Web.Services.RouteTrust.TripProfiles.VehicleSnapshot(await original.Veiculos.SingleAsync(v=>v.Id==revisionProfile.VehicleId))});
        await original.SaveChangesAsync();
        var path=Directory.CreateTempSubdirectory("mgl-stage6-pgdump-").FullName;
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var dump=Path.Combine(path,"snapshot.dump");
        await Tool("pg_dump",original.Database.GetConnectionString()!,"--format=custom","--file",dump);
        Assert.True(new FileInfo(dump).Length>0);
        await using var target=await CreateAsync();
        await Tool("pg_restore",target.Database.GetConnectionString()!,"--exit-on-error","--no-owner",dump);
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(target);
        Assert.Equal(1,await target.PerfisViagemRevisoes.CountAsync());Assert.Equal(revisionJson,(await target.PerfisViagemRevisoes.SingleAsync()).PerfilJson);Assert.Equal(1,(await target.Entregas.FindAsync(sample.EntregaId))!.PerfilViagemVersao);
        Assert.Equal(1,await target.RotaAvaliacoes.CountAsync());Assert.Equal(1,await target.GpsArquivos.CountAsync());Assert.Equal(1,await target.GpsEventosArquivados.CountAsync());Assert.Equal(1,await target.PosicoesGps.CountAsync());
        Assert.Equal("Entregue",(await target.Entregas.FindAsync(sample.EntregaId))!.StatusEntrega);
        await using var restored=await SecurityHttpFixture.CreateAsync(postgresConnection:target.Database.GetConnectionString(),seedDatabase:false);
        using var customer=await restored.LoginAsync("client-a");using var foreign=await restored.LoginAsync("client-b");using var driver=await restored.LoginAsync("driver-a");
        using(var pdf=await customer.Client.GetAsync($"/Cliente/Pedido/{f.Deliveries[0].PedidoId}/Comprovante"))Assert.Equal(HttpStatusCode.OK,pdf.StatusCode);
        using(var denied=await foreign.Client.GetAsync($"/api/gps/historico/{sample.EntregaId}"))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        using(var request=driver.Api(HttpMethod.Post,"/api/gps/sincronizar",new{eventos=new[]{sample}}))
        using(var response=await driver.Client.SendAsync(request))Assert.Equal("duplicado",(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receipts")[0].GetProperty("estado").GetString());
        Assert.Equal(2,(await customer.Client.GetFromJsonAsync<JsonElement>($"/api/gps/historico/{sample.EntregaId}")).GetProperty("total").GetInt32());
        using var admin=await restored.LoginAsync("admin-a");using var csv=await admin.Client.GetAsync($"/GpsHistorico/Exportar?entregaId={sample.EntregaId}");Assert.Equal(HttpStatusCode.OK,csv.StatusCode);
    }

    [PostgresFact, Trait("Provider", "Postgres")]
    public async Task StageFourGpsUpgradeIsAdditiveAndPreservesUnknownCaptureAndOriginalUuid()
    {
        await using var db=await CreateAsync();var script=db.Database.GenerateCreateScript();
        foreach(var part in script.Split(';',StringSplitOptions.RemoveEmptyEntries))
        {
            if(part.Contains("GpsArquivos")||part.Contains("GpsEventosArquivados")||part.TrimStart().StartsWith("CREATE INDEX")&&part.Contains("OrdenadoEmUtc"))continue;
            var sql=System.Text.RegularExpressions.Regex.Replace(part,"^.*\\\"(?:OrdenadoEmUtc|ForaDeOrdem)\\\"[^\\n]*\\n?","",System.Text.RegularExpressions.RegexOptions.Multiline);
            if(!string.IsNullOrWhiteSpace(sql))
            {await using var command=db.Database.GetDbConnection().CreateCommand();await db.Database.OpenConnectionAsync();command.CommandText=sql+";";await command.ExecuteNonQueryAsync();}
        }
        var driver=new Usuario{Nome="Synthetic",Login="upgrade",SenhaHash="unused",Perfil="Motorista"};var vehicle=new Veiculo{Placa="PGU1A11",Modelo="Synthetic"};var customer=new Cliente{RazaoSocial="Synthetic",Cnpj="UPGRADE-ONLY",Telefone="0000000000"};db.AddRange(driver,vehicle,customer);await db.SaveChangesAsync();
        var order=new Pedido{ClienteId=customer.Id,NumeroPedido="UPGRADE-ONLY",Descricao="Synthetic"};db.Add(order);await db.SaveChangesAsync();var trip=new Entrega{PedidoId=order.Id,MotoristaId=driver.Id,VeiculoId=vehicle.Id,CodigoRastreio="UPGRADE-ONLY"};db.Add(trip);await db.SaveChangesAsync();
        var id=Guid.NewGuid();var received=DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PosicoesGps\" (\"EventoId\",\"TenantId\",\"EntregaId\",\"MotoristaId\",\"VeiculoId\",\"AtorUsuarioId\",\"Latitude\",\"Longitude\",\"RecebidoEmUtc\",\"Origem\",\"CapturaInformada\") VALUES ({id},{Guid.Empty},{trip.Id},{driver.Id},{vehicle.Id},{driver.Id},{-23d},{-46d},{received},{"GPS"},{false})");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        var row=await db.PosicoesGps.SingleAsync();Assert.Equal(id,row.EventoId);Assert.Null(row.CapturadoEmUtc);Assert.False(row.CapturaInformada);Assert.Equal(row.RecebidoEmUtc,row.OrdenadoEmUtc);Assert.Equal(0,await db.GpsArquivos.CountAsync());
    }

    [PostgresFact,Trait("Provider","Postgres")]
    public async Task RouteProfilesAndCalculationVersionsPersistAndBecomeObsoleteAfterOperationalChanges()
    {
        await using var original=await CreateAsync();await using var f=await SecurityHttpFixture.CreateAsync(postgresConnection:original.Database.GetConnectionString());
        var p=RouteTestData.Profile();p.TripId=f.Deliveries[0].Id;
        await f.ChangeAsync(db=>{var v=db.Veiculos.Single(v=>v.Id==f.Deliveries[0].VeiculoId);v.PerfilOperacionalJson=JsonSerializer.Serialize(p);v.PerfilOperacionalVersao=1;});
        var source=new TestRouteSource(RouteTestData.Complete());var service=new MglConnect.Web.Services.RouteTrust.RouteTrustService(original,source,new(),clock:new RouteTestClock());
        var ticket=await service.BeginAsync(f.Deliveries[0].Id,f.Users["admin-a"].Id);await service.FinishAsync(ticket,source.Data.RecordedPlan!);
        Assert.False((await service.CurrentAsync(ticket.Trip))!.Obsolete);
        await f.ChangeAsync(db=>db.Pedidos.Single(o=>o.Id==f.Deliveries[0].PedidoId).PesoKg+=1);
        Assert.True((await service.CurrentAsync(ticket.Trip))!.Obsolete);Assert.Equal(1,await original.RotaAvaliacoes.CountAsync());
    }
    private static async Task Tool(string name,string connection,params string[] arguments)
    {
        var cs=new NpgsqlConnectionStringBuilder(connection);
        var start=new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("MGL_POSTGRES_BIN")??"/usr/lib/postgresql/18/bin",name))
        {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var item in new[]{"--host",cs.Host!,"--port",cs.Port.ToString(),"--username",cs.Username!,"--dbname",cs.Database!})start.ArgumentList.Add(item);
        foreach(var item in arguments)start.ArgumentList.Add(item);
        if(!string.IsNullOrEmpty(cs.Password))start.Environment["PGPASSWORD"]=cs.Password;
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));await output;var error=await errors;
        Assert.True(process.ExitCode==0,name+" failed: "+error);
    }

}
