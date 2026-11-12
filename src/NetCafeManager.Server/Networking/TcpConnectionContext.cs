using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Networking;

public sealed class TcpConnectionContext
{
    public VerifiedMachine? VerifiedMachine { get; internal set; }

    public AuthenticatedUser? User { get; internal set; }

    public string? SessionToken { get; internal set; }

    public UsageSessionSummary? ActiveUsageSession { get; internal set; }

    internal void ClearAuthentication()
    {
        User = null;
        SessionToken = null;
        ActiveUsageSession = null;
    }
}
