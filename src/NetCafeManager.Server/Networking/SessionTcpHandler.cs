using System.Text.Json;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Server.Sessions;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Networking;

public sealed class SessionTcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly UsageSessionService _sessions;
    private readonly AuthenticationService _authentication;

    public SessionTcpHandler(
        UsageSessionService sessions,
        AuthenticationService authentication)
    {
        _sessions = sessions
            ?? throw new ArgumentNullException(nameof(sessions));
        _authentication = authentication
            ?? throw new ArgumentNullException(nameof(authentication));
    }

    public async Task<NetworkMessage?> HandleAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Type is not (
            NetworkMessageTypes.SessionStart or
            NetworkMessageTypes.SessionGetActive or
            NetworkMessageTypes.SessionEnd))
        {
            return null;
        }

        if (!TryValidateAuthentication(context))
        {
            return NetworkResponseFactory.Error(
                request,
                "SESSION_AUTHENTICATION_REQUIRED",
                "Cần đăng nhập trước khi thao tác với phiên sử dụng.");
        }

        return request.Type switch
        {
            NetworkMessageTypes.SessionStart =>
                await StartAsync(request, context, cancellationToken),
            NetworkMessageTypes.SessionGetActive =>
                await GetActiveAsync(request, context, cancellationToken),
            NetworkMessageTypes.SessionEnd =>
                await EndAsync(request, context, cancellationToken),
            _ => null
        };
    }

    public async Task EndOnDisconnectAsync(
        TcpConnectionContext context,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User is null || context.ActiveUsageSession is null)
        {
            return;
        }

        try
        {
            await _sessions.EndAsync(
                context.User,
                context.ActiveUsageSession.SessionId,
                reason,
                cancellationToken);
        }
        finally
        {
            context.ActiveUsageSession = null;
        }
    }

    private async Task<NetworkMessage> StartAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedMachine is null)
        {
            return NetworkResponseFactory.Error(
                request,
                "SESSION_MACHINE_REQUIRED",
                "Máy trạm phải hoàn tất handshake trước khi mở phiên.");
        }

        if (context.ActiveUsageSession is not null)
        {
            return NetworkResponseFactory.Error(
                request,
                "SESSION_ALREADY_ACTIVE",
                "Kết nối này đã có phiên sử dụng đang hoạt động.");
        }

        SessionResult<UsageSessionSummary> result = await _sessions.StartAsync(
            context.User!,
            context.VerifiedMachine,
            cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            context.ActiveUsageSession = result.Value;
        }

        return NetworkResponseFactory.Result(request, result);
    }

    private async Task<NetworkMessage> GetActiveAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        UsageSessionSummary? active = await _sessions.GetActiveAsync(
            context.User!,
            cancellationToken);
        context.ActiveUsageSession = active;
        return NetworkResponseFactory.Result(request, active);
    }

    private async Task<NetworkMessage> EndAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        EndUsageSessionRequest? payload;
        try
        {
            payload = request.Data.Deserialize<EndUsageSessionRequest>(
                JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidRequest(request);
        }

        if (payload is null || payload.SessionId <= 0)
        {
            return InvalidRequest(request);
        }

        SessionResult<UsageSessionSummary> result = await _sessions.EndAsync(
            context.User!,
            payload.SessionId,
            payload.Reason,
            cancellationToken);
        if (result.IsSuccess &&
            context.ActiveUsageSession?.SessionId == payload.SessionId)
        {
            context.ActiveUsageSession = null;
        }

        return NetworkResponseFactory.Result(request, result);
    }

    private bool TryValidateAuthentication(TcpConnectionContext context)
    {
        if (context.User is null ||
            string.IsNullOrWhiteSpace(context.SessionToken) ||
            !_authentication.TryValidateSession(
                context.SessionToken,
                out UserSession? session) ||
            session?.UserId != context.User.Id)
        {
            return false;
        }

        return true;
    }

    private static NetworkMessage InvalidRequest(NetworkMessage request) =>
        NetworkResponseFactory.Error(
            request,
            "SESSION_INVALID_REQUEST",
            "Dữ liệu phiên sử dụng không hợp lệ.");
}
