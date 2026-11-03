using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using MglConnect.Web.Services.RouteTrust;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public sealed class TripProfileIsolationTests
{
    [Fact]
    public void ExplicitDeclarationOfNoPermitsSurvivesEditingWithoutBecomingUnknown()
    {
        var profile=RouteTestData.Profile();profile.PermitsDeclared=true;profile.Permits=[];
        var form=MglConnect.Web.ViewModels.TruckProfileForm.From(profile,1);Assert.Equal("nenhuma",form.Permits);
        Assert.True(form.ToProfile(1).PermitsDeclared);Assert.Empty(form.ToProfile(1).Permits);
    }
    internal static async Task VerifyStageSevenAUpgrade(MglDbContext db)
    {
        var actor=new Usuario{TenantId=Guid.NewGuid(),Nome="Synthetic",Login="7a-upgrade",Perfil="Admin",SenhaHash="unused"};
        var vehicle=new Veiculo{TenantId=actor.TenantId,Placa="UPG7B01",Modelo="Synthetic"};var client=new Cliente{TenantId=actor.TenantId,RazaoSocial="Synthetic",Cnpj="7B1-UPGRADE",Telefone="0000000000"};db.AddRange(actor,vehicle,client);await db.SaveChangesAsync();
        var order=new Pedido{TenantId=actor.TenantId,ClienteId=client.Id,NumeroPedido="7A-UPGRADE",Descricao="Synthetic"};db.Add(order);await db.SaveChangesAsync();
        var trip=new Entrega{TenantId=actor.TenantId,PedidoId=order.Id,VeiculoId=vehicle.Id,CodigoRastreio="7A-UPGRADE",AtribuicaoAtiva=false};db.Add(trip);await db.SaveChangesAsync();
        var legacy=RouteTestData.Profile();legacy.TripId=trip.Id;var raw=JsonSerializer.Serialize(legacy);vehicle.PerfilOperacionalJson=raw;vehicle.PerfilOperacionalVersao=3;await db.SaveChangesAsync();db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"PerfisViagemRevisoes\"");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Entregas\" DROP COLUMN \"PerfilViagemJson\"");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Entregas\" DROP COLUMN \"PerfilViagemVersao\"");
        await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(db);
        var upgraded=await db.Entregas.Include(e=>e.Veiculo).SingleAsync(e=>e.Id==trip.Id);Assert.Null(upgraded.PerfilViagemJson);Assert.Equal(0,upgraded.PerfilViagemVersao);
        Assert.Equal(raw,upgraded.Veiculo!.PerfilOperacionalJson);Assert.Equal(20000,TripProfiles.Read(upgraded).Payload!.Value);Assert.Equal(0,await db.PerfisViagemRevisoes.CountAsync());
    }
    [Fact]
    public async Task StageSevenASqliteUpgradePreservesLegacyJsonWithoutAutomaticCopying()
    { await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);await using var db=new MglDbContext(f.Options);await VerifyStageSevenAUpgrade(db); }
    private static Dictionary<string,string> Form(string token,int version,decimal payload=20000) => new()
    {
        ["__RequestVerificationToken"]=token,["Revision"]=version.ToString(),["Height"]="4.2",["Width"]="2.6",["Length"]="18.6",["Tare"]="10000",
        ["Payload"]=payload.ToString(System.Globalization.CultureInfo.InvariantCulture),["Pbt"]=(10000+payload).ToString(System.Globalization.CultureInfo.InvariantCulture),["Axles"]="6",
        ["Composition"]="simples",["Category"]="caminhao",["Cargo"]="seca",["Hazardous"]="false",["AxleLoadsKg"]=payload==20000?"5000;5000;5000;5000;5000;5000":"6000;6000;6000;6000;6000;6000"
    };
    [Fact]
    public async Task ReusingVehiclePreservesPriorTripDeclarationLegacyRecordAndIndependentRevisions()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();using var admin=await f.LoginAsync("admin-a");var trip=f.Deliveries[0].Id;
        int later=0;
        await f.ChangeAsync(db=>
        {
            var original=db.Entregas.Include(e=>e.Veiculo).Single(e=>e.Id==trip);var legacy=RouteTestData.Profile();legacy.TripId=trip;
            original.Veiculo!.PerfilOperacionalJson=JsonSerializer.Serialize(legacy);original.Veiculo.PerfilOperacionalVersao=3;
            db.Entregas.Add(new(){TenantId=f.TenantA,Pedido=new Pedido{TenantId=f.TenantA,ClienteId=db.Pedidos.Single(p=>p.Id==original.PedidoId).ClienteId,NumeroPedido="SYNTHETIC-NEXT-ORDER",Descricao="Synthetic independent trip",PesoKg=26000},VeiculoId=original.VeiculoId,MotoristaId=original.MotoristaId,CodigoRastreio="SYNTHETIC-NEXT-TRIP",AtribuicaoAtiva=false,StatusEntrega="Pendente"});
        });
        await using(var db=new MglDbContext(f.Options)){later=await db.Entregas.Where(e=>e.CodigoRastreio=="SYNTHETIC-NEXT-TRIP").Select(e=>e.Id).SingleAsync();}
        var token=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,$"/Rotas/Entrega/{trip}");
        using(var saved=await admin.Client.PostAsync($"/Rotas/Entrega/{trip}/Perfil",new FormUrlEncodedContent(Form(token,3))))Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
        var nextToken=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,$"/Rotas/Entrega/{later}");
        using(var saved=await admin.Client.PostAsync($"/Rotas/Entrega/{later}/Perfil",new FormUrlEncodedContent(Form(nextToken,0,26000))))Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
        await using var verify=new MglDbContext(f.Options);
        var first=await verify.Entregas.Include(e=>e.Veiculo).SingleAsync(e=>e.Id==trip);var next=await verify.Entregas.Include(e=>e.Veiculo).SingleAsync(e=>e.Id==later);
        Assert.Equal(20000,TripProfiles.Read(first).Payload!.Value);Assert.Equal(26000,TripProfiles.Read(next).Payload!.Value);
        Assert.Equal(3,first.Veiculo!.PerfilOperacionalVersao);Assert.Equal(20000,OperationalTruckProfile.Read(first.Veiculo.PerfilOperacionalJson).Payload!.Value);
        Assert.Equal(new[]{1,4},await verify.PerfisViagemRevisoes.OrderBy(r=>r.Revisao).Select(r=>r.Revisao).ToArrayAsync());
        var history=await verify.PerfisViagemRevisoes.SingleAsync(r=>r.EntregaId==trip);Assert.Equal(f.Users["admin-a"].Id,history.AtorUsuarioId);
        history.PerfilJson="{}";await Assert.ThrowsAsync<InvalidOperationException>(()=>verify.SaveChangesAsync());
    }
    [Fact]
    public void TripAndVehicleIdentityAreRequiredAndPhysicalCatalogueNeverBecomesCargo()
    {
        var p=RouteTestData.Profile();p.TripId=1;p.VehicleId=9;
        var vehicle=new Veiculo{Id=9,PerfilOperacionalJson=JsonSerializer.Serialize(p),PerfilOperacionalVersao=7};
        Assert.Null(TripProfiles.Read(new(){Id=2,VeiculoId=9,Veiculo=vehicle}).Payload);
        var trip=new Entrega{Id=1,VeiculoId=9,Veiculo=vehicle,PerfilViagemJson=JsonSerializer.Serialize(p),PerfilViagemVersao=1};
        Assert.Equal(20000,TripProfiles.Read(trip).Payload!.Value);trip.VeiculoId=10;Assert.Null(TripProfiles.Read(trip).Payload);
        trip.PerfilViagemJson="{invalid";Assert.Null(TripProfiles.Read(trip).Height);
    }
    [Fact]
    public async Task TripRevisionsSurviveVerifiedBackupAndRestoreWithTheirActorAndVehicleSnapshot()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();using var admin=await f.LoginAsync("admin-a");var id=f.Deliveries[0].Id;
        var token=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,$"/Rotas/Entrega/{id}");
        using(var saved=await admin.Client.PostAsync($"/Rotas/Entrega/{id}/Perfil",new FormUrlEncodedContent(Form(token,0))))Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
        var directory=Directory.CreateTempSubdirectory("mgl-7b1-restore-").FullName;
        var manifest=await MglConnect.Web.Operations.SqliteRecovery.BackupAsync(f.DatabaseFile,Path.Combine(directory,"backup"));Assert.Equal(1,manifest.Rows["PerfisViagemRevisoes"]);
        await MglConnect.Web.Operations.SqliteRecovery.RestoreAsync(Path.Combine(directory,"backup"),Path.Combine(directory,"restore"));
        await using var db=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseSqlite($"Data Source={Path.Combine(directory,"restore/database.sqlite")}").Options);
        var revision=await db.PerfisViagemRevisoes.SingleAsync();Assert.Equal(f.Users["admin-a"].Id,revision.AtorUsuarioId);Assert.Equal(id,OperationalTruckProfile.Read(revision.PerfilJson).TripId);
        Assert.Equal(1,(await db.Entregas.SingleAsync(e=>e.Id==id)).PerfilViagemVersao);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ExperimentalResultIsPersistedButChangesWhileProviderIsPendingMakeItObsolete(bool change)
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);var id=f.Deliveries[0].Id;
        await f.ChangeAsync(db=>{var e=db.Entregas.Single(e=>e.Id==id);var p=RouteTestData.Profile();p.TripId=id;p.VehicleId=e.VeiculoId;e.PerfilViagemJson=JsonSerializer.Serialize(p);e.PerfilViagemVersao=1;db.Pedidos.Single(o=>o.Id==e.PedidoId).PesoKg=20000;});
        await using var db=new MglDbContext(f.Options);var service=new RouteTrustService(db,new TestRouteSource(new()),new());
        var ticket=await service.BeginAsync(id,f.Users["admin-a"].Id);var response=ValhallaExperimentalRouter.Parse(ValhallaContractTests.Response);
        if(change)await f.ChangeAsync(ctx=>{var e=ctx.Entregas.Single(e=>e.Id==id);e.PerfilViagemVersao++;});
        var result=await service.FinishAsync(ticket,response.ToPlan(),providerReply:response);Assert.Equal(change,result.Obsolete);Assert.False(result.NavigationAllowed);
        Assert.Equal(RouteConfidence.Estimada,result.Confidence);Assert.Equal(10,result.Checks.Count(c=>c.Outcome==RestrictionOutcome.Desconhecido));
        Assert.Equal(response.ResponseSha256,result.ProviderReply!.ResponseSha256);Assert.NotNull(result.PermanentVehicleSnapshot);
        Assert.Equal(change,(await service.CurrentAsync(id))!.Obsolete);Assert.False(await service.NavigationAllowedAsync(id));
    }
    [Fact]
    public async Task RoutingIntegrationUsesExperimentalAdapterAndNeverFallsBackToOsrm()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);var id=f.Deliveries[0].Id;
        await f.ChangeAsync(db=>{var e=db.Entregas.Single(e=>e.Id==id);var p=RouteTestData.Profile();p.TripId=id;p.VehicleId=e.VeiculoId;e.PerfilViagemJson=JsonSerializer.Serialize(p);e.PerfilViagemVersao=1;db.Pedidos.Single(o=>o.Id==e.PedidoId).PesoKg=20000;});
        using var handler=new ValhallaContractTests.Handler((request,_)=>
        {
            Assert.Equal("photon.invalid",request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"features\":[{\"geometry\":{\"coordinates\":[-46.656,-23.561]}}]}")});
        });
        var experimental=new Mock<IExperimentalTruckRouter>();experimental.SetupGet(e=>e.Enabled).Returns(true);
        experimental.Setup(e=>e.RouteAsync(It.IsAny<ProviderRouteRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(ValhallaExperimentalRouter.Parse(ValhallaContractTests.Response));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Geocoding:PhotonEnabled"]="true",["Geocoding:PhotonBaseUrl"]="https://photon.invalid"}).Build();
        await using var db=new MglDbContext(f.Options);using var cache=new MemoryCache(new MemoryCacheOptions());
        var router=new TruckRoutingService(db,ValhallaContractTests.Factory(handler),cache,config,Mock.Of<ILogger<TruckRoutingService>>(),Mock.Of<IRouteDeviationEngine>(),new RouteTrustService(db,new TestRouteSource(new()),new()),experimental:experimental.Object);
        var plan=await router.CalcularRotaParaEntregaAsync(id);Assert.True(plan.RotaDisponivel);Assert.Equal("truck",plan.Confianca!.RoutingProfile);Assert.False(plan.Confianca.NavigationAllowed);
        experimental.Verify(e=>e.RouteAsync(It.IsAny<ProviderRouteRequest>(),It.IsAny<CancellationToken>()),Times.Once);
        Assert.Null(await router.RecalcularRotaDinamicaAsync(f.Deliveries[0].CodigoRastreio,-23.56,-46.65));
    }
}
