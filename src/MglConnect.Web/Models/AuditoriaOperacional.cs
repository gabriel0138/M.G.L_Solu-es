using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MglConnect.Web.Models;

[Table("AuditoriaOperacional")]
public class AuditoriaOperacional : ITenantOwned
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    [Required]
    public int AtorUsuarioId { get; set; }

    [Required, StringLength(60)]
    public string Evento { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string TipoEntidade { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string IdentificadorEntidade { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Detalhes { get; set; }

    public DateTime OcorridoEmUtc { get; set; }
}
