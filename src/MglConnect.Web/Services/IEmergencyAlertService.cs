using MglConnect.Web.Models;

namespace MglConnect.Web.Services;

public sealed record EmergencyAlertRequest(
    Guid IdempotencyKey,
    int DeliveryId,
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters,
    DateTime? PositionCapturedAtUtc);

public sealed record EmergencyAlertResult(bool Success, bool Duplicate, AlertaEmergencia? Alert, string? Error);

public interface IEmergencyAlertService
{
    Task<EmergencyAlertResult> CreateAsync(int driverId, EmergencyAlertRequest request, CancellationToken cancellationToken = default);
    Task<bool> AcknowledgeAsync(Guid alertId, int operatorId, CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(Guid alertId, int operatorId, string status, CancellationToken cancellationToken = default);
}
