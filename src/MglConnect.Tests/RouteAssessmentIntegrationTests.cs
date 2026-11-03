using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using MglConnect.Web.Services.RouteTrust;
using Xunit;
namespace MglConnect.Tests;

public sealed class RouteAssessmentIntegrationTests
{
    private static async Task Profile(SecurityHttpFixture f)
    {
        var p=RouteTestData.Profile();p.TripId=f.Deliveries[0].Id;
        await f.ChangeAsync(db=>{var v=db.Veiculos.Single(v=>v.Id==f.Deliveries[0].VeiculoId);v.PerfilOperacionalJson=JsonSerializer.Serialize(p);v.PerfilOperacionalVersao++;});
    }
    [Theory][InlineData("vehicle")][InlineData("cargo")][InlineData("driver")][InlineData("rules")][InlineData("trip")]
    public async Task RelevantChangesInvalidatePersistedEvaluationsWithoutRewritingHistory(string change)
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false,activeShifts:false);await Profile(f);
        var replacement=new MglConnect.Web.Models.Usuario{TenantId=f.TenantA,Login="replacement-driver",Nome="Synthetic",Perfil="Motorista",SenhaHash="not-used"};if(change=="driver")await f.ChangeAsync(ctx=>ctx.Add(replacement));
        await using var db=new MglDbContext(f.Options);var source=new TestRouteSource(RouteTestData.Complete());var service=new RouteTrustService(db,source,new(),clock:new RouteTestClock());
        var ticket=await service.BeginAsync(f.Deliveries[0].Id,f.Users["admin-a"].Id);await service.FinishAsync(ticket,source.Data.RecordedPlan!);
        Assert.False((await service.CurrentAsync(ticket.Trip))!.Obsolete);
        if(change=="rules")source.Data.Rules[0]=source.Data.Rules[0] with{Version="changed"};
        else await f.ChangeAsync(ctx=>{var e=ctx.Entregas.Single(e=>e.Id==ticket.Trip);switch(change){case "vehicle":ctx.Veiculos.Single(v=>v.Id==e.VeiculoId).AlturaMetros+=.1m;break;case "cargo":ctx.Pedidos.Single(p=>p.Id==e.PedidoId).PesoKg+=10;break;case "driver":e.MotoristaId=replacement.Id;break;case "trip":e.EnderecoDestino="changed";break;}});
        var current=await service.CurrentAsync(ticket.Trip);Assert.True(current!.Obsolete);Assert.False(await service.NavigationAllowedAsync(ticket.Trip));Assert.Equal(1,await db.RotaAvaliacoes.CountAsync());
    }
    [Fact]
    public async Task ASlowerOlderCalculationCannotReplaceTheNewerResultAndHistorySurvivesRestart()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);await Profile(f);
        var evidence=RouteTestData.Complete();await using var aDb=new MglDbContext(f.Options);await using var bDb=new MglDbContext(f.Options);
        var a=new RouteTrustService(aDb,new TestRouteSource(evidence),new(),clock:new RouteTestClock());var b=new RouteTrustService(bDb,new TestRouteSource(evidence),new(),clock:new RouteTestClock());
        var earlier=await a.BeginAsync(f.Deliveries[0].Id,f.Users["admin-a"].Id);var newer=await b.BeginAsync(earlier.Trip,f.Users["admin-a"].Id);
        var latest=await b.FinishAsync(newer,evidence.RecordedPlan!);Assert.False(latest.Obsolete);
        var late=await a.FinishAsync(earlier,evidence.RecordedPlan!);Assert.True(late.Obsolete);Assert.False(late.NavigationAllowed);
        await using var restarted=new MglDbContext(f.Options);var current=await new RouteTrustService(restarted,new TestRouteSource(evidence),new(),clock:new RouteTestClock()).CurrentAsync(earlier.Trip);
        Assert.Equal(newer.Id,current!.CalculationId);Assert.False(current.Obsolete);Assert.Equal(2,await restarted.RotaAvaliacoes.CountAsync());
    }
    [Fact]
    public async Task PersistedBooleansCannotPromoteSyntheticOrUnverifiableDataToNavigation()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);await Profile(f);await using var db=new MglDbContext(f.Options);
        var source=new TestRouteSource(RouteTestData.Complete());var service=new RouteTrustService(db,source,new(),clock:new RouteTestClock());
        var result=await service.FinishAsync(await service.BeginAsync(f.Deliveries[0].Id,f.Users["admin-a"].Id),source.Data.RecordedPlan!);
        result.NavigationAllowed=true;result.Synthetic=false;result.Confidence=RouteConfidence.Homologada;
        await f.ChangeAsync(ctx=>ctx.RotaAvaliacoes.Single().ResultadoJson=JsonSerializer.Serialize(result));
        Assert.False(await service.NavigationAllowedAsync(f.Deliveries[0].Id));Assert.True((await service.CurrentAsync(f.Deliveries[0].Id))!.Obsolete);
    }
    [Fact]
    public async Task HttpProfileIsExplicitScopedVersionedAndCannotAcceptForgedConfidenceOrVehicleIds()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();using var admin=await f.LoginAsync("admin-a");var trip=f.Deliveries[0].Id;
        var token=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,$"/Rotas/Entrega/{trip}");
        var data=new Dictionary<string,string>{{"__RequestVerificationToken",token},{"Revision","0"},{"Height","4.2"},{"Width","2.6"},{"Length","18.6"},{"Pbt","30000"},{"Axles","6"},{"Category","caminhao"},{"Composition","simples"},{"Cargo","seca"},{"Hazardous","false"},{"VeiculoId",f.Deliveries[2].VeiculoId.ToString()!},{"TenantId",f.TenantB.ToString()},{"Confidence","Homologada"}};
        using(var saved=await admin.Client.PostAsync($"/Rotas/Entrega/{trip}/Perfil",new FormUrlEncodedContent(data)))Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
        using(var stale=await admin.Client.PostAsync($"/Rotas/Entrega/{trip}/Perfil",new FormUrlEncodedContent(data)))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        await using(var db=new MglDbContext(f.Options)){var v=await db.Veiculos.SingleAsync(v=>v.Id==f.Deliveries[0].VeiculoId);Assert.Equal(0,v.PerfilOperacionalVersao);Assert.Null(v.PerfilOperacionalJson);var declaration=await db.Entregas.SingleAsync(e=>e.Id==trip);Assert.Equal(1,declaration.PerfilViagemVersao);Assert.Single(await db.PerfisViagemRevisoes.ToListAsync());var profile=OperationalTruckProfile.Read(declaration.PerfilViagemJson);Assert.Equal(trip,profile.TripId);Assert.Equal(FactOrigin.Declarado,profile.Height!.Origin);Assert.Null((await db.Veiculos.SingleAsync(v=>v.Id==f.Deliveries[2].VeiculoId)).PerfilOperacionalJson);}
        using(var calc=await admin.Client.PostAsync($"/Rotas/Entrega/{trip}/Calcular",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"RotaAprovada","true"}})))Assert.Equal(HttpStatusCode.Redirect,calc.StatusCode);
        await using var verify=new MglDbContext(f.Options);var record=await verify.RotaAvaliacoes.SingleAsync();var result=JsonSerializer.Deserialize<RouteTrustResult>(record.ResultadoJson)!;Assert.Equal(RouteConfidence.Indisponivel,result.Confidence);Assert.False(result.NavigationAllowed);
        Assert.Equal(f.TenantA,record.TenantId);Assert.Equal(f.Users["admin-a"].Id,record.AtorUsuarioId);
    }
    [Theory][InlineData("client-a")][InlineData("driver-a")][InlineData("admin-b")]
    public async Task OtherProfilesOrOrganizationsCannotReadOrChangeTheAssessment(string login)
    {
        await using var f=await SecurityHttpFixture.CreateAsync();using var actor=await f.LoginAsync(login);var trip=f.Deliveries[0].Id;
        using var response=await actor.Client.GetAsync($"/Rotas/Entrega/{trip}");Assert.Equal(login=="admin-b"?HttpStatusCode.NotFound:HttpStatusCode.Redirect,response.StatusCode);
        var token=await SecurityHttpFixture.AntiForgeryAsync(actor.Client,login=="admin-b"?"/Frota":login=="driver-a"?"/Entregas":"/Cliente");
        using var mutation=await actor.Client.PostAsync($"/Rotas/Entrega/{trip}/Perfil",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"Height","4.2"}}));
        Assert.Equal(login=="admin-b"?HttpStatusCode.NotFound:HttpStatusCode.Redirect,mutation.StatusCode);
        await using var db=new MglDbContext(f.Options);Assert.Equal(0,await db.RotaAvaliacoes.CountAsync());Assert.Null((await db.Veiculos.FindAsync(f.Deliveries[0].VeiculoId))!.PerfilOperacionalJson);
    }
    [Fact]
    public async Task InvalidProfileOrForgeryCannotWriteAndRevocationBlocksTheAdministrativeRoute()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();using var admin=await f.LoginAsync("admin-a");var trip=f.Deliveries[0].Id;var path=$"/Rotas/Entrega/{trip}";
        var token=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,path);
        using(var invalid=await admin.Client.PostAsync(path+"/Perfil",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"Height","-1"}})))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using(var forged=await admin.Client.PostAsync(path+"/Calcular",new FormUrlEncodedContent(new Dictionary<string,string>())))Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
        await f.ChangeAsync(ctx=>ctx.Usuarios.Single(u=>u.Login=="admin-a").Ativo=false);
        using var revoked=await admin.Client.GetAsync(path);Assert.Equal(HttpStatusCode.Redirect,revoked.StatusCode);Assert.Equal("/Account/Login",revoked.Headers.Location!.ToString());
    }
    [Fact]
    public async Task BackupRestoresProfileAssessmentIdentityAndHistoryWithoutPromotingItAcrossSources()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);await Profile(f);
        await using(var db=new MglDbContext(f.Options))
        {var source=new TestRouteSource(RouteTestData.Complete());var service=new RouteTrustService(db,source,new(),clock:new RouteTestClock());await service.FinishAsync(await service.BeginAsync(f.Deliveries[0].Id,f.Users["admin-a"].Id),source.Data.RecordedPlan!);}
        var root=Directory.CreateTempSubdirectory("mgl-stage7a-recovery-").FullName;var backup=Path.Combine(root,"backup");var restore=Path.Combine(root,"restore");
        var manifest=await MglConnect.Web.Operations.SqliteRecovery.BackupAsync(f.DatabaseFile,backup);Assert.Equal(1,manifest.Rows["RotaAvaliacoes"]);
        await MglConnect.Web.Operations.SqliteRecovery.RestoreAsync(backup,restore);
        await using var restored=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite($"Data Source={Path.Combine(restore,"database.sqlite")}").Options);
        Assert.Equal(1,await restored.RotaAvaliacoes.CountAsync());Assert.NotNull((await restored.Veiculos.FindAsync(f.Deliveries[0].VeiculoId))!.PerfilOperacionalJson);
        var current=await new RouteTrustService(restored,new TestRouteSource(new()),new(),clock:new RouteTestClock()).CurrentAsync(f.Deliveries[0].Id);Assert.True(current!.Obsolete);Assert.False(current.NavigationAllowed);
    }

}
