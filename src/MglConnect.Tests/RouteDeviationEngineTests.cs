using Microsoft.Extensions.Logging;
using MglConnect.Web.Services;
using Moq;
using Xunit;

namespace MglConnect.Tests;

public class RouteDeviationEngineTests
{
    [Fact]
    public void InaccurateGpsFixDoesNotIncrementOffRouteFrames()
    {
        var engine = CreateEngine();
        RegisterRoute(engine);

        var result = engine.EvaluatePosition(
            "MGL-00000001BR", 0.001, 0.002, 20, 90, accuracyMeters: 80);

        Assert.False(result.GpsDataAccepted);
        Assert.False(result.NeedsReroute);
        Assert.Equal(0, result.OffRouteFramesCount);
        Assert.Contains("Precisão GPS insuficiente", result.Motivo);
    }

    [Fact]
    public void ConfirmedDeviationRequiresThreeAccurateConsecutiveFixes()
    {
        var engine = CreateEngine();
        RegisterRoute(engine);

        var first = engine.EvaluatePosition("MGL-00000001BR", 0.001, 0.002, 20, 90, 5);
        var second = engine.EvaluatePosition("MGL-00000001BR", 0.001, 0.0022, 20, 90, 5);
        var third = engine.EvaluatePosition("MGL-00000001BR", 0.001, 0.0024, 20, 90, 5);

        Assert.True(first.GpsDataAccepted);
        Assert.False(first.NeedsReroute);
        Assert.False(second.NeedsReroute);
        Assert.True(third.IsOffRoute);
        Assert.True(third.NeedsReroute);
        Assert.Equal(RouteDeviationEngine.ConsecutiveFramesThreshold, third.OffRouteFramesCount);
    }

    [Fact]
    public void ImplausibleGpsJumpIsRejectedAndDoesNotReplaceLastAcceptedPosition()
    {
        var engine = CreateEngine();
        RegisterRoute(engine);
        engine.EvaluatePosition("MGL-00000001BR", 0, 0.002, 10, 90, 5);

        var result = engine.EvaluatePosition("MGL-00000001BR", 1, 1, 10, 90, 5);
        var state = engine.GetState("MGL-00000001BR");

        Assert.False(result.GpsDataAccepted);
        Assert.Contains("Salto GPS", result.Motivo);
        Assert.Equal(0, state!.LastKnownLatitude);
        Assert.Equal(0.002, state.LastKnownLongitude);
    }

    private static RouteDeviationEngine CreateEngine() =>
        new(Mock.Of<ILogger<RouteDeviationEngine>>());

    private static void RegisterRoute(RouteDeviationEngine engine) =>
        engine.RegisterActiveRoute(
            "MGL-00000001BR",
            1,
            "Leve",
            new List<RouteCoordinate>
            {
                new() { Latitude = 0, Longitude = 0 },
                new() { Latitude = 0, Longitude = 0.01 }
            },
            0,
            0.01);
}
