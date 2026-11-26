namespace NetCafeManager.Shared.Sessions;

public static class HeartbeatDefaults
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
}

public sealed record MachineHeartbeatRequest(DateTimeOffset SentAt);

public sealed record MachineHeartbeatAck(
    int ComputerId,
    DateTimeOffset ReceivedAt,
    int NextHeartbeatAfterSeconds,
    SessionBalanceStatus? BalanceStatus = null,
    Administration.PendingRemoteCommand? PendingCommand = null,
    string? ComputerStatus = null);

public sealed record SessionBalanceStatus(
    bool HasActiveSession,
    bool ShouldWarn,
    bool SessionEnded,
    decimal RemainingBalance,
    TimeSpan EstimatedRemaining,
    string Message);
