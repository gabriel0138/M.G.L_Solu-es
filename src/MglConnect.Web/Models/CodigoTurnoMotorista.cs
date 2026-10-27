using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models;

[Table("CodigosTurnoMotorista")]
public class CodigoTurnoMotorista : ITenantOwned
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    [Required]
    public int EntregaId { get; set; }

    [Required]
    public int MotoristaId { get; set; }

    [Required, StringLength(64)]
    public string CodigoHash { get; set; } = string.Empty;

    [Required]
    public int EmitidoPorUsuarioId { get; set; }

    public DateTime EmitidoEmUtc { get; set; }
    public DateTime ExpiraEmUtc { get; set; }
    public DateTime? UsadoEmUtc { get; set; }
    public DateTime? CanceladoEmUtc { get; set; }
    public int Tentativas { get; set; }
    public int Versao { get; set; }
}
