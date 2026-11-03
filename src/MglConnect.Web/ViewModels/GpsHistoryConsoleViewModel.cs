using MglConnect.Web.Services;
namespace MglConnect.Web.ViewModels;
public sealed record GpsTripOption(int Id,string Codigo,string Estado);
public sealed record GpsArchiveSummary(Guid Id,DateTime CriadoEmUtc,int Quantidade,string Sha256,DateTime? CompactadoEmUtc,int ResponsavelId,long Bytes);
public sealed class GpsHistoryConsoleViewModel
{
    public string? Termo { get; init; }
    public int PaginaArquivos { get; init; }
    public int PaginasArquivos { get; init; }
    public long BytesArquivados { get; init; }
    public long LimiteBytesArquivo { get; init; }
    public int? EntregaId { get; init; }
    public string? De { get; init; }
    public string? Ate { get; init; }
    public string? StatusEntrega { get; init; }
    public IReadOnlyList<GpsTripOption> Viagens { get; init; }=[];
    public IReadOnlyList<GpsArchiveSummary> Arquivos { get; init; }=[];
    public int PosicoesOnlineSemArquivo { get; init; }
    public int RetencaoDias { get; init; }
    public int LinhasCsv { get; init; }
    public int CotaViagem24h { get; init; }
    public int Recebidas24h { get; init; }
}
