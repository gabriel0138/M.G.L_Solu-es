using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.ModelBinding;
using MglConnect.Web.Models;
using MglConnect.Web.Services.RouteTrust;
namespace MglConnect.Web.ViewModels;
public sealed class TruckProfileForm : IValidatableObject
{
    public int Revision {get;set;}
    [ModelBinder(BinderType=typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal),"0.01","1000000",ParseLimitsInInvariantCulture=true)]public decimal? Height {get;set;}
    [ModelBinder(BinderType=typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal),"0.01","1000000",ParseLimitsInInvariantCulture=true)]public decimal? Width {get;set;}
    [ModelBinder(BinderType=typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal),"0.01","1000000",ParseLimitsInInvariantCulture=true)]public decimal? Length {get;set;}
    [ModelBinder(BinderType=typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal),"0.01","1000000",ParseLimitsInInvariantCulture=true)]public decimal? Tare {get;set;}
    [ModelBinder(BinderType=typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal),"0.01","1000000",ParseLimitsInInvariantCulture=true)]public decimal? Pbt {get;set;}
    [ModelBinder(BinderType=typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal),"0.01","1000000",ParseLimitsInInvariantCulture=true)]public decimal? Pbtc {get;set;}
    [ModelBinder(BinderType=typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal),"0","1000000",ParseLimitsInInvariantCulture=true)]public decimal? Payload {get;set;}
    [Range(1,30)]public int? Axles {get;set;}
    [StringLength(200)]public string? Category {get;set;}
    [StringLength(200)]public string? Composition {get;set;}
    [StringLength(200)]public string? Cargo {get;set;}
    public bool? Hazardous {get;set;}
    [StringLength(200)]public string? HazardClass {get;set;}
    [StringLength(1000)]public string? Permits {get;set;}
    [StringLength(200)]public string? AccessRequirements {get;set;}
    [DisplayFormat(ConvertEmptyStringToNull=false)]
    [StringLength(1000)]public string? AxleLoadsKg {get;set;}
    public OperationalTruckProfile ToProfile(int trip)
    {
        Measure? M(decimal? v,string unit)=>v.HasValue?new(v.Value,unit,FactOrigin.Declarado,$"Declaração administrativa para viagem {trip}"):null;
        var loads = ParseAxleLoads();
        return new(){TripId=trip,Height=M(Height,"m"),Width=M(Width,"m"),Length=M(Length,"m"),Tare=M(Tare,"kg"),Pbt=M(Pbt,"kg"),Pbtc=M(Pbtc,"kg"),Payload=M(Payload,"kg"),
            Axles=Axles,Category=Category?.Trim(),Composition=Composition?.Trim(),Cargo=Cargo?.Trim(),Hazardous=Hazardous,HazardClass=HazardClass?.Trim(),
            PermitsDeclared=string.IsNullOrWhiteSpace(Permits)?null:true,
            Permits=(Permits=="nenhuma"?"":Permits??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList(),AccessRequirements=AccessRequirements?.Trim(),
            AxleLoads=loads.Select(v=>M(v,"kg")!).ToList()};
    }
    private List<decimal> ParseAxleLoads()
    {
        if (string.IsNullOrEmpty(AxleLoadsKg)) return [];
        var loads = new List<decimal>();
        foreach (var text in AxleLoadsKg.Split(';'))
        {
            if (!DecimalFormContract.TryParse(text, out var value))
                throw new FormatException(DecimalFormContract.Error);
            loads.Add(value);
        }
        return loads;
    }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        foreach (var (field, value) in new[] { (nameof(Height), Height), (nameof(Width), Width),
            (nameof(Length), Length), (nameof(Tare), Tare), (nameof(Pbt), Pbt),
            (nameof(Pbtc), Pbtc), (nameof(Payload), Payload) })
            if (value.HasValue && !DecimalFormContract.HasSupportedScale(value.Value))
                yield return new ValidationResult(DecimalFormContract.Error, [field]);
        bool valid;
        try { ParseAxleLoads(); valid = true; }
        catch (FormatException) { valid = false; }
        if (!valid) yield return new ValidationResult(DecimalFormContract.Error + " Separe as cargas por eixo com ponto e vírgula.", [nameof(AxleLoadsKg)]);
    }
    public static TruckProfileForm From(OperationalTruckProfile p,int version)=>new(){Revision=version,Height=p.Height?.Value,Width=p.Width?.Value,Length=p.Length?.Value,Tare=p.Tare?.Value,Pbt=p.Pbt?.Value,Pbtc=p.Pbtc?.Value,Payload=p.Payload?.Value,
        Axles=p.Axles,Category=p.Category,Composition=p.Composition,Cargo=p.Cargo,Hazardous=p.Hazardous,HazardClass=p.HazardClass,Permits=p.PermitsDeclared==true&&p.Permits.Count==0?"nenhuma":string.Join(';',p.Permits),AccessRequirements=p.AccessRequirements,
        AxleLoadsKg=string.Join(';',p.AxleLoads.Select(m=>m.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))};
}
public sealed class RouteAssessmentViewModel
{
    public CoverageInventory Inventory {get;init;}=BrazilCoverageInventory.Read();
    public required Entrega Delivery {get;init;}
    public RouteTrustResult? Assessment {get;init;}
    public TruckProfileForm Profile {get;init;}=new();
    public IReadOnlyList<PerfilViagemRevisao> ProfileHistory {get;init;}=[];
    public IReadOnlyList<RotaAvaliacao> History {get;init;}=[];
}
