using System.Text.Json;
using MglConnect.Web.Services;
using MglConnect.Web.Services.RouteTrust;
using Xunit;
namespace MglConnect.Tests;

internal sealed class RouteTestClock:TimeProvider { public override DateTimeOffset GetUtcNow()=>RouteTestData.At; }
internal sealed class TestRouteSource(RouteEvidence evidence):IRouteEvidenceSource
{
    public RouteEvidence Data {get;set;}=evidence;
    public RouteEvidence Read()=>Data;
}
internal static class RouteTestData
{
    public static readonly DateTimeOffset At=new(2026,10,29,12,0,0,TimeSpan.Zero);
    public static OperationalTruckProfile Profile()=>new(){TripId=1,Height=new(4.2m,"m",FactOrigin.Declarado,"fixture"),Width=new(2.6m,"m",FactOrigin.Declarado,"fixture"),Length=new(18.6m,"m",FactOrigin.Declarado,"fixture"),Tare=new(10000,"kg",FactOrigin.Declarado,"fixture"),Payload=new(20000,"kg",FactOrigin.Declarado,"fixture"),Pbt=new(30000,"kg",FactOrigin.Verificado,"balança sintética"),Axles=6,AxleLoads=Enumerable.Range(0,6).Select(_=>new Measure(5000,"kg",FactOrigin.Declarado,"fixture")).ToList(),Category="caminhao",Composition="simples",Cargo="seca",Hazardous=false,PermitsDeclared=true,Permits=["PERMIT-FIXTURE"]};
    public static RouteEvidence Evidence()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"MglConnect.sln")))directory=directory.Parent;
        return JsonSerializer.Deserialize<RouteEvidence>(File.ReadAllText(Path.Combine(directory!.FullName,"src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json")))!;
    }
    public static RouteEvidence Complete(bool synthetic=true)
    {
        var e=Evidence();e.Synthetic=synthetic;e.GeographicVersion="fixture-v1";
        var hash=RouteFingerprint.Geometry(e.RecordedPlan!.TrajetoCoordenadas);int last=e.RecordedPlan.TrajetoCoordenadas.Count-2;
        foreach(var kind in Enum.GetValues<RestrictionKind>())
        {
            decimal? maximum=kind switch{RestrictionKind.Height=>4.5m,RestrictionKind.Width=>3m,RestrictionKind.Length=>20m,RestrictionKind.GrossWeight=>40000m,RestrictionKind.AxleLoad=>6000m,RestrictionKind.AxleCount=>7m,_=>null};
            string? unit=kind switch{RestrictionKind.Height or RestrictionKind.Width or RestrictionKind.Length=>"m",RestrictionKind.GrossWeight or RestrictionKind.AxleLoad=>"kg",RestrictionKind.AxleCount=>"eixos",_=>null};
            e.Rules.Add(new("fixture-"+kind,kind,"fixture://known-restrictions","v1",e.Territory!,hash,0,last,At.AddDays(-10),At.AddDays(10),Maximum:maximum,Unit:unit,
                Allowed:kind==RestrictionKind.Category?["caminhao"]:kind==RestrictionKind.Hazardous?["nao-perigosa"]:null,RequiredPermit:kind==RestrictionKind.Access?"PERMIT-FIXTURE":null,ExplicitlyUnrestricted:kind==RestrictionKind.Time));
        }
        return e;
    }
}
public sealed class RouteRestrictionTests
{
    [Fact]
    public void CompleteSyntheticEvidenceValidatesOnlyItsScopeAndNeverGrantsRealNavigationOrHomologation()
    {
        var source=RouteTestData.Complete();var r=new RestrictionEngine().Evaluate(RouteTestData.Profile(),source.RecordedPlan!,source,RouteTestData.At);
        Assert.Equal(RouteConfidence.Validada,r.Confidence);Assert.False(r.NavigationAllowed);Assert.True(r.Synthetic);Assert.All(r.Checks,c=>Assert.Equal(RestrictionOutcome.Compativel,c.Outcome));Assert.NotEqual(RouteConfidence.Homologada,r.Confidence);
        foreach(var point in source.RecordedPlan!.TrajetoCoordenadas)point.Nome="Display-only metadata";
        Assert.Equal(r.GeometrySha256,new RestrictionEngine().Evaluate(RouteTestData.Profile(),source.RecordedPlan,source,RouteTestData.At).GeometrySha256);
    }
    [Theory][InlineData(RestrictionKind.Height)][InlineData(RestrictionKind.Width)][InlineData(RestrictionKind.Length)][InlineData(RestrictionKind.GrossWeight)][InlineData(RestrictionKind.AxleCount)][InlineData(RestrictionKind.AxleLoad)][InlineData(RestrictionKind.Category)][InlineData(RestrictionKind.Access)][InlineData(RestrictionKind.Hazardous)]
    public void KnownIncompatibilityBlocksTheRoute(RestrictionKind kind)
    {
        var source=RouteTestData.Complete();var rule=source.Rules.Single(r=>r.Kind==kind);
        source.Rules.Remove(rule);source.Rules.Add(kind switch{
            RestrictionKind.Category=>rule with{Allowed=["utilitario"]},RestrictionKind.Access=>rule with{RequiredPermit="OTHER"},
            RestrictionKind.Hazardous=>rule with{Allowed=["classe-3"]},_=>rule with{Maximum=1}});
        var r=new RestrictionEngine().Evaluate(RouteTestData.Profile(),source.RecordedPlan!,source,RouteTestData.At);
        Assert.Contains(r.Checks,c=>c.Kind==kind&&c.Outcome==RestrictionOutcome.Incompativel);Assert.False(r.NavigationAllowed);Assert.NotEqual(RouteConfidence.Validada,r.Confidence);
    }
    [Theory][InlineData("missing")][InlineData("expired")][InlineData("future")][InlineData("territory")][InlineData("geometry-hash")][InlineData("nearby")][InlineData("conflict")][InlineData("unit")][InlineData("partial")]
    public void MissingUnrelatedOrConflictingInformationIsUnknownNeverApproval(string condition)
    {
        var s=RouteTestData.Complete();var p=RouteTestData.Profile();var rule=s.Rules.Single(r=>r.Kind==RestrictionKind.Height);s.Rules.Remove(rule);
        if(condition=="missing")p.Height=null;
        if(condition=="conflict")s.Rules.Add(rule with{Id="other-source",Source="fixture://different",Maximum=5});
        s.Rules.Add(condition switch{
            "expired"=>rule with{ValidUntil=RouteTestData.At.AddSeconds(-1)},"future"=>rule with{ValidFrom=RouteTestData.At.AddSeconds(1)},
            "territory"=>rule with{Territory="other"},"geometry-hash"=>rule with{GeometrySha256="OTHER"},"nearby"=>rule with{FirstEdge=-1},
            "unit"=>rule with{Unit="cm"},"partial"=>rule with{LastEdge=1},_=>rule});
        var r=new RestrictionEngine().Evaluate(p,s.RecordedPlan!,s,RouteTestData.At);
        Assert.Contains(r.Checks,c=>c.Kind==RestrictionKind.Height&&c.Outcome==RestrictionOutcome.Desconhecido);Assert.False(r.NavigationAllowed);Assert.NotEqual(RouteConfidence.Validada,r.Confidence);
    }
    [Fact]
    public void GenericGeometryOrAnEmptyVehicleCannotBecomeValidatedThroughBooleanApproval()
    {
        var s=RouteTestData.Evidence();s.RecordedPlan!.RotaAprovada=true;s.RecordedPlan.RotaHomologadaParaVeiculo=true;
        var r=new RestrictionEngine().Evaluate(new(),s.RecordedPlan,s,RouteTestData.At);
        Assert.Equal(RouteConfidence.Estimada,r.Confidence);Assert.False(r.NavigationAllowed);Assert.NotEmpty(r.MissingData);
    }
    [Theory][InlineData(9,true)][InlineData(20,false)]
    public void TimeWindowsUseTheSpecifiedTimeZoneAndNeverCertifyFutureArrival(int hour,bool compatible)
    {
        var s=RouteTestData.Complete(false);var old=s.Rules.Single(r=>r.Kind==RestrictionKind.Time);s.Rules.Remove(old);
        s.Rules.Add(old with{ExplicitlyUnrestricted=false,WindowStartMinute=480,WindowEndMinute=1080,TimeZone="America/Sao_Paulo"});
        var r=new RestrictionEngine().Evaluate(RouteTestData.Profile(),s.RecordedPlan!,s,new DateTimeOffset(2026,10,29,hour+3,0,0,TimeSpan.Zero));
        Assert.Contains(r.Checks,c=>c.Kind==RestrictionKind.Time&&c.Outcome==(compatible?RestrictionOutcome.Compativel:RestrictionOutcome.Incompativel));Assert.False(r.NavigationAllowed);
    }
    [Fact]
    public void InvalidGeometryDoesNotProduceDistanceEtaOrAnInventedConnectingLine()
    {
        var s=RouteTestData.Complete();s.RecordedPlan!.TrajetoCoordenadas[0].Latitude=double.NaN;
        var r=new RestrictionEngine().Evaluate(RouteTestData.Profile(),s.RecordedPlan,s,RouteTestData.At);
        Assert.Equal(RouteConfidence.Indisponivel,r.Confidence);Assert.Empty(r.Geometry);Assert.Null(r.EstimatedDistanceKm);Assert.Null(r.EstimatedMinutes);
    }
    [Fact]
    public void ProfileUnitsMassBalanceAndAxleDataAreValidatedWithoutUsingLegacyWeights()
    {
        var p=RouteTestData.Profile();p.Height=p.Height! with{Unit="cm"};Assert.NotEmpty(p.Validate());p=RouteTestData.Profile();p.Pbt=p.Pbt! with{Value=1000};Assert.NotEmpty(p.Validate());
        p=RouteTestData.Profile();p.Axles=7;Assert.NotEmpty(p.Validate());p=RouteTestData.Profile();p.Pbt=null;Assert.Equal(FactOrigin.Calculado,p.Gross!.Origin);Assert.Equal(30000,p.Gross.Value);
        Assert.Null(new OperationalTruckProfile().Gross);
    }
}
