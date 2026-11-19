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
    int NextHeartbeatAfterSeconds);
