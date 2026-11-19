namespace NetCafeManager.Server.Networking;

public static class HeartbeatTimeoutPolicy
{
    public static bool IsTimedOut(
        DateTimeOffset? lastHeartbeat,
        DateTimeOffset now,
        TimeSpan timeout)
    {
        if (lastHeartbeat is null)
        {
            return false;
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        return now - lastHeartbeat.Value > timeout;
    }
}
