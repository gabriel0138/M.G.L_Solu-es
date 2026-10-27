using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models;

[Table("TurnosMotorista")]
public class TurnoMotorista : ITenantOwned
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    [Required]
    public int EntregaId { get; set; }

    [Required]
    public int MotoristaId { get; set; }

    [Required]
    public int VeiculoId { get; set; }

    [Required]
    public Guid CodigoTurnoId { get; set; }

    public DateTime IniciadoEmUtc { get; set; }
    public DateTime? EncerradoEmUtc { get; set; }
    public bool Ativo { get; set; } = true;
    public int Versao { get; set; }
}
