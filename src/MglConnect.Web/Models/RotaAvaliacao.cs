using System.ComponentModel.DataAnnotations;
namespace MglConnect.Web.Models;
public sealed class RotaAvaliacao:ITenantOwned
{
    public Guid Id {get;set;}
    public Guid TenantId {get;set;}
    public int EntregaId {get;set;}
    public int? VeiculoId {get;set;}
    public int? AtorUsuarioId {get;set;}
    [MaxLength(64)]public string VinculoHash {get;set;}="";
    [MaxLength(64)]public string RegrasHash {get;set;}="";
    public DateTime CriadoEmUtc {get;set;}
    public DateTime ExpiraEmUtc {get;set;}
    public bool Obsoleta {get;set;}
    public string ResultadoJson {get;set;}="";
}
