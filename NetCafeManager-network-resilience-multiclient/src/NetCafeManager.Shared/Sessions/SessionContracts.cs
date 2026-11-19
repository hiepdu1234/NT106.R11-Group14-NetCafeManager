namespace NetCafeManager.Shared.Sessions;

public static class ComputerStatuses
{
    public const string Available = "Trong";
    public const string InUse = "DangSuDung";
    public const string Locked = "Khoa";
    public const string Maintenance = "BaoTri";
    public const string Incident = "SuCo";
    public const string Disconnected = "MatKetNoi";
}

public static class UsageSessionStatuses
{
    public const string InUse = "DangSuDung";
    public const string Paused = "TamDung";
    public const string Ended = "KetThuc";
}

public sealed record MachineHandshakeChallenge(
    Guid ChallengeId,
    string MachineName,
    string NonceBase64,
    DateTimeOffset ExpiresAt);

public sealed record VerifiedMachine(
    int ComputerId,
    string MachineName,
    DateTimeOffset VerifiedAt,
    DateTimeOffset ExpiresAt);

public sealed record UsageSessionSummary(
    long SessionId,
    int ComputerId,
    string ComputerName,
    Guid AccountId,
    string Username,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    decimal HourlyRate,
    decimal UsageCost,
    decimal RemainingBalance,
    string Status,
    string? EndReason);

public sealed record SessionResult<T>(
    bool IsSuccess,
    string? ErrorCode,
    string Message,
    T? Value)
{
    public static SessionResult<T> Success(T value, string message) =>
        new(true, null, message, value);

    public static SessionResult<T> Failure(string errorCode, string message) =>
        new(false, errorCode, message, default);
}
