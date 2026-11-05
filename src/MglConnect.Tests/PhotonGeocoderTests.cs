using System.Text;
using System.Net;
using Microsoft.Extensions.Configuration;
using Moq;
using MglConnect.Web.Services;
using Xunit;
namespace MglConnect.Tests;
public sealed class PhotonGeocoderTests
{
    [Fact]public void SuggestionsPreserveAmbiguityAndNeverClaimAnExactAddress() {
        var json="""{"features":[{"geometry":{"type":"Point","coordinates":[-46.65,-23.56]},"properties":{"name":"Public museum","city":"São Paulo"}},{"geometry":{"type":"Point","coordinates":[-46.654,-23.563]},"properties":{"name":"Nearby station"}}]}""";
        var reply=PhotonGeocoder.Parse(Encoding.UTF8.GetBytes(json));Assert.Equal("Available",reply.State);Assert.Equal(2,reply.Suggestions.Count);Assert.All(reply.Suggestions,s=>Assert.Contains("Aproximada",s.Precision));Assert.Equal(-23.56,reply.Suggestions[0].Latitude);
    }
    [Theory][InlineData("{}")][InlineData("{\"features\":[{\"geometry\":{\"type\":\"LineString\",\"coordinates\":[-46,-23]}}]}")]
    public void MalformedOrNonPointGeometryCannotBecomeAConfirmedPlace(string json){Assert.Empty(PhotonGeocoder.Parse(Encoding.UTF8.GetBytes(json)).Suggestions);}
    [Fact]public async Task TimeoutIsVisibleAndCallerCancellationPropagates() {
        var factory=new Mock<IHttpClientFactory>();factory.Setup(f=>f.CreateClient("Photon")).Returns(()=>new HttpClient(new Blocking(),false));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Geocoding:PhotonEnabled","true"},{"Geocoding:PhotonBaseUrl","https://fixture.invalid"},{"Geocoding:TimeoutSeconds","1"}}).Build();
        var adapter=new PhotonGeocoder(factory.Object,config);Assert.Equal("Timeout",(await adapter.SearchAsync("Public test landmark")).State);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>adapter.SearchAsync("Public test landmark",cancelled.Token));
    }
    private sealed class Blocking:HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){await Task.Delay(Timeout.Infinite,ct);return new(HttpStatusCode.OK);}
    }
}
