using System.Security.Cryptography;
using System.Text.Json;
using MglConnect.Web.Models;
namespace MglConnect.Web.Services;

public static class GpsQuality
{
    public const double MaximumAuditAccuracy = 10000;
    public static string Classify(double? accuracy) => accuracy switch
    { null => "precisao_desconhecida", <=50 => "adequada", <=100 => "aproximada", _ => "baixa_precisao" };
    public static bool CanMap(double? accuracy) => accuracy == null || accuracy <= 100;
    public static bool CanNavigate(double? accuracy) => accuracy is >=0 and <=50;
}
public static class GpsFingerprint
{
    public static string Input(int actorId, Guid tenant, GpsEventInput input, bool legacy) => Hash(new
    {
        actorId, tenant, input.EventoId, input.EntregaId,
        turnoId = legacy ? null : input.TurnoId, captura = legacy ? (long?)null : input.CapturadoEmUtc.UtcTicks,
        input.Latitude, input.Longitude, input.PrecisaoMetros, input.VelocidadeKmH, input.Direcao, input.Simulado
    });
    public static string Stored(PosicaoGps p) => Input(p.AtorUsuarioId,p.TenantId,
        new(p.EventoId,p.EntregaId,p.TurnoId,p.Latitude,p.Longitude,p.PrecisaoMetros,
            new DateTimeOffset(GpsIngestionService.Utc(p.CapturadoEmUtc ?? p.RecebidoEmUtc)),p.VelocidadeKmH,p.Direcao,p.Origem=="Simulacao"),!p.CapturaInformada);
    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
