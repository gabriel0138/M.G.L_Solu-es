namespace MglConnect.Web.Services;

public enum DriverShiftStartStatus
{
    Started,
    AlreadyActive,
    InvalidCode,
    Expired,
    Cancelled,
    AttemptsExceeded,
    AssignmentUnavailable,
    AnotherShiftActive,
    Conflict
}

public sealed record DriverShiftStartResult(DriverShiftStartStatus Status, Guid? ShiftId = null);
public sealed record DriverShiftCodeIssueResult(bool Success, string? Code, DateTime? ExpiresAtUtc, string? Error);

public interface IDriverShiftService
{
    Task<DriverShiftCodeIssueResult> IssueCodeAsync(int deliveryId, int adminUserId, CancellationToken cancellationToken = default);
    Task<bool> CancelCodeAsync(int deliveryId, int adminUserId, string reason, CancellationToken cancellationToken = default);
    Task<DriverShiftStartResult> StartAsync(int driverId, int deliveryId, string code, CancellationToken cancellationToken = default);
    Task<bool> HasActiveShiftAsync(int driverId, int deliveryId, CancellationToken cancellationToken = default);
    Task<bool> EndAsync(int driverId, CancellationToken cancellationToken = default);
}
