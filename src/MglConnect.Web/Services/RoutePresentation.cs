using MglConnect.Web.Services.RouteTrust;

namespace MglConnect.Web.Services;

public enum RouteDisplayState { AwaitingParameters, Calculating, Calculated, NoRoute, ProviderUnavailable, GenericUnverifiedTruck, SyntheticDemo, InsufficientVehicleData }

// Transport/presentation contract: missing measurements remain null, never a successful zero route.
public sealed record RoutePresentation(RouteDisplayState State, bool HasGeometry, string Label, string Notice,
    decimal? DistanceKm, double? DurationSeconds, decimal? FuelLitres, decimal? FuelCost,
    decimal? RouteToll, decimal? FixedTollScenario, decimal? ScenarioCost, string Provider, string Profile, string GeometrySource)
{
    public static bool ValidGeometry(TruckRoutePlan plan) => plan.RotaDisponivel && plan.TrajetoCoordenadas is {Count: >=2 and <=20000}
        && plan.TrajetoCoordenadas.All(c=>c!=null && double.IsFinite(c.Latitude)&&Math.Abs(c.Latitude)<=90&&double.IsFinite(c.Longitude)&&Math.Abs(c.Longitude)<=180)
        && plan.TrajetoCoordenadas.Any(c=>c.Latitude!=plan.TrajetoCoordenadas[0].Latitude||c.Longitude!=plan.TrajetoCoordenadas[0].Longitude);
    public static RoutePresentation From(TruckRoutePlan plan, TripCostResult? cost=null, bool requested=true)
    {
        bool valid=ValidGeometry(plan)&&plan.DistanciaKm>0;
        bool synthetic=plan.GeometrySource=="SyntheticCircuit" || plan.DemonstracaoControlada;
        bool heavy=plan.PerfilVeiculo!="Leve";
        var state=plan.IsCalculating?RouteDisplayState.Calculating:!requested?RouteDisplayState.AwaitingParameters:
            !valid ? plan.ProviderFailure is ProviderFailure.Disabled or ProviderFailure.InvalidConfiguration or ProviderFailure.HttpError or ProviderFailure.Timeout or ProviderFailure.Network or ProviderFailure.RateLimited or ProviderFailure.InvalidResponse
                ? RouteDisplayState.ProviderUnavailable : plan.ProviderFailure==ProviderFailure.MissingVehicleData?RouteDisplayState.InsufficientVehicleData:RouteDisplayState.NoRoute:
            synthetic?RouteDisplayState.SyntheticDemo:
            plan.Confianca?.MissingData.Count>0?RouteDisplayState.InsufficientVehicleData:
            heavy&&plan.Confianca?.NavigationAllowed!=true?RouteDisplayState.GenericUnverifiedTruck:RouteDisplayState.Calculated;
        var label=state switch {
            RouteDisplayState.AwaitingParameters=>"Informe os parâmetros",RouteDisplayState.Calculating=>"Calculando rota",
            RouteDisplayState.Calculated=>"Rota genérica calculada",RouteDisplayState.NoRoute=>"Sem rota encontrada",
            RouteDisplayState.ProviderUnavailable=>"Provedor indisponível",RouteDisplayState.SyntheticDemo=>"Circuito sintético de demonstração",
            RouteDisplayState.InsufficientVehicleData=>"Dados de veículo insuficientes",_=>"Rota genérica · caminhão não validado" };
        var notice=state==RouteDisplayState.AwaitingParameters?"Escolha uma entrega ou informe origem e destino. Nenhuma consulta foi enviada.":
            valid?synthetic?"Circuito fictício; não representa ruas reais nem autorização para dirigir.":"Geometria viária não comprova restrições de caminhão. Não há dados de trânsito ao vivo.":
            "Sem geometria utilizável: distância, duração e custos dependentes da rota estão indisponíveis.";
        double? seconds=valid && plan.ProviderDurationSeconds is >0 and <=2592000 && double.IsFinite(plan.ProviderDurationSeconds.Value)?plan.ProviderDurationSeconds:null;
        return new(state,valid,label,notice,valid?plan.DistanciaKm:null,seconds,
            valid?cost?.LitrosCombustivel:null,valid?cost?.CustoCombustivel:null,null,
            cost==null?null:FuelCostService.TarifaBasePedagioPorEixo*cost.QtdEixos,valid?cost?.CustoTotalViagem:null,plan.ProviderName,plan.ProviderProfile,plan.GeometrySource);
    }
}
