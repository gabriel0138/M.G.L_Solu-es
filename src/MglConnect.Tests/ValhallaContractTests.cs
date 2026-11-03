using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MglConnect.Web.Services.RouteTrust;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public sealed class ValhallaContractTests
{
    internal static string FixturePath
    {
        get { var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"MglConnect.sln")))directory=directory.Parent;
            return Path.Combine(directory!.FullName,"src/MglConnect.Tests/Fixtures/valhalla-contract-synthetic.json"); }
    }
    internal static byte[] Response => File.ReadAllBytes(FixturePath);
    private static IConfiguration Config(bool enabled = true, string url = "http://127.0.0.1:9191") => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["Routing:Valhalla:Enabled"] = enabled.ToString(), ["Routing:Valhalla:BaseUrl"] = url }).Build();
    private static ProviderRouteRequest Request => new(new(-23.561, -46.656), new(-23.550, -46.633), TruckPhysicalRequest.From(RouteTestData.Profile())!);
    internal sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply(request,cancellationToken); }
    internal static IHttpClientFactory Factory(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f=>f.CreateClient(It.IsAny<string>())).Returns(()=>new HttpClient(handler,false)); return factory.Object;
    }
    [Fact]
    public async Task ConvertsPhysicalUnitsAndSendsExplicitTruckParametersWithoutPermissiveFlags()
    {
        string? body = null;
        using var handler = new Handler(async (request,_)=>
        {
            Assert.Equal(HttpMethod.Post,request.Method); Assert.Equal("/route",request.RequestUri!.AbsolutePath);
            body = await request.Content!.ReadAsStringAsync(); return new(HttpStatusCode.OK){Content=new ByteArrayContent(Response)};
        });
        var result=await new ValhallaExperimentalRouter(Factory(handler),Config()).RouteAsync(Request);
        Assert.Equal(ProviderFailure.None,result.Failure); Assert.Equal(189,result.Geometry.Count);
        using var json=JsonDocument.Parse(body!);var root=json.RootElement;Assert.Equal("truck",root.GetProperty("costing").GetString());
        var truck=root.GetProperty("costing_options").GetProperty("truck");
        Assert.Equal(30m,truck.GetProperty("weight").GetDecimal());Assert.Equal(5m,truck.GetProperty("axle_load").GetDecimal());
        Assert.Equal(4.2m,truck.GetProperty("height").GetDecimal());Assert.Equal(2.6m,truck.GetProperty("width").GetDecimal());Assert.Equal(18.6m,truck.GetProperty("length").GetDecimal());
        Assert.Equal(6,truck.GetProperty("axle_count").GetInt32());Assert.False(truck.GetProperty("hazmat").GetBoolean());
        Assert.False(truck.TryGetProperty("ignore_restrictions",out _));Assert.Equal(43200,truck.GetProperty("hgv_no_access_penalty").GetInt32());
        Assert.Null(result.GraphVersion);Assert.Empty(result.Segments);Assert.All(result.Coverage.Values,x=>Assert.Equal(RestrictionOutcome.Desconhecido,x));
        Assert.False(result.ToPlan().RotaAprovada);Assert.False(result.ToPlan().RotaHomologadaParaVeiculo);Assert.Empty(result.Evidence().Rules);Assert.NotNull(result.ResponseSha256);
    }
    [Theory][InlineData("height")][InlineData("weight")][InlineData("axles")][InlineData("axle-load")][InlineData("hazmat")]
    public void MissingPhysicalFactsCannotBecomeProviderDefaults(string missing)
    {
        var p=RouteTestData.Profile();switch(missing){case "height":p.Height=null;break;case "weight":p.Pbt=null;p.Pbtc=null;p.Tare=null;break;case "axles":p.Axles=null;break;case "axle-load":p.AxleLoads=[];break;case "hazmat":p.Hazardous=null;break;}
        Assert.Null(TruckPhysicalRequest.From(p));
    }
    [Theory][InlineData("http://external.invalid")][InlineData("https://user:secret@external.invalid")][InlineData("https://external.invalid?token=secret")][InlineData("file:///tmp/route")]
    public async Task InvalidEndpointConfigurationMakesNoRequest(string url)
    { var result=await new ValhallaExperimentalRouter(Mock.Of<IHttpClientFactory>(),Config(url:url)).RouteAsync(Request);Assert.Equal(ProviderFailure.InvalidConfiguration,result.Failure); }
    [Fact]
    public async Task DisabledByDefaultMakesNoRequest()
    { var router=new ValhallaExperimentalRouter(Mock.Of<IHttpClientFactory>(),new ConfigurationBuilder().Build());Assert.False(router.Enabled);Assert.Equal(ProviderFailure.Disabled,(await router.RouteAsync(Request)).Failure); }
    [Theory][InlineData(429,ProviderFailure.RateLimited)][InlineData(503,ProviderFailure.HttpError)][InlineData(302,ProviderFailure.HttpError)]
    public async Task HttpFailuresNeverCreateFallbackGeometry(int code,ProviderFailure expected)
    {
        var calls=0;using var handler=new Handler((_,_)=>{calls++;var response=new HttpResponseMessage((HttpStatusCode)code);response.Headers.TryAddWithoutValidation("Retry-After","120");return Task.FromResult(response);});
        var result=await new ValhallaExperimentalRouter(Factory(handler),Config()).RouteAsync(Request);Assert.Equal(expected,result.Failure);Assert.Empty(result.Geometry);Assert.Equal(1,calls);
        if(code==429)Assert.Equal(120,result.RetryAfterSeconds);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task NetworkFailureAndTimeoutHaveDistinctOutcomes(bool timeout)
    {
        using var handler=new Handler((_,_)=>throw (timeout?new TaskCanceledException():new HttpRequestException()));
        Assert.Equal(timeout?ProviderFailure.Timeout:ProviderFailure.Network,(await new ValhallaExperimentalRouter(Factory(handler),Config()).RouteAsync(Request)).Failure);
    }
    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfRecordingProviderOutage()
    {
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();using var handler=new Handler((_,ct)=>Task.FromCanceled<HttpResponseMessage>(ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ValhallaExperimentalRouter(Factory(handler),Config()).RouteAsync(Request,cancelled.Token));
    }
    [Theory][InlineData("{}")][InlineData("not-json")][InlineData("{\"trip\":{\"status\":0,\"units\":\"miles\",\"legs\":[]}}")]
    public void IncompleteResponseCannotDeliverPartialGeometry(string body)
    {var reply=ValhallaExperimentalRouter.Parse(Encoding.UTF8.GetBytes(body));Assert.Equal(ProviderFailure.InvalidResponse,reply.Failure);Assert.Empty(reply.Geometry);}
    [Fact]
    public void NoCoverageIsExplicitAndNoWarningsIsNotVerifiedCoverage()
    {
        Assert.Equal(ProviderFailure.NoCoverage,ValhallaExperimentalRouter.Parse("{\"error_code\":171}"u8.ToArray()).Failure);
        var reply=ValhallaExperimentalRouter.Parse(Response);Assert.Equal(ProviderFailure.None,reply.Failure);
        var evaluation=new RestrictionEngine().Evaluate(RouteTestData.Profile(),reply.ToPlan(),reply.Evidence(),DateTimeOffset.UtcNow);
        Assert.Equal(RouteConfidence.Estimada,evaluation.Confidence);Assert.False(evaluation.NavigationAllowed);
    }
    [Theory][InlineData("provider")][InlineData("graph")][InlineData("segment")][InlineData("way")][InlineData("direction")][InlineData("topology")][InlineData("space")][InlineData("expired")][InlineData("missing")]
    public void NearbyGeometryOrMatchingEdgeOrdinalCannotBindIncompatibleRoadEvidence(string change)
    {
        var road=new RoadReference("Valhalla","graph-a","edge-42","osm-way-5",true,"ordered-node-digest","coverage-digest");
        var other=change switch{"provider"=>road with{Provider="OSRM"},"graph"=>road with{GraphVersion="graph-b"},"segment"=>road with{SegmentId="edge-43"},"way"=>road with{WayId="osm-way-6"},"direction"=>road with{Forward=false},"topology"=>road with{TopologyHash="other"},"space"=>road with{SpatialCoverageHash="other"},"missing"=>road with{GraphVersion=""},_=>road};
        var at=DateTimeOffset.UtcNow;var evidence=new RoadRestrictionEvidence(other,"official-source","v1",at.AddDays(-1),change=="expired"?at.AddMinutes(-1):at.AddDays(1));
        Assert.Equal(RestrictionOutcome.Desconhecido,RoadEvidenceAssociation.Check(road,evidence,at));
        Assert.Equal(RestrictionOutcome.Compativel,RoadEvidenceAssociation.Check(road,new(road,"official-source","v1",at.AddMinutes(-1),at.AddMinutes(1)),at));
    }
    [Fact]
    public void PublicInventoryIsReproducibleWithoutConvertingMissingTagsIntoUnrestrictedRoads()
    {
        var inventory=BrazilCoverageInventory.Read();Assert.Equal(49,inventory.Ways);Assert.Equal(10,inventory.Counts.Count);
        Assert.All(inventory.Counts,c=>Assert.Equal(inventory.Ways,c.TaggedWays+c.UnknownWays));
        Assert.Equal(2,inventory.Counts.Single(c=>c.Kind==RestrictionKind.Height).TaggedWays);
        Assert.Equal(47,inventory.Counts.Single(c=>c.Kind==RestrictionKind.Height).UnknownWays);
        Assert.Equal(49,inventory.Counts.Single(c=>c.Kind==RestrictionKind.GrossWeight).UnknownWays);
        Assert.Equal(10,inventory.Counts.Single(c=>c.Kind==RestrictionKind.Access).TaggedWays);
        Assert.Equal(10,inventory.Counts.Single(c=>c.Kind==RestrictionKind.Time).TaggedWays);
    }
    [Theory][InlineData("truncated")][InlineData("invalid-coordinate")][InlineData("disconnected-legs")][InlineData("warning")]
    public void CorruptGeometryDisconnectedLegsAndProviderWarningsAreNotAccepted(string change)
    {
        var doc=System.Text.Json.Nodes.JsonNode.Parse(Response)!;var trip=doc["trip"]!;
        if(change=="truncated")trip["legs"]![0]!["shape"]="_";
        if(change=="invalid-coordinate")trip["legs"]![0]!["shape"]="~~~~~~~~~";
        if(change=="disconnected-legs")((System.Text.Json.Nodes.JsonArray)trip["legs"]!).Add(trip["legs"]![0]!.DeepClone());
        if(change=="warning")doc["warnings"]=System.Text.Json.Nodes.JsonNode.Parse("[{\"code\":103,\"description\":\"clamped physical parameter\"}]");
        var reply=ValhallaExperimentalRouter.Parse(Encoding.UTF8.GetBytes(doc.ToJsonString()));Assert.Equal(ProviderFailure.InvalidResponse,reply.Failure);Assert.Empty(reply.Geometry);
    }
    [Theory][InlineData(1,5)][InlineData(21,5)][InlineData(6,41)]
    public async Task ValuesOutsideProviderDomainAreRejectedInsteadOfSilentlyClamped(int axles,decimal axleTonnes)
    {
        var request=Request with{Vehicle=Request.Vehicle with{Axles=axles,MaximumAxleTonnes=axleTonnes}};
        Assert.Equal(ProviderFailure.MissingVehicleData,(await new ValhallaExperimentalRouter(Mock.Of<IHttpClientFactory>(),Config()).RouteAsync(request)).Failure);
    }
    [Fact]
    public async Task OversizedResponseCannotExhaustUnboundedMemory()
    {
        using var handler=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[2*1024*1024+1])}));
        var result=await new ValhallaExperimentalRouter(Factory(handler),Config()).RouteAsync(Request);Assert.Equal(ProviderFailure.InvalidResponse,result.Failure);Assert.Empty(result.Geometry);
    }
    [Fact]
    public async Task RateLimitIsSharedAcrossRequestsToTheSameConfiguredEndpoint()
    {
        int calls=0;using var handler=new Handler((_,_)=>{calls++;var r=new HttpResponseMessage(HttpStatusCode.TooManyRequests);r.Headers.TryAddWithoutValidation("Retry-After","120");return Task.FromResult(r);});
        using var cache=new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var first=new ValhallaExperimentalRouter(Factory(handler),Config(),cache);var second=new ValhallaExperimentalRouter(Factory(handler),Config(),cache);
        Assert.Equal(ProviderFailure.RateLimited,(await first.RouteAsync(Request)).Failure);
        Assert.Equal(ProviderFailure.RateLimited,(await second.RouteAsync(Request)).Failure);Assert.Equal(1,calls);
    }
    [Fact]
    public async Task StandardBadRequestNoRouteCodeIsClassifiedWithoutInventingGeometry()
    {
        using var handler=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("{\"error_code\":442,\"error\":\"No path\"}")}));
        var result=await new ValhallaExperimentalRouter(Factory(handler),Config()).RouteAsync(Request);Assert.Equal(ProviderFailure.NoCoverage,result.Failure);Assert.Empty(result.Geometry);
    }
}
