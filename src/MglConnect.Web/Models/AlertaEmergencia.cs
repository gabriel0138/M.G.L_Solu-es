using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models;

[Table("AlertasEmergencia")]
public class AlertaEmergencia : ITenantOwned
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    [Required]
    public Guid ChaveIdempotencia { get; set; }

    [Required]
    public int EntregaId { get; set; }

    [Required]
    public int MotoristaId { get; set; }

    public int? VeiculoId { get; set; }

    public DateTime CriadoEmUtc { get; set; }

    [StringLength(30)]
    public string Status { get; set; } = "Aberto";

    [StringLength(30)]
    public string SituacaoLocalizacao { get; set; } = "Indisponível";

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? PrecisaoMetros { get; set; }
    public DateTime? LocalizacaoCapturadaEmUtc { get; set; }
    public int? ReconhecidoPorUsuarioId { get; set; }
    public DateTime? ReconhecidoEmUtc { get; set; }
    public DateTime? AtualizadoEmUtc { get; set; }
    public int Versao { get; set; }
}
