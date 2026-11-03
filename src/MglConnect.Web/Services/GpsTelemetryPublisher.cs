using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;

namespace MglConnect.Web.Services;

public sealed class GpsTelemetryPublisher(MglDbContext db, IHubContext<TelemetryHub> hub,
    TelemetrySubscriptions subscriptions, IDriverShiftService shifts, IRouteDeviationEngine engine,
    ITruckRoutingService routes, ILogger<GpsTelemetryPublisher> logger, MglConnect.Web.Services.RouteTrust.RouteTrustService? trust=null)
{
    public async Task<object?> PublishAsync(GpsReceipt receipt)
    {
        if (!receipt.AtualizouPosicao || receipt.Posicao is not {} p || p.IsSimulated) return null;
        if (!await db.Entregas.AnyAsync(e => e.CodigoRastreio == p.CodigoRastreio
            && e.UltimoGpsCapturadoEmUtc == p.Timestamp && e.UltimaLatitudeGps == p.Latitude && e.UltimaLongitudeGps == p.Longitude)) return null;
        async Task Send(string name, object payload)
        {
            // Revalidate after awaited routing as well as before the first point.
            // A cached recipient list can outlive a shift, account or customer link.
            if (!await db.Entregas.AnyAsync(e => e.CodigoRastreio == p.CodigoRastreio
                && e.UltimoGpsCapturadoEmUtc == p.Timestamp && e.UltimaLatitudeGps == p.Latitude && e.UltimaLongitudeGps == p.Longitude)) return;
            var audience = await TelemetryAudience.ResolveAsync(db, shifts, subscriptions, p.CodigoRastreio, hub.Groups);
            if (audience.Count > 0) await hub.Clients.Clients(audience).SendAsync(name, payload);
        }
        await Send("ReceiveVehiclePosition", p);
        // Historical samples are never interpreted as current deviation frames or used to reroute.
        if (!GpsQuality.CanNavigate(p.AccuracyMeters) || DateTime.UtcNow - p.Timestamp > TimeSpan.FromSeconds(90)) return null;
        var state=engine.GetState(p.CodigoRastreio);
        var trip=await db.Entregas.Where(e=>e.CodigoRastreio==p.CodigoRastreio).Select(e=>e.Id).SingleAsync();
        bool allowed=trust==null||await trust.NavigationAllowedAsync(trip);
        if(!allowed)return new{gpsDataAccepted=true,navigationAllowed=false,motivo="GPS observado preservado; orientação suspensa até validação da rota."};
        var result = engine.EvaluatePosition(p.CodigoRastreio, p.Latitude, p.Longitude,
            p.SpeedKmH ?? 0, p.Heading ?? 0, p.AccuracyMeters);
        if (result.NeedsReroute)
        {
            try
            {
                var plan = await routes.RecalcularRotaDinamicaAsync(p.CodigoRastreio, p.Latitude, p.Longitude,
                    engine.GetState(p.CodigoRastreio)?.PerfilVeiculo ?? "Pesado");
                if (plan == null) throw new InvalidOperationException("Rota alternativa não disponível.");
                await Send("ReceiveRouteRecalculated", new
                {
                    codigoRastreio = p.CodigoRastreio, origem = plan.Origem, destino = plan.Destino,
                    distanciaKm = plan.DistanciaKm, tempoEstimadoMinutos = plan.TempoEstimadoMinutos,
                    trajetoCoordenadas = plan.TrajetoCoordenadas.Select(c => new { lat = c.Latitude, lng = c.Longitude }),
                    passosNavegacao = plan.PassosNavegacao, timestamp = DateTime.UtcNow,
                    navigationAllowed=plan.Confianca?.NavigationAllowed==true, routeTrust=plan.Confianca?.Confidence.ToString(),
                    isSimulated = false, verifiedForVehicle = plan.RotaHomologadaParaVeiculo, mensagem = plan.AvisoRota
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning("Recálculo GPS indisponível para a entrega: {ErrorType}.", ex.GetType().Name);
                await Send("ReceiveRouteRecalculationFailed", new
                {
                    codigoRastreio = p.CodigoRastreio, isSimulated = false, revisaoDespachanteNecessaria = true,
                    mensagem = "Não foi possível validar uma rota alternativa. Procure a operação.", timestamp = DateTime.UtcNow
                });
            }
        }
        return new
        {
            gpsDataAccepted = result.GpsDataAccepted, gpsAccuracyMeters = p.AccuracyMeters, motivo = result.Motivo,
            isOffRoute = result.IsOffRoute, crossTrackDistanceMeters = result.CrossTrackDistanceMeters,
            alongTrackDistanceMeters = result.AlongTrackDistanceMeters, azimuthDivergenceDegrees = result.AzimuthDivergenceDegrees,
            offRouteFramesCount = result.OffRouteFramesCount, activeSegmentIndex = result.ActiveSegmentIndex
        };
    }
}
