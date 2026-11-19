using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Networking;

public sealed class TcpConnectionContext
{
    private long _lastHeartbeatUtcTicks;
    private int _heartbeatTimedOut;

    public VerifiedMachine? VerifiedMachine { get; internal set; }

    public DateTimeOffset? LastHeartbeatReceivedAt
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastHeartbeatUtcTicks);
            return ticks == 0
                ? null
                : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public bool HeartbeatTimedOut
    {
        get => Volatile.Read(ref _heartbeatTimedOut) == 1;
        internal set => Volatile.Write(
            ref _heartbeatTimedOut,
            value ? 1 : 0);
    }

    public AuthenticatedUser? User { get; internal set; }

    public string? SessionToken { get; internal set; }

    public UsageSessionSummary? ActiveUsageSession { get; internal set; }

    internal void SetVerifiedMachine(
        VerifiedMachine machine,
        DateTimeOffset verifiedAt)
    {
        VerifiedMachine = machine;
        Interlocked.Exchange(
            ref _lastHeartbeatUtcTicks,
            verifiedAt.UtcDateTime.Ticks);
        HeartbeatTimedOut = false;
    }

    internal void MarkHeartbeat(DateTimeOffset receivedAt)
    {
        Interlocked.Exchange(
            ref _lastHeartbeatUtcTicks,
            receivedAt.UtcDateTime.Ticks);
        HeartbeatTimedOut = false;
    }

    internal void ClearAuthentication()
    {
        User = null;
        SessionToken = null;
        ActiveUsageSession = null;
    }
}
