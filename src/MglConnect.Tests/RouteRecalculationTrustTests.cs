using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using MglConnect.Web.Services.RouteTrust;
using Xunit;
namespace MglConnect.Tests;

// Trusted adapter is injected only in this unit/integration scope. The runtime
// Development file adapter requires Synthetic=true and cannot grant navigation.
public sealed class RouteRecalculationTrustTests
{
    [Theory][InlineData(false,true)][InlineData(true,false)][InlineData(false,false)]
    public async Task AlternativeGeometryIsReevaluatedAndAChangedProfileCannotPublishAnObsoleteResult(bool changeWhilePending,bool reverseCandidate)
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);
        var profile=RouteTestData.Profile();profile.TripId=f.Deliveries[0].Id;
        await f.ChangeAsync(db=>{db.Veiculos.Single(v=>v.Id==f.Deliveries[0].VeiculoId).PerfilOperacionalJson=JsonSerializer.Serialize(profile);db.Pedidos.Single(p=>p.Id==f.Deliveries[0].PedidoId).PesoKg=profile.Payload!.Value;});
        await using var db=new MglDbContext(f.Options);var evidence=RouteTestData.Complete(false);
        var identity=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};identity.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,f.Users["admin-a"].Id.ToString()),new Claim(ClaimTypes.Role,"Admin")},"fixture"));
        var trust=new RouteTrustService(db,new TestRouteSource(evidence),new(),identity,new RouteTestClock());
        var trip=f.Deliveries[0];var first=await trust.FinishAsync(await trust.BeginAsync(trip.Id),evidence.RecordedPlan!);
        Assert.True(first.NavigationAllowed);Assert.True(await trust.NavigationAllowedAsync(trip.Id));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new DelayedHandler(entered,release,evidence.RecordedPlan!,reverse:reverseCandidate);
        var factory=new Mock<IHttpClientFactory>();factory.Setup(x=>x.CreateClient(It.IsAny<string>())).Returns(()=>new HttpClient(handler,false));
        var engine=new Mock<IRouteDeviationEngine>();var last=evidence.RecordedPlan!.TrajetoCoordenadas.Last();
        engine.Setup(x=>x.GetState(trip.CodigoRastreio)).Returns(new VehicleTelemetryState{EntregaId=trip.Id,PerfilVeiculo="Pesado",DestinoLatitude=last.Latitude,DestinoLongitude=last.Longitude,DestinoNome="Public landmark fixture"});
        using var cache=new MemoryCache(new MemoryCacheOptions());var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Routing:OsrmBaseUrl","https://fixture.invalid"}}).Build();
        var routing=new TruckRoutingService(db,factory.Object,cache,config,NullLogger<TruckRoutingService>.Instance,engine.Object,trust,identity);
        var start=evidence.RecordedPlan.TrajetoCoordenadas.First();var calculating=routing.RecalcularRotaDinamicaAsync(trip.CodigoRastreio,start.Latitude,start.Longitude,"Pesado");await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(changeWhilePending)await f.ChangeAsync(ctx=>ctx.Veiculos.Single(v=>v.Id==trip.VeiculoId).PerfilOperacionalVersao++);
        release.SetResult();var candidate=await calculating;
        if(!changeWhilePending&&!reverseCandidate){Assert.NotNull(candidate);Assert.True(candidate.Confianca!.NavigationAllowed);}
        else Assert.Null(candidate);
        engine.Verify(e=>e.UpdateRouteAfterReroute(It.IsAny<string>(),It.IsAny<List<RouteCoordinate>>()),changeWhilePending||reverseCandidate?Times.Never():Times.Once());
        var current=await trust.CurrentAsync(trip.Id);Assert.Equal(!changeWhilePending&&!reverseCandidate,current!.NavigationAllowed);
        Assert.Equal(changeWhilePending,current.Obsolete);
        if(reverseCandidate)Assert.Contains(current.Checks,c=>c.Outcome==RestrictionOutcome.Desconhecido);
        Assert.Equal(2,await db.RotaAvaliacoes.CountAsync());
    }
    private sealed class DelayedHandler(TaskCompletionSource entered,TaskCompletionSource release,TruckRoutePlan plan,bool reverse):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            entered.TrySetResult();await release.Task.WaitAsync(ct);
            var points=reverse?plan.TrajetoCoordenadas.AsEnumerable().Reverse():plan.TrajetoCoordenadas;
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{code="Ok",routes=new[]{new{distance=5000,duration=600,geometry=new{coordinates=points.Select(p=>new[]{p.Longitude,p.Latitude})},legs=new[]{new{steps=Array.Empty<object>()}}}}}))};
        }
    }
}
