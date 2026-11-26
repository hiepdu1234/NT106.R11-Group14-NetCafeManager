namespace NetCafeManager.Shared.Administration;

public sealed record CustomerIdRequest(Guid CustomerId);

public sealed record ServiceItemIdRequest(int ItemId);

public sealed record InvoiceStatusRequest(
    long InvoiceId,
    string? Status);

public sealed record MachineAdminSummary(
    int Id,
    string Name,
    string? IpAddress,
    string? MacAddress,
    string Status,
    DateTimeOffset? LastHeartbeatAt,
    string? MaintenanceReason,
    bool HasActiveSession);

public sealed record SetMachineStatusRequest(
    int ComputerId,
    string? Status,
    string? Reason);

public sealed record MachineStatusHistoryRequest(int ComputerId);

public sealed record MachineStatusHistorySummary(
    long Id,
    int ComputerId,
    string ComputerName,
    string PreviousStatus,
    string NewStatus,
    string? Reason,
    string AdministratorUsername,
    DateTimeOffset ChangedAt);

public static class RemoteCommandTypes
{
    public const string LockComputer = "KhoaMay";
    public const string LogoutComputer = "DangXuat";
}

public static class RemoteCommandStatuses
{
    public const string Pending = "ChoGui";
    public const string Sent = "DaGui";
    public const string Acknowledged = "DaACK";
    public const string Succeeded = "ThanhCong";
    public const string Failed = "ThatBai";
}

public sealed record QueueRemoteCommandRequest(
    int ComputerId,
    string? CommandType);

public sealed record RemoteCommandHistoryRequest(int ComputerId);

public sealed record PendingRemoteCommand(
    Guid CommandId,
    string CommandType,
    string? Payload);

public sealed record RemoteCommandResultRequest(
    Guid CommandId,
    bool Succeeded,
    string? ErrorCode);

public sealed record RemoteCommandSummary(
    Guid CommandId,
    int ComputerId,
    string ComputerName,
    string AdministratorUsername,
    string CommandType,
    string Status,
    DateTimeOffset SentAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode);

public sealed record AdministrativeAuditSummary(
    long Id,
    string AdministratorUsername,
    string Action,
    string TargetType,
    string? TargetId,
    bool Succeeded,
    string Description,
    DateTimeOffset CreatedAt);

public sealed record AdminResult<T>(
    bool IsSuccess,
    string? ErrorCode,
    string Message,
    T? Value)
{
    public static AdminResult<T> Success(T value, string message) =>
        new(true, null, message, value);

    public static AdminResult<T> Failure(string errorCode, string message) =>
        new(false, errorCode, message, default);
}
