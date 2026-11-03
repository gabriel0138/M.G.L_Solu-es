using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace MglConnect.Web.Models;

[Table("GpsArquivos")]
public sealed class GpsArquivo : ITenantOwned
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EntregaId { get; set; }
    public int CriadoPorUsuarioId { get; set; }
    public DateTime CriadoEmUtc { get; set; }
    public int? CompactadoPorUsuarioId { get; set; }
    public DateTime? CompactadoEmUtc { get; set; }
    public int Quantidade { get; set; }
    public long PrimeiraSequencia { get; set; }
    public long UltimaSequencia { get; set; }
    [Required, StringLength(64), Column(TypeName="varchar(64)")] public string Sha256 { get; set; } = "";
    [Required] public byte[] ConteudoGzip { get; set; } = [];
    public int Versao { get; set; }
}

// A permanent identity/lookup index survives removal of the verified online copy.
// Coordinates and full original records remain in the bounded compressed package.
[Table("GpsEventosArquivados")]
public sealed class GpsEventoArquivado : ITenantOwned
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public long Id { get; set; }
    public Guid EventoId { get; set; }
    public Guid TenantId { get; set; }
    public int EntregaId { get; set; }
    public int MotoristaId { get; set; }
    public int VeiculoId { get; set; }
    public int AtorUsuarioId { get; set; }
    public Guid? TurnoId { get; set; }
    public Guid ArquivoId { get; set; }
    public DateTime? CapturadoEmUtc { get; set; }
    public DateTime RecebidoEmUtc { get; set; }
    public DateTime OrdenadoEmUtc { get; set; }
    public double? PrecisaoMetros { get; set; }
    [Required, StringLength(12)] public string Origem { get; set; } = "GPS";
    public bool CapturaInformada { get; set; }
    public bool? ForaDeOrdem { get; set; }
    [Required, StringLength(64), Column(TypeName="varchar(64)")] public string ImpressaoRequisicao { get; set; } = "";
}
