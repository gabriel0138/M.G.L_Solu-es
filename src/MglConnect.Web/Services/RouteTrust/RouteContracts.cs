using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using MglConnect.Web.Models;
namespace MglConnect.Web.Services.RouteTrust;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FactOrigin { Declarado, Calculado, Verificado }
public sealed record Measure(decimal Value,string Unit,FactOrigin Origin,string Reference);
public sealed class OperationalTruckProfile
{
    public string? ReadError {get;set;}
    public int? TripId {get;set;}
    public int? VehicleId {get;set;}
    public Measure? Height {get;set;}
    public Measure? Width {get;set;}
    public Measure? Length {get;set;}
    public Measure? Tare {get;set;}
    public Measure? Pbt {get;set;}
    public Measure? Pbtc {get;set;}
    public Measure? Payload {get;set;}
    public int? Axles {get;set;}
    public List<Measure> AxleLoads {get;set;}=[];
    public string? Category {get;set;}
    public string? Composition {get;set;}
    public string? Cargo {get;set;}
    public bool? Hazardous {get;set;}
    public string? HazardClass {get;set;}
    public bool? PermitsDeclared {get;set;}
    public List<string> Permits {get;set;}=[];
    public string? AccessRequirements {get;set;}
    public Measure? Gross=>Pbtc??Pbt??(Tare!=null&&Payload!=null?new(Tare.Value+Payload.Value,"kg",FactOrigin.Calculado,"tara + carga efetiva declaradas"):null);
    public List<string> Missing()=>new[]{(Height==null,"Altura total"),(Width==null,"Largura"),(Length==null,"Comprimento total"),(Gross==null,"Massa bruta efetiva"),(Axles==null,"Eixos"),(string.IsNullOrWhiteSpace(Category),"Categoria"),(string.IsNullOrWhiteSpace(Composition),"Composição"),(string.IsNullOrWhiteSpace(Cargo),"Tipo de carga"),(Hazardous==null,"Classificação perigosa/não perigosa")}.Where(p=>p.Item1).Select(p=>p.Item2).ToList();
    public List<string> Validate()
    {
        var errors=new List<string>();if(ReadError!=null)errors.Add(ReadError);
        foreach(var pair in new[]{("altura",Height,"m"),("largura",Width,"m"),("comprimento",Length,"m"),("tara",Tare,"kg"),("PBT",Pbt,"kg"),("PBTC",Pbtc,"kg"),("carga",Payload,"kg")})
            if(pair.Item2 is {} value&&((value.Value<0||value.Value==0&&pair.Item1!="carga")||value.Value>1000000||value.Unit!=pair.Item3||string.IsNullOrWhiteSpace(value.Reference)||value.Reference.Length>200||!Enum.IsDefined(value.Origin)))errors.Add($"Dado/unidade inválido: {pair.Item1}.");
        if(Axles is <1 or >30)errors.Add("Número de eixos inválido.");
        if(AxleLoads.Count>30||AxleLoads.Any(m=>m.Value<=0||m.Unit!="kg"||string.IsNullOrWhiteSpace(m.Reference))||AxleLoads.Count>0&&AxleLoads.Count!=Axles)errors.Add("Informe uma carga em kg para cada eixo conhecido.");
        if(Pbt!=null&&Pbtc!=null&&Pbtc.Value<Pbt.Value)errors.Add("PBTC não pode ser menor que PBT informado.");
        if(Tare!=null&&Payload!=null&&Gross!=null&&Math.Abs(Gross.Value-Tare.Value-Payload.Value)>.01m)errors.Add("Massa bruta efetiva diverge de tara + carga; reconcilie os dados.");
        if(AxleLoads.Count>0&&Gross!=null&&Math.Abs(AxleLoads.Sum(m=>m.Value)-Gross.Value)>.01m)errors.Add("Soma das cargas por eixo diverge da massa bruta informada.");
        if(Hazardous==true&&string.IsNullOrWhiteSpace(HazardClass))errors.Add("Informe a classificação da carga perigosa.");
        if(Permits.Count>30||Permits.Any(p=>string.IsNullOrWhiteSpace(p)||p.Length>100)||new[]{Category,Composition,Cargo,HazardClass,AccessRequirements}.Any(s=>s?.Length>200))errors.Add("Descrição operacional excede o limite.");
        return errors;
    }
    public static OperationalTruckProfile Read(string? json)
    {
        if(string.IsNullOrWhiteSpace(json))return new();
        try{var p=JsonSerializer.Deserialize<OperationalTruckProfile>(json)??throw new JsonException();if(p.AxleLoads==null||p.Permits==null)throw new JsonException();return p;}
        catch(JsonException){return new(){ReadError="Perfil armazenado inválido; declare novamente os dados conhecidos."};}
    }
}
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestrictionKind { Height, GrossWeight, Width, Length, AxleLoad, AxleCount, Category, Access, Time, Hazardous }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestrictionOutcome { Compativel, Incompativel, Desconhecido }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RouteConfidence { Indisponivel, Estimada, Validada, Homologada }
public sealed record RestrictionRule(string Id,RestrictionKind Kind,string Source,string Version,string Territory,
    string GeometrySha256,int FirstEdge,int LastEdge,DateTimeOffset ValidFrom,DateTimeOffset ValidUntil,
    decimal? Maximum=null,string? Unit=null,string[]? Allowed=null,string? RequiredPermit=null,
    int? WindowStartMinute=null,int? WindowEndMinute=null,string? TimeZone=null,bool ExplicitlyUnrestricted=false);
public sealed class RouteEvidence
{
    public string Provider {get;set;}="OSRM/configurado";
    public string RoutingProfile {get;set;}="driving";
    public string? GeographicVersion {get;set;}
    public string? SourceReference {get;set;}
    public string? Territory {get;set;}
    public bool Synthetic {get;set;}
    public List<RestrictionRule> Rules {get;set;}=[];
    // Only an explicit Development fixture may supply a captured provider geometry.
    public TruckRoutePlan? RecordedPlan {get;set;}
}
public sealed record RestrictionCheck(string RuleId,RestrictionKind Kind,RestrictionOutcome Outcome,string Reason,string Source,string Version,int FirstEdge,int LastEdge);
public sealed class RouteTrustResult
{
    public Guid CalculationId {get;set;}=Guid.NewGuid();
    public string CalculationVersion {get;set;}="7B1-1";
    public DateTime ValidUntilUtc {get;set;}
    public List<RouteStep> Steps {get;set;}=[];
    public DateTime CalculatedAtUtc {get;set;}=DateTime.UtcNow;
    public string Provider {get;set;}="";
    public string RoutingProfile {get;set;}="";
    public string? GeographicVersion {get;set;}
    public string? SourceReference {get;set;}
    public string RulesVersion {get;set;}="";
    public string GeometrySha256 {get;set;}="";
    public string? Territory {get;set;}
    public bool Synthetic {get;set;}
    public RouteConfidence Confidence {get;set;}
    public bool NavigationAllowed {get;set;}
    public bool Obsolete {get;set;}
    public int VehicleVersion {get;set;}
    public string? PermanentVehicleSnapshot {get;set;}
    public string? SourcePolicyHash {get;set;}
    public ProviderRouteReply? ProviderReply {get;set;}
    public int? VehicleId {get;set;}
    public string? VehiclePlate {get;set;}
    public List<string> Reasons {get;set;}=[];
    public List<string> MissingData {get;set;}=[];
    public List<RestrictionCheck> Checks {get;set;}=[];
    public OperationalTruckProfile Vehicle {get;set;}=new();
    public List<RouteCoordinate> Geometry {get;set;}=[];
    public decimal? EstimatedDistanceKm {get;set;}
    public int? EstimatedMinutes {get;set;}
}
public static class RouteFingerprint
{
    public static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string Geometry(IEnumerable<RouteCoordinate> points)=>Hash(points.Select(p=>new{lat=p.Latitude,lng=p.Longitude}));
    public static string Rules(RouteEvidence e)=>Hash(new{e.Provider,e.RoutingProfile,e.GeographicVersion,e.SourceReference,e.Territory,e.Synthetic,e.Rules});
    public static string Binding(Entrega e)=>Hash(new {
        Trip=new{e.Id,e.TenantId,e.MotoristaId,e.VeiculoId,e.Versao,e.PerfilViagemJson,e.PerfilViagemVersao,e.StatusEntrega,e.AtribuicaoAtiva,e.EnderecoDestino,e.NumeroDestino,e.CidadeDestino,e.EstadoDestino,e.CepDestino,e.TipoCarga,e.AlturaMetros,e.LarguraMetros,e.PesoToneladas,e.QtdEixos},
        Order=e.Pedido==null?null:new{e.Pedido.PesoKg,e.Pedido.TipoCarga,e.Pedido.AlturaCargaMetros,e.Pedido.LarguraCargaMetros,e.Pedido.ComprimentoCargaMetros,e.Pedido.OrigemEndereco,e.Pedido.OrigemCidade,e.Pedido.OrigemEstado},
        Vehicle=e.Veiculo==null?null:new{e.Veiculo.Placa,e.Veiculo.PerfilOperacionalJson,e.Veiculo.PerfilOperacionalVersao,e.Veiculo.Ativo,e.Veiculo.AlturaMetros,e.Veiculo.LarguraMetros,e.Veiculo.ComprimentoMetros,e.Veiculo.PesoToneladas,e.Veiculo.QtdEixos,e.Veiculo.CapacidadeCargaKg,e.Veiculo.TipoCarga}});
}
