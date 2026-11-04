using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using MglConnect.Web.Services.RouteTrust;
using Moq;
using Xunit;

namespace MglConnect.Tests;
public sealed class RoutePresentationTests
{
    public static TruckRoutePlan Valid() => new(){RotaDisponivel=true,PerfilVeiculo="Leve",DistanciaKm=1.25m,ProviderDurationSeconds=61,
        ProviderName="OSRM",ProviderProfile="driving",GeometrySource="LocalProviderFixture",TrajetoCoordenadas=[new(){Latitude=-23.55,Longitude=-46.63},new(){Latitude=-23.551,Longitude=-46.632}]};
    [Fact]
    public void UnavailableGeometryNeverClaimsSuccessfulMetricsOrRouteTolls()
    {
        using var db=new MglDbContext(new DbContextOptionsBuilder<MglDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var costs=new FuelCostService(db,Mock.Of<ITruckRoutingService>());
        foreach(var plan in new[]{new TruckRoutePlan(),new(){RotaDisponivel=true,DistanciaKm=100},new(){RotaDisponivel=true,DistanciaKm=10,TrajetoCoordenadas=[new(),new()]}}){
            var result=RoutePresentation.From(plan,costs.CalcularCustoViagem(0,"Pesado",5));
            Assert.False(result.HasGeometry);Assert.Null(result.DistanceKm);Assert.Null(result.DurationSeconds);Assert.Null(result.RouteToll);Assert.Null(result.ScenarioCost);Assert.Null(result.FuelCost);
        }
        var zero=costs.CalcularCustoViagem(0,"Pesado",5);Assert.False(zero.RouteCostAvailable);Assert.Equal(0,zero.CustoTotalViagem);Assert.Equal(0,zero.CustoPedagio);
        var fixedScenario=costs.CalcularCustoViagem(100,"Pesado",5);Assert.Equal(92.50m,fixedScenario.CustoPedagio);Assert.Equal("FixedPerAxleScenario",fixedScenario.TollSource);
    }
    [Fact]
    public void AllEightStatesAreExplicitAndTrustCannotBeInferredFromGeometry()
    {
        Assert.Equal(RouteDisplayState.AwaitingParameters,RoutePresentation.From(new(),requested:false).State);
        Assert.True(Enum.IsDefined(RouteDisplayState.Calculating));
        Assert.Equal(RouteDisplayState.NoRoute,RoutePresentation.From(new()).State);
        Assert.Equal(RouteDisplayState.ProviderUnavailable,RoutePresentation.From(new(){ProviderFailure=ProviderFailure.Timeout}).State);
        Assert.Equal(RouteDisplayState.InsufficientVehicleData,RoutePresentation.From(new(){ProviderFailure=ProviderFailure.MissingVehicleData}).State);
        var plan=Valid();Assert.Equal(RouteDisplayState.Calculated,RoutePresentation.From(plan).State);
        plan.PerfilVeiculo="Pesado";Assert.Equal(RouteDisplayState.GenericUnverifiedTruck,RoutePresentation.From(plan).State);
        plan.DemonstracaoControlada=true;Assert.Equal(RouteDisplayState.SyntheticDemo,RoutePresentation.From(plan).State);
        plan.DemonstracaoControlada=false;plan.Confianca=new(){MissingData=["Altura"]};Assert.Equal(RouteDisplayState.InsufficientVehicleData,RoutePresentation.From(plan).State);
    }
    [Fact]
    public void SyntheticRestrictionEvidenceDoesNotRelabelCapturedRoadGeometryAsAFictionalCircuit()
    {
        var plan=Valid();plan.PerfilVeiculo="Pesado";plan.GeometrySource="HistoricalRoadCapture";plan.Confianca=new(){Synthetic=true};
        var state=RoutePresentation.From(plan);Assert.Equal(RouteDisplayState.GenericUnverifiedTruck,state.State);Assert.Equal("HistoricalRoadCapture",state.GeometrySource);
    }
    [Fact]
    public void RestrictionAssessmentPreservesProviderMetricsWithoutGrantingTrust()
    {
        var plan=Valid();plan.ProviderResponseSha256=new string('A',64);plan.GeometrySourceReference="https://fixture.invalid/route";
        var assessment=new RestrictionEngine().Evaluate(new(),plan,new(){Provider="OSRM",RoutingProfile="driving"},DateTimeOffset.UtcNow);
        var restored=System.Text.Json.JsonSerializer.Deserialize<RouteTrustResult>(System.Text.Json.JsonSerializer.Serialize(assessment))!;
        Assert.Equal(61,restored.ProviderDurationSeconds);Assert.Equal(plan.ProviderResponseSha256,restored.ProviderResponseSha256);Assert.Equal(plan.GeometrySourceReference,restored.GeometrySourceReference);Assert.False(restored.NavigationAllowed);
    }
    [Fact]
    public async Task DeliveryCostDoesNotInventTheLegacy147Kilometres()
    {
        await using var f=await SecurityHttpFixture.CreateAsync(startServer:false);
        await using var db=new MglDbContext(f.Options);var router=new Mock<ITruckRoutingService>();router.Setup(r=>r.CalcularRotaParaEntregaAsync(It.IsAny<int>(),It.IsAny<string>())).ReturnsAsync(new TruckRoutePlan());
        var cost=await new FuelCostService(db,router.Object).CalcularCustoParaEntregaAsync(f.Deliveries[0].Id);
        Assert.False(cost.RouteCostAvailable);Assert.Equal(0,cost.DistanciaKm);Assert.Equal(0,cost.CustoTotalViagem);
    }
}
