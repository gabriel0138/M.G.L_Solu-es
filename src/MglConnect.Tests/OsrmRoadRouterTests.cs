using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MglConnect.Web.Services;
using MglConnect.Web.Services.RouteTrust;
using Moq;
using Xunit;
namespace MglConnect.Tests;
public sealed class OsrmRoadRouterTests
{
    internal const string ValidResponse="""{"code":"Ok","routes":[{"distance":1250,"duration":61.2,"geometry":{"type":"LineString","coordinates":[[-46.63,-23.55],[-46.632,-23.551]]},"legs":[]}]}""";
    [Fact]public void GeometryOrderAndProviderDurationArePreserved()
    {
        var result=OsrmRoadRouter.Parse(Encoding.UTF8.GetBytes(ValidResponse));Assert.Equal(ProviderFailure.None,result.Failure);Assert.Equal(1.25m,result.DistanceKm);Assert.Equal(61.2,result.DurationSeconds);
        Assert.Equal(-23.55,result.Geometry[0].Latitude);Assert.Equal(-46.63,result.Geometry[0].Longitude);Assert.Equal(2,result.EstimatedMinutes);Assert.NotNull(result.ResponseSha256);
        var plan=result.ToPlan();Assert.False(plan.RotaHomologadaParaVeiculo);Assert.Equal(61.2,RoutePresentation.From(plan).DurationSeconds);
    }
    [Theory]
    [InlineData("{}")][InlineData("{\"code\":\"Ok\",\"routes\":[]}")][InlineData("not json")]
    public void EmptyOrMalformedResponsesDoNotProduceGeometry(string body){var result=OsrmRoadRouter.Parse(Encoding.UTF8.GetBytes(body));Assert.NotEqual(ProviderFailure.None,result.Failure);Assert.Empty(result.Geometry);Assert.Null(result.DistanceKm);}
    [Theory][InlineData("\"distance\":1250","\"distance\":0")][InlineData("\"duration\":61.2","\"duration\":-1")][InlineData("LineString","Point")][InlineData("-23.551","91")][InlineData("[-46.632,-23.551]","[-46.63,-23.55]")]
    public void InvalidMetricsTypeRangesAndDegenerateGeometryAreRejected(string from,string to){Assert.Equal(ProviderFailure.InvalidResponse,OsrmRoadRouter.Parse(Encoding.UTF8.GetBytes(ValidResponse.Replace(from,to))).Failure);}
    [Theory][InlineData("-46.63,-23.55;-46.632,-23.551",true)][InlineData("-46.63,-23.55;-46.631,-23.55;-46.632,-23.551",true)][InlineData("NaN,0;1,2",false)][InlineData("1,91;0,0",false)][InlineData("1,2",false)]
    public void WaypointsUseLongitudeLatitudeAndFiniteBounds(string input,bool expected){Assert.Equal(expected,OsrmRoadRouter.TryLocations(input,out _));}
    [Fact]public async Task RateLimitIsNotRetriedAndCallerCancellationPropagates()
    {
        int calls=0;var handler=new Handler((_,_)=>{calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests){Headers={RetryAfter=new(TimeSpan.FromSeconds(30))}});});
        using var cache=new MemoryCache(new MemoryCacheOptions());var router=Router(handler,cache);var points=new[]{new RouteLocation(-23.55,-46.63),new RouteLocation(-23.551,-46.632)};
        Assert.Equal(ProviderFailure.RateLimited,(await router.RouteAsync(points)).Failure);Assert.Equal(ProviderFailure.RateLimited,(await router.RouteAsync(points)).Failure);Assert.Equal(1,calls);
        var blocking=Router(new Handler(async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return new(HttpStatusCode.OK);}));using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>blocking.RouteAsync(points,cancelled.Token));
    }
    [Fact]public async Task HttpFailureAndProviderTimeoutHaveDistinctCodes()
    {
        var points=new[]{new RouteLocation(-23.55,-46.63),new RouteLocation(-23.551,-46.632)};
        Assert.Equal(ProviderFailure.HttpError,(await Router(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))).RouteAsync(points)).Failure);
        Assert.Equal(ProviderFailure.Timeout,(await Router(new Handler(async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return new(HttpStatusCode.OK);})).RouteAsync(points)).Failure);
    }
    [Fact]public async Task HistoricalRoadCaptureDoesNotInventProviderTimeOrPromoteSyntheticRestrictions()
    {
        var source=new DirectoryInfo(AppContext.BaseDirectory);while(source!=null&&!File.Exists(Path.Combine(source.FullName,"MglConnect.sln")))source=source.Parent;
        Assert.NotNull(source);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"DemoData:Enabled","true"},{"Routing:HistoricalGeometryPath",Path.Combine(source!.FullName,"src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json")}}).Build();
        var env=new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();env.SetupGet(e=>e.EnvironmentName).Returns("Development");
        var router=new OsrmRoadRouter(Mock.Of<IHttpClientFactory>(),config,environment:env.Object);
        var reply=await router.RouteAsync([new(-23.5614,-46.6559),new(-23.5505,-46.6333)]);
        Assert.Equal(ProviderFailure.None,reply.Failure);Assert.Equal(189,reply.Geometry.Count);Assert.Equal(4.962m,reply.DistanceKm);Assert.Null(reply.DurationSeconds);Assert.Null(reply.EstimatedMinutes);
        Assert.Equal("HistoricalRoadCapture",reply.GeometrySource);Assert.NotNull(reply.ResponseSha256);Assert.False(reply.ToPlan().RotaHomologadaParaVeiculo);Assert.Null(reply.ToPlan().Confianca);
        env.SetupGet(e=>e.EnvironmentName).Returns("Production");
        Assert.Equal(ProviderFailure.InvalidConfiguration,(await router.RouteAsync([new(-23.5614,-46.6559),new(-23.5505,-46.6333)])).Failure);
    }
    [Fact]public async Task NoRouteInHttp400IsNotConfusedWithProviderOutage()
    {
        var router=Router(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("{\"code\":\"NoRoute\"}")})));
        Assert.Equal(ProviderFailure.NoCoverage,(await router.RouteAsync([new(-23.55,-46.63),new(-23.551,-46.632)])).Failure);
    }
    [Fact]public async Task IntermediateWaypointsAreSentInOrderWithoutPrivateRequestLogging()
    {
        Uri? requested=null;
        var router=Router(new Handler((request,_)=>{requested=request.RequestUri;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(ValidResponse)});}));
        var result=await router.RouteAsync([new(-23.55,-46.63),new(-23.5505,-46.631),new(-23.551,-46.632)]);
        Assert.Equal(ProviderFailure.None,result.Failure);Assert.NotNull(requested);
        Assert.Contains("-46.630000,-23.550000;-46.631000,-23.550500;-46.632000,-23.551000",requested!.OriginalString);
        Assert.Contains("geometries=geojson",requested.Query);
    }
    private static OsrmRoadRouter Router(HttpMessageHandler handler,IMemoryCache? cache=null){var factory=new Mock<IHttpClientFactory>();factory.Setup(f=>f.CreateClient(It.IsAny<string>())).Returns(()=>new HttpClient(handler,false));return new(factory.Object,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Routing:OsrmBaseUrl","https://fixture.invalid"},{"Routing:TimeoutSeconds","1"}}).Build(),cache);}
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> callback):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>callback(request,ct);}
}
