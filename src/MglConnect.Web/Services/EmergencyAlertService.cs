using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services;

public sealed class EmergencyAlertService : IEmergencyAlertService
{
    private static readonly TimeSpan MaxLocationAge = TimeSpan.FromMinutes(2);
    private readonly MglDbContext _context;
    private readonly TimeProvider _clock;

    public EmergencyAlertService(MglDbContext context, TimeProvider? clock = null)
    {
        _context = context;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<EmergencyAlertResult> CreateAsync(
        int driverId,
        EmergencyAlertRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.IdempotencyKey == Guid.Empty)
        {
            return new(false, false, null, "Chave de solicitação inválida.");
        }

        var existing = await _context.AlertasEmergencia.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ChaveIdempotencia == request.IdempotencyKey, cancellationToken);
        if (existing != null)
        {
            return existing.MotoristaId == driverId && existing.EntregaId == request.DeliveryId
                ? new(true, true, existing, null)
                : new(false, false, null, "Chave de solicitação já utilizada.");
        }

        var shift = await _context.TurnosMotorista.AsNoTracking().FirstOrDefaultAsync(
            item => item.MotoristaId == driverId
                && item.EntregaId == request.DeliveryId
                && item.Ativo
                && _context.Usuarios.Any(user =>
                    user.Id == driverId && user.Ativo && user.Perfil == "Motorista"),
            cancellationToken);
        var delivery = await _context.Entregas.AsNoTracking().FirstOrDefaultAsync(
            item => item.Id == request.DeliveryId
                && item.MotoristaId == driverId
                && item.StatusEntrega == "Em transporte"
                && item.AtribuicaoAtiva,
            cancellationToken);
        if (shift == null || delivery == null)
        {
            return new(false, false, null, "Não há turno ativo autorizado para esta operação.");
        }

        if (request.Latitude.HasValue != request.Longitude.HasValue
            || (request.Latitude.HasValue && (!double.IsFinite(request.Latitude.Value) || request.Latitude is < -90 or > 90))
            || (request.Longitude.HasValue && (!double.IsFinite(request.Longitude.Value) || request.Longitude is < -180 or > 180))
            || (request.AccuracyMeters.HasValue
                && (!double.IsFinite(request.AccuracyMeters.Value) || request.AccuracyMeters.Value < 0 || request.AccuracyMeters.Value > 1000)))
        {
            return new(false, false, null, "Localização inválida.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var locationState = "Indisponível";
        var latitude = (double?)null;
        var longitude = (double?)null;
        var accuracy = (double?)null;
        var capturedAt = (DateTime?)null;
        if (request.Latitude.HasValue && request.PositionCapturedAtUtc.HasValue)
        {
            capturedAt = DateTime.SpecifyKind(request.PositionCapturedAtUtc.Value, DateTimeKind.Utc);
            var age = now - capturedAt.Value;
            if (age >= TimeSpan.FromSeconds(-30) && age <= MaxLocationAge)
            {
                latitude = request.Latitude;
                longitude = request.Longitude;
                accuracy = request.AccuracyMeters;
                locationState = "Disponível";
            }
            else
            {
                locationState = "Desatualizada";
                latitude = request.Latitude;
                longitude = request.Longitude;
                accuracy = request.AccuracyMeters;
            }
        }

        var alert = new AlertaEmergencia
        {
            ChaveIdempotencia = request.IdempotencyKey,
            EntregaId = delivery.Id,
            MotoristaId = driverId,
            VeiculoId = delivery.VeiculoId,
            CriadoEmUtc = now,
            Status = "Aberto",
            SituacaoLocalizacao = locationState,
            Latitude = latitude,
            Longitude = longitude,
            PrecisaoMetros = accuracy,
            LocalizacaoCapturadaEmUtc = capturedAt
        };
        _context.AlertasEmergencia.Add(alert);
        AddAudit(driverId, alert.Id, "AlertaCriado", null, "Aberto", now);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new(true, false, alert, null);
        }
        catch (DbUpdateException)
        {
            _context.Entry(alert).State = EntityState.Detached;
            foreach (var auditEntry in _context.ChangeTracker.Entries<AuditoriaOperacional>()
                         .Where(entry => entry.Entity.TipoEntidade == "AlertaEmergencia"
                             && entry.Entity.IdentificadorEntidade == alert.Id.ToString("N")))
            {
                auditEntry.State = EntityState.Detached;
            }

            var duplicate = await _context.AlertasEmergencia.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ChaveIdempotencia == request.IdempotencyKey, cancellationToken);
            if (duplicate?.MotoristaId == driverId && duplicate.EntregaId == request.DeliveryId)
            {
                return new(true, true, duplicate, null);
            }
            throw;
        }
    }

    public async Task<bool> AcknowledgeAsync(
        Guid alertId,
        int operatorId,
        CancellationToken cancellationToken = default)
    {
        var alert = await _context.AlertasEmergencia.FirstOrDefaultAsync(
            item => item.Id == alertId,
            cancellationToken);
        if (alert == null || alert.Status != "Aberto")
        {
            return false;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        alert.Status = "Reconhecido";
        alert.ReconhecidoPorUsuarioId = operatorId;
        alert.ReconhecidoEmUtc = now;
        alert.AtualizadoEmUtc = now;
        alert.Versao++;
        AddAudit(operatorId, alert.Id, "AlertaReconhecido", "Aberto", "Reconhecido", now);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<bool> UpdateStatusAsync(
        Guid alertId,
        int operatorId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var alert = await _context.AlertasEmergencia.FirstOrDefaultAsync(
            item => item.Id == alertId,
            cancellationToken);
        if (alert == null || !IsValidTransition(alert.Status, status))
        {
            return false;
        }

        var previousStatus = alert.Status;
        var now = _clock.GetUtcNow().UtcDateTime;
        alert.Status = status;
        alert.AtualizadoEmUtc = now;
        alert.Versao++;
        AddAudit(operatorId, alert.Id, "StatusAlertaAtualizado", previousStatus, status, now);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private void AddAudit(int actorId, Guid alertId, string eventName, string? previous, string current, DateTime now) =>
        _context.AuditoriaOperacional.Add(new AuditoriaOperacional
        {
            AtorUsuarioId = actorId,
            Evento = eventName,
            TipoEntidade = "AlertaEmergencia",
            IdentificadorEntidade = alertId.ToString("N"),
            Detalhes = previous == null ? current : $"{previous} -> {current}",
            OcorridoEmUtc = now
        });

    private static bool IsValidTransition(string current, string next) =>
        (current, next) switch
        {
            ("Reconhecido", "EmAtendimento") => true,
            ("EmAtendimento", "Resolvido") => true,
            ("Aberto", "FalsoAlarme") => true,
            ("Reconhecido", "FalsoAlarme") => true,
            ("EmAtendimento", "FalsoAlarme") => true,
            _ => false
        };
}
