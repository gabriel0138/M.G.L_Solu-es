using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MglConnect.Web.Data;

namespace MglConnect.Web.Services;

/// <summary>Persists a server-received GPS snapshot without changing business concurrency or delivery state.</summary>
public sealed class DeliveryPositionRecorder(MglDbContext context, TimeProvider? clock = null, IOptions<GpsHistoryOptions>? options = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<DateTime?> RecordLiveAsync(int actorId, string trackingCode, double latitude, double longitude,
        double speedKmH, double heading, double? accuracyMeters, bool isSimulated, CancellationToken cancellationToken = default)
    {
        var code = trackingCode.Trim().ToUpperInvariant();
        var deliveryId = await context.Entregas.Where(e => e.CodigoRastreio == code).Select(e => e.Id).FirstOrDefaultAsync(cancellationToken);
        if (deliveryId == 0) return null;
        var result = await new GpsIngestionService(context, _clock, options).AcceptAsync(actorId,
            new GpsEventInput(Guid.NewGuid(), deliveryId, null, latitude, longitude, accuracyMeters,
                _clock.GetUtcNow(), speedKmH, heading, isSimulated), legacy: true, cancellationToken);
        return result.Estado == "confirmado" ? result.RecebidoEmUtc : null;
    }

    public async Task<DateTime?> RecordAsync(string trackingCode, double latitude, double longitude,
        double speedKmH, double heading, double? accuracyMeters, bool isSimulated,
        CancellationToken cancellationToken = default)
    {
        if (isSimulated || string.IsNullOrWhiteSpace(trackingCode)
            || !double.IsFinite(latitude) || latitude is < -90 or > 90
            || !double.IsFinite(longitude) || longitude is < -180 or > 180
            || !double.IsFinite(speedKmH) || speedKmH is < 0 or > 200
            || !double.IsFinite(heading) || heading is < 0 or >= 360
            || (accuracyMeters.HasValue && (!double.IsFinite(accuracyMeters.Value)
                || accuracyMeters < 0 || accuracyMeters > RouteDeviationEngine.MaximumAcceptedGpsAccuracyMeters)))
            return null;

        var receivedAt = _clock.GetUtcNow().UtcDateTime;
        var code = trackingCode.Trim().ToUpperInvariant();
        var active = context.Entregas.Where(e => e.CodigoRastreio == code && e.AtribuicaoAtiva
            && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte")
            && (!e.UltimoGpsRecebidoEmUtc.HasValue || e.UltimoGpsRecebidoEmUtc <= receivedAt));

        if (context.Database.IsRelational())
        {
            // Update only snapshot columns. SaveChanges on a tracked delivery
            // could overwrite a concurrent assignment or advance its Versao.
            var affected = await active.ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.UltimaLatitudeGps, (double?)latitude)
                .SetProperty(e => e.UltimaLongitudeGps, (double?)longitude)
                .SetProperty(e => e.UltimaPrecisaoGps, accuracyMeters)
                .SetProperty(e => e.UltimaVelocidadeGps, (double?)speedKmH)
                .SetProperty(e => e.UltimaDirecaoGps, (double?)heading)
                .SetProperty(e => e.UltimoGpsRecebidoEmUtc, (DateTime?)receivedAt), cancellationToken);
            return affected > 0 ? receivedAt : null;
        }

        // EF's non-relational InMemory test provider has no ExecuteUpdate.
        var delivery = await active.FirstOrDefaultAsync(cancellationToken);
        if (delivery == null) return null;
        delivery.UltimaLatitudeGps = latitude;
        delivery.UltimaLongitudeGps = longitude;
        delivery.UltimaPrecisaoGps = accuracyMeters;
        delivery.UltimaVelocidadeGps = speedKmH;
        delivery.UltimaDirecaoGps = heading;
        delivery.UltimoGpsRecebidoEmUtc = receivedAt;
        await context.SaveChangesAsync(cancellationToken);
        return receivedAt;
    }
}
