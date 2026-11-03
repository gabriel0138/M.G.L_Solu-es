using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models;

[Table("PosicoesGps")]
public sealed class PosicaoGps : ITenantOwned
{
    [Key] public long Id { get; set; }
    public Guid EventoId { get; set; }
    public Guid TenantId { get; set; }
    public int EntregaId { get; set; }
    public int MotoristaId { get; set; }
    public int VeiculoId { get; set; }
    public Guid? TurnoId { get; set; }
    public int AtorUsuarioId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? PrecisaoMetros { get; set; }
    public double? VelocidadeKmH { get; set; }
    public double? Direcao { get; set; }
    public DateTime? CapturadoEmUtc { get; set; }
    public DateTime? OrdenadoEmUtc { get; set; }
    public bool? ForaDeOrdem { get; set; }
    public DateTime RecebidoEmUtc { get; set; }
    [Required, StringLength(12)] public string Origem { get; set; } = "GPS";
    public bool CapturaInformada { get; set; } = true;
}
