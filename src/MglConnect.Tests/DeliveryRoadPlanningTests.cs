using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using MglConnect.Web.Services.RouteTrust;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public sealed class DeliveryRoadPlanningTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public bool Fail;
        public int Calls;
        public bool Wait;
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct) {
            Calls++;if(Wait){Entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}
            return new HttpResponseMessage(Fail?HttpStatusCode.InternalServerError:HttpStatusCode.OK){Content=new StringContent(OsrmRoadRouterTests.ValidResponse)};
        }
    }
    [Fact]
    public async Task LinkedPlanningPersistsSeparatelyAndFailureCannotFallBackToOldGeometry()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);
        using var db=new MglDbContext(f.Options);var handler=new Handler();
        var factory=new Mock<IHttpClientFactory>();factory.Setup(x=>x.CreateClient("OsrmRoad")).Returns(new HttpClient(handler));
        var router=new OsrmRoadRouter(factory.Object,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Routing:OsrmBaseUrl","https://fixture.invalid"}}).Build());
        var service=new DeliveryRoadPlanning(db,router);int id=f.Deliveries[0].Id;
        Assert.Null(await service.ReadAsync(id));
        var plan=await service.CalculateAsync(id,[new(-23.55,-46.63),new(-23.551,-46.632)]);
        Assert.NotNull(plan);Assert.True(RoutePresentation.ValidGeometry(plan));Assert.NotNull(plan.PlanningId);
        Assert.Equal(61.2,plan.ProviderDurationSeconds);Assert.Equal(2,plan.RequestedStops.Count);Assert.False(plan.RotaHomologadaParaVeiculo);Assert.Null(plan.Confianca);Assert.False(plan.DemonstracaoControlada);
        Assert.Equal(RouteDisplayState.GenericUnverifiedTruck,RoutePresentation.From(plan).State);
        Assert.Equal(plan.PlanningId,(await service.ReadAsync(id))!.PlanningId);Assert.Equal(1,handler.Calls);
        Assert.Equal(0,await db.PosicoesGps.CountAsync());
        handler.Fail=true;var failed=await service.CalculateAsync(id,[new(-23.55,-46.63),new(-23.551,-46.632)]);
        Assert.NotNull(failed);Assert.NotEqual(plan.PlanningId,failed.PlanningId);Assert.False(failed.RotaDisponivel);Assert.Empty(failed.TrajetoCoordenadas);Assert.Null(RoutePresentation.From(failed).DistanceKm);
        Assert.Equal(2,await db.DeliveryRoadPlans.CountAsync());
        handler.Fail=false;var recovered=await service.CalculateAsync(id,[new(-23.55,-46.63),new(-23.551,-46.632)]);Assert.True(recovered!.RotaDisponivel);
        var delivery=await db.Entregas.FindAsync(id);delivery!.EnderecoDestino="Changed fictitious destination";await db.SaveChangesAsync();
        Assert.False((await service.ReadAsync(id))!.RotaDisponivel);Assert.Equal(3,handler.Calls);
        Assert.Equal(0,await db.RotaAvaliacoes.CountAsync());
    }
    [Fact]
    public async Task CancellationIsPersistedAsUnavailableAndASeparateAttemptRecovers()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);using var db=new MglDbContext(f.Options);
        var handler=new Handler();var factory=new Mock<IHttpClientFactory>();factory.Setup(x=>x.CreateClient("OsrmRoad")).Returns(()=>new HttpClient(handler,false));
        var service=new DeliveryRoadPlanning(db,new OsrmRoadRouter(factory.Object,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Routing:OsrmBaseUrl","https://fixture.invalid"}}).Build()));
        var points=new RouteLocation[]{new(-23.55,-46.63),new(-23.551,-46.632)};int id=f.Deliveries[0].Id;
        await service.CalculateAsync(id,points);handler.Wait=true;
        using var cancelled=new CancellationTokenSource();var request=service.CalculateAsync(id,points,cancelled.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));cancelled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>request);
        Assert.False((await service.ReadAsync(id))!.RotaDisponivel);
        handler.Wait=false;Assert.True((await service.CalculateAsync(id,points))!.RotaDisponivel);
        Assert.Equal(3,await db.DeliveryRoadPlans.CountAsync());
    }
    private sealed class FixedClock(DateTimeOffset now):TimeProvider {public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class ReorderedHandler:HttpMessageHandler {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            int call=Interlocked.Increment(ref calls);if(call==1){Entered.TrySetResult();await Release.Task.WaitAsync(ct);}
            return new(HttpStatusCode.OK){Content=new StringContent(OsrmRoadRouterTests.ValidResponse.Replace("1250",call==1?"1250":"2500"))};
        }
    }
    [Fact]
    public async Task ConcurrentRequestsWithIdenticalTimestampsUseClaimedRevisionAndOlderCompletionCannotWin()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);await using var db1=new MglDbContext(f.Options);await using var db2=new MglDbContext(f.Options);
        var handler=new ReorderedHandler();var factory=new Mock<IHttpClientFactory>();factory.Setup(x=>x.CreateClient("OsrmRoad")).Returns(()=>new HttpClient(handler,false));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Routing:OsrmBaseUrl","https://fixture.invalid"}}).Build();
        var clock=new FixedClock(DateTimeOffset.UtcNow);var one=new DeliveryRoadPlanning(db1,new(factory.Object,config),clock:clock);var two=new DeliveryRoadPlanning(db2,new(factory.Object,config),clock:clock);
        var points=new RouteLocation[]{new(-23.55,-46.63),new(-23.551,-46.632)};int id=f.Deliveries[0].Id;
        var first=one.CalculateAsync(id,points);await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second=await two.CalculateAsync(id,points);Assert.NotNull(second);Assert.Equal(2,second.PlanRevision);Assert.Equal(2.5m,second.DistanciaKm);
        handler.Release.SetResult();var olderResponse=await first;
        Assert.Equal(second.PlanningId,olderResponse!.PlanningId);Assert.Equal(second.PlanningId,(await one.ReadAsync(id))!.PlanningId);
        var attempts=await db1.DeliveryRoadPlans.AsNoTracking().OrderBy(p=>p.Revision).ToListAsync();Assert.Equal(2,attempts.Count);Assert.Equal(attempts[0].CreatedAtUtc,attempts[1].CreatedAtUtc);Assert.Equal(new[]{1,2},attempts.Select(a=>a.Revision));
    }
    [Fact]
    public async Task AddressDriverAndExpiryInvalidatePlanAndStaleConfirmationIsRejected()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);using var db=new MglDbContext(f.Options);
        var factory=new Mock<IHttpClientFactory>();factory.Setup(x=>x.CreateClient("OsrmRoad")).Returns(()=>new HttpClient(new Handler(),false));
        var service=new DeliveryRoadPlanning(db,new(factory.Object,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Routing:OsrmBaseUrl","https://fixture.invalid"}}).Build()));
        int id=f.Deliveries[0].Id;var points=new RouteLocation[]{new(-23.55,-46.63),new(-23.551,-46.632)};
        var old=await service.ContextAsync(id);await service.CalculateAsync(id,points);
        await f.ChangeAsync(ctx=>ctx.Entregas.Single(e=>e.Id==id).EnderecoDestino="Different public test landmark");
        Assert.False((await service.ReadAsync(id))!.RotaDisponivel);
        await Assert.ThrowsAsync<PlanningContextChangedException>(()=>service.CalculateAsync(id,points,expectedBinding:old!.Binding));
        await service.CalculateAsync(id,points);
        await f.ChangeAsync(ctx=>ctx.Entregas.Single(e=>e.MotoristaId==f.Users["driver-a-other"].Id).AtribuicaoAtiva=false);
        await f.ChangeAsync(ctx=>ctx.Entregas.Single(e=>e.Id==id).MotoristaId=f.Users["driver-a-other"].Id);
        Assert.False((await service.ReadAsync(id))!.RotaDisponivel);Assert.Null(await service.CalculateAsync(id,points,expectedDriver:f.Users["driver-a"].Id));
        var latest=await service.CalculateAsync(id,points);await f.ChangeAsync(ctx=>ctx.DeliveryRoadPlans.Single(p=>p.Id==latest!.PlanningId).ValidUntilUtc=DateTime.UtcNow.AddSeconds(-1));
        Assert.False((await service.ReadAsync(id))!.RotaDisponivel);Assert.Equal(latest!.PlanRevision,(await service.ReadAsync(id))!.PlanRevision);
    }
    [Fact]
    public async Task GeographicConfirmationRequiresEveryPointAndRejectsAChangedAddressSnapshot()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();using var admin=await f.LoginAsync("admin-a");int id=f.Deliveries[0].Id;
        var html=await admin.Client.GetStringAsync("/Roteamento?entregaId="+id);
        var binding=System.Text.RegularExpressions.Regex.Match(html,"name=\"binding\" value=\"([A-F0-9]+)\"").Groups[1].Value;Assert.Equal(64,binding.Length);
        var token=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,"/Roteamento?entregaId="+id);
        async Task<HttpStatusCode> Submit(bool confirmed,double latitude=-23.55){
            var stops=new[]{new ConfirmedRouteStop("Public test origin",latitude,-46.63,"Photon",confirmed),new ConfirmedRouteStop("Public test destination",-23.551,-46.632,"Map",true)};
            using var response=await admin.Client.PostAsync("/Roteamento/Confirmar",new FormUrlEncodedContent(new Dictionary<string,string>{{"entregaId",id.ToString()},{"binding",binding},{"confirmedStops",JsonSerializer.Serialize(stops)},{"__RequestVerificationToken",token}}));return response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.BadRequest,await Submit(false));Assert.Equal(HttpStatusCode.BadRequest,await Submit(true,91));
        Assert.Equal(HttpStatusCode.Redirect,await Submit(true));
        await f.ChangeAsync(db=>db.Entregas.Single(e=>e.Id==id).EnderecoDestino="A new fictitious public landmark");
        Assert.Equal(HttpStatusCode.Conflict,await Submit(true));
        await using var db=new MglDbContext(f.Options);Assert.Equal(1,await db.DeliveryRoadPlans.CountAsync());
        var saved=JsonSerializer.Deserialize<TruckRoutePlan>((await db.DeliveryRoadPlans.SingleAsync()).PlanJson)!;
        Assert.Equal(2,saved.ConfirmedStops.Count);Assert.All(saved.ConfirmedStops,p=>Assert.NotNull(p.ConfirmedAtUtc));
    }
    [Fact]
    public async Task ScopedEndpointAndMutationRequireOwnerRoleAndAntiforgery()
    {
        await using var f=await SecurityHttpFixture.CreateAsync();int id=f.Deliveries[0].Id;
        using var admin=await f.LoginAsync("admin-a");using var foreign=await f.LoginAsync("admin-b");using var customer=await f.LoginAsync("client-a");using var driver=await f.LoginAsync("driver-a");
        var fields=new Dictionary<string,string>{{"entregaId",id.ToString()},{"coordenadas","-46.63,-23.55;-46.632,-23.551"}};
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.Client.PostAsync("/Roteamento/Planejar",new FormUrlEncodedContent(fields))).StatusCode);
        fields["__RequestVerificationToken"]=await SecurityHttpFixture.AntiForgeryAsync(foreign.Client,"/Roteamento");
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.Client.PostAsync("/Roteamento/Planejar",new FormUrlEncodedContent(fields))).StatusCode);
        fields["__RequestVerificationToken"]=await SecurityHttpFixture.AntiForgeryAsync(admin.Client,"/Roteamento?entregaId="+id);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.Client.PostAsync("/Roteamento/Planejar",new FormUrlEncodedContent(fields))).StatusCode);
        var response=await driver.Client.GetAsync($"/Entregas/{id}/Planejamento");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("plan");Assert.False(json.GetProperty("rotaDisponivel").GetBoolean());Assert.NotEqual(JsonValueKind.Null,json.GetProperty("planningId").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.Client.GetAsync($"/Entregas/{id}/Planejamento")).StatusCode);
        Assert.Contains((await customer.Client.GetAsync($"/Entregas/{id}/Planejamento")).StatusCode,new[]{HttpStatusCode.Redirect,HttpStatusCode.Forbidden});
    }
}
