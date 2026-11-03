using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public class DriverRoutingTests
{
    [Fact]
    public async Task TrafficWithoutProviderIsReportedAsUnavailable()
    {
        var service = new TrafficMatchService(
            new ConfigurationBuilder().Build(),
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<TrafficMatchService>>());

        var result = await service.ObterCondicaoTrafegoAsync(
            -23.55,
            -46.63,
            -23.56,
            -46.64,
            42,
            "Leve");

        Assert.False(result.DadosAoVivoDisponiveis);
        Assert.Equal("Indisponível", result.Condicao);
        Assert.Equal(42, result.TempoEstimadoMinutosComTrafego);
        Assert.Equal(0, result.AtrasoMinutos);
    }

    [Fact]
    public async Task RouteUsesDeliveryAddressAndDoesNotAssumeSantos()
    {
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!.ToString());
            if (request.RequestUri.Host == "maps.googleapis.com")
            {
                return JsonResponse(
                    @"{""status"":""OK"",""results"":[{""geometry"":{""location"":{""lat"":-22.9,""lng"":-43.2}}}]}");
            }

            if (request.RequestUri.Host == "photon.komoot.io")
            {
                return JsonResponse(
                    @"{""features"":[{""geometry"":{""coordinates"":[-46.63,-23.55]}}]}");
            }

            return JsonResponse(
                @"{""code"":""Ok"",""routes"":[{""distance"":12345,""duration"":600,""geometry"":{""coordinates"":[[-46.877658,-23.355797],[-43.2,-22.9]]},""legs"":[{""steps"":[{""name"":""Rua de Origem"",""distance"":6000,""duration"":300,""maneuver"":{""type"":""depart"",""modifier"":""straight"",""location"":[-46.877658,-23.355797]}},{""name"":""Rua de Destino"",""distance"":6345,""duration"":300,""maneuver"":{""type"":""arrive"",""location"":[-43.2,-22.9]}}]}]}]}");
        });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new MglDbContext(options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleMaps:GeocodingApiKey"] = "test-key",
                ["Routing:OsrmBaseUrl"] = "https://router.project-osrm.org"
            })
            .Build();
        var service = new TruckRoutingService(
            context,
            factory.Object,
            cache,
            configuration,
            Mock.Of<ILogger<TruckRoutingService>>(),
            Mock.Of<IRouteDeviationEngine>());

        var route = await service.CalcularRotaPesadaAsync(
            "CD Logístico MGL Cajamar (SP)",
            "Rua de Destino, 10, Rio de Janeiro/RJ, Brasil",
            2.1m,
            2.8m,
            2.0m,
            2,
            "Carga leve",
            "Leve");

        Assert.True(route.RotaDisponivel);
        Assert.Equal(12.345m, route.DistanciaKm);
        Assert.False(route.RotaHomologadaParaVeiculo);
        Assert.Contains("perfil rodoviário genérico", route.AvisoRota, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(requests, request => request.Contains("-43.200000,-22.900000", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, request => request.Contains("-46.3322,-23.9618", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DevelopmentPhotonGeocodingCalculatesAndCachesRoute()
    {
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!.ToString());
            if (request.RequestUri.Host == "photon.komoot.io")
            {
                return JsonResponse(
                    @"{""features"":[{""geometry"":{""coordinates"":[-46.63,-23.55]}}]}");
            }

            return JsonResponse(
                @"{""code"":""Ok"",""routes"":[{""distance"":7200,""duration"":540,""geometry"":{""coordinates"":[[-46.877658,-23.355797],[-46.63,-23.55]]},""legs"":[{""steps"":[]}]}]}");
        });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new MglDbContext(options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Geocoding:PhotonEnabled"] = "true",
                ["Routing:OsrmBaseUrl"] = "https://router.project-osrm.org"
            })
            .Build();
        var service = new TruckRoutingService(
            context,
            factory.Object,
            cache,
            configuration,
            Mock.Of<ILogger<TruckRoutingService>>(),
            Mock.Of<IRouteDeviationEngine>());

        var firstRoute = await service.CalcularRotaPesadaAsync(
            "CD Logístico MGL Cajamar (SP)",
            "Rua de Teste, São Paulo/SP, Brasil",
            2.1m,
            2.8m,
            2.0m,
            2,
            "Carga leve",
            "Leve");
        var cachedRoute = await service.CalcularRotaPesadaAsync(
            "CD Logístico MGL Cajamar (SP)",
            "Rua de Teste, São Paulo/SP, Brasil",
            2.1m,
            2.8m,
            2.0m,
            2,
            "Carga leve",
            "Leve");

        Assert.True(firstRoute.RotaDisponivel);
        Assert.Same(firstRoute, cachedRoute);
        Assert.Equal(2, requests.Count);
        Assert.Contains(requests, request => request.StartsWith("https://photon.komoot.io/api/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RouteIsUnavailableWhenNoGeocoderIsConfigured()
    {
        var factory = new Mock<IHttpClientFactory>();
        var options = new DbContextOptionsBuilder<MglDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new MglDbContext(options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new TruckRoutingService(
            context,
            factory.Object,
            cache,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Geocoding:PhotonEnabled"] = "false"
                })
                .Build(),
            Mock.Of<ILogger<TruckRoutingService>>(),
            Mock.Of<IRouteDeviationEngine>());

        var route = await service.CalcularRotaPesadaAsync(
            "CD Logístico MGL Cajamar (SP)",
            "Endereço sem geocodificação, São Paulo/SP",
            2.1m,
            2.8m,
            2.0m,
            2,
            "Carga leve",
            "Leve");

        Assert.False(route.RotaDisponivel);
        Assert.Empty(route.TrajetoCoordenadas);
        Assert.Contains("Google Geocoding", route.AvisoRota, StringComparison.Ordinal);
        factory.Verify(item => item.CreateClient(It.IsAny<string>()), Times.Never);
    }


    [Theory]
    [InlineData("network",false)][InlineData("timeout",false)][InlineData("json",false)][InlineData("503",false)]
    [InlineData("network",true)][InlineData("timeout",true)][InlineData("json",true)][InlineData("503",true)]
    public async Task ProviderFailureNeverCreatesFabricatedCoordinatesDistanceOrEta(string failure,bool geocoding)
    {
        var handler=new StubHttpMessageHandler(request=>
        {
            if(request.RequestUri!.Host.Contains("google")&&!geocoding)
                return JsonResponse("{\"status\":\"OK\",\"results\":[{\"geometry\":{\"location\":{\"lat\":-23.55,\"lng\":-46.63}}}]}");
            if(failure=="network")throw new HttpRequestException("Synthetic outage");
            if(failure=="timeout")throw new TaskCanceledException("Synthetic timeout");
            return failure=="503"?new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable):JsonResponse("invalid-json");
        });
        var factory=new Mock<IHttpClientFactory>();factory.Setup(f=>f.CreateClient(It.IsAny<string>())).Returns(()=>new HttpClient(handler,false));
        using var db=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var cache=new MemoryCache(new MemoryCacheOptions());
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"GoogleMaps:GeocodingApiKey","SYNTHETIC-ONLY"},{"Routing:OsrmBaseUrl","https://routing.invalid"}}).Build();
        var service=new TruckRoutingService(db,factory.Object,cache,config,Mock.Of<ILogger<TruckRoutingService>>(),Mock.Of<IRouteDeviationEngine>());
        var route=await service.CalcularRotaPesadaAsync("CD MGL Cajamar","Synthetic destination",4.2m,30m,2.6m,6,"Synthetic cargo");
        Assert.False(route.RotaDisponivel);Assert.False(route.RotaHomologadaParaVeiculo);Assert.False(route.RotaAprovada);
        Assert.Empty(route.TrajetoCoordenadas);Assert.Equal(0,route.DistanciaKm);Assert.Equal(0,route.TempoEstimadoMinutos);
    }
    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_response(request));
    }
}
