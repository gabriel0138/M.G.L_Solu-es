using System.Net;
using Microsoft.Extensions.Configuration;
using MglConnect.Web.Services.RouteTrust;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public sealed class RoadEngineComparisonTests
{
    // Default is a deterministic recorded protocol response. A separately invoked
    // integration run may target ONLY an explicitly selected loopback engine.
    [Fact]
    public async Task TruckAdapterReadsRoadGeometryWithoutClaimingRestrictionCoverage()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"MglConnect.sln")))dir=dir.Parent;
        var bytes=File.ReadAllBytes(Path.Combine(dir!.FullName,"src/MglConnect.Tests/Fixtures/osrm/valhalla-truck-central-sp-20261105.response.json"));
        var endpoint=Environment.GetEnvironmentVariable("MGL_VALHALLA_COMPARE_URL");
        if(endpoint!=null && (!Uri.TryCreate(endpoint,UriKind.Absolute,out var u)||!u.IsLoopback||u.Scheme!="http"))throw new InvalidOperationException("Comparison requires an explicit loopback instance.");
        var factory=new Mock<IHttpClientFactory>();factory.Setup(f=>f.CreateClient("ValhallaExperimental")).Returns(()=>endpoint==null?new HttpClient(new Reply(bytes)):new HttpClient());
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Routing:Valhalla:Enabled","true"},{"Routing:Valhalla:BaseUrl",endpoint??"http://127.0.0.1:8008"}}).Build();
        var profile=new TruckPhysicalRequest(4.2m,2.6m,18.6m,28.5m,8m,5,false);
        var reply=await new ValhallaExperimentalRouter(factory.Object,config).RouteAsync(new(new(-23.5614,-46.6559),new(-23.5505,-46.6333),profile));
        Assert.Equal(ProviderFailure.None,reply.Failure);Assert.True(reply.Geometry.Count>10);Assert.Equal(3.388m,reply.DistanceKm);Assert.NotNull(reply.EstimatedMinutes);Assert.Equal(299.223,reply.DurationSeconds);
        Assert.Equal(profile,reply.PhysicalRequest);Assert.All(reply.Coverage.Values,v=>Assert.Equal(RestrictionOutcome.Desconhecido,v));
        Assert.False(reply.ToPlan().RotaHomologadaParaVeiculo);Assert.Null(reply.ToPlan().Confianca);Assert.NotNull(reply.ResponseSha256);
    }
    private sealed class Reply(byte[] bytes):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
    }
}
