namespace MglConnect.Web.Services;

public sealed record GpsEventInput(Guid EventoId, int EntregaId, Guid? TurnoId, double Latitude,
    double Longitude, double? PrecisaoMetros, DateTimeOffset CapturadoEmUtc,
    double? VelocidadeKmH = null, double? Direcao = null, bool Simulado = false);
public sealed record GpsBatchInput(IReadOnlyList<GpsEventInput>? Eventos);
public sealed record GpsPositionPayload(string CodigoRastreio, double Latitude, double Longitude,
    double? SpeedKmH, double? Heading, double? AccuracyMeters, bool IsSimulated,
    DateTime Timestamp, DateTime ReceivedAt, Guid EventId, bool CaptureTimeKnown = true);
public sealed record GpsReceipt(Guid EventoId, string Estado, string? Motivo = null,
    long? Sequencia = null, DateTime? CapturadoEmUtc = null, DateTime? RecebidoEmUtc = null,
    GpsPositionPayload? Posicao = null, bool AtualizouPosicao = false);
public sealed class GpsHistoryOptions
{
    public int MaximoPorViagem { get; set; } = 20000;
    public int MaximoPorOrganizacao { get; set; } = 1000000;
    // Moving 24h admission budgets, not lifetime limits on an active trip.
    public int HorasMaximasSincronizacao { get; set; } = 168;
    public int DiasAntesCompactar { get; set; } = 30;
    public int TamanhoLoteArquivo { get; set; } = 500;
    public long MaximoBytesArquivadosPorOrganizacao { get; set; } = 128L * 1024 * 1024;
    public int LinhasPorExportacao { get; set; } = 5000;
}
