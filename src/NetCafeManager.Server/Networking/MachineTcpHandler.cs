using System.Text.Json;
using NetCafeManager.Server.Machines;
using NetCafeManager.Shared.Administration;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Networking;

public sealed class MachineTcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly MachineHandshakeService _handshakes;
    private readonly MachineHeartbeatService _heartbeats;
    private readonly SessionTcpHandler? _sessions;
    private readonly RemoteCommandService? _remoteCommands;

    public MachineTcpHandler(
        MachineHandshakeService handshakes,
        MachineHeartbeatService heartbeats,
        SessionTcpHandler? sessions = null,
        RemoteCommandService? remoteCommands = null)
    {
        _handshakes = handshakes
            ?? throw new ArgumentNullException(nameof(handshakes));
        _heartbeats = heartbeats
            ?? throw new ArgumentNullException(nameof(heartbeats));
        _sessions = sessions;
        _remoteCommands = remoteCommands;
    }

    public async Task<NetworkMessage?> HandleAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        string? remoteIpAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return request.Type switch
        {
            NetworkMessageTypes.MachineHandshakeBegin =>
                await BeginAsync(request, context, cancellationToken),
            NetworkMessageTypes.MachineHandshakeComplete =>
                await CompleteAsync(
                    request,
                    context,
                    remoteIpAddress,
                    cancellationToken),
            NetworkMessageTypes.MachineHeartbeat =>
                await HeartbeatAsync(request, context, cancellationToken),
            NetworkMessageTypes.MachineRemoteCommandResult =>
                await RemoteCommandResultAsync(request, context, cancellationToken),
            _ => null
        };
    }

    private async Task<NetworkMessage> BeginAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedMachine is not null)
        {
            return NetworkResponseFactory.Error(
                request,
                "MACHINE_ALREADY_VERIFIED",
                "Máy trạm đã được xác thực trên kết nối này.");
        }

        MachineHandshakeBeginRequest? payload;
        try
        {
            payload = request.Data.Deserialize<MachineHandshakeBeginRequest>(
                JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidRequest(request);
        }

        if (payload is null)
        {
            return InvalidRequest(request);
        }

        SessionResult<MachineHandshakeChallenge> result =
            await _handshakes.BeginAsync(
                payload.MachineName,
                cancellationToken);
        return NetworkResponseFactory.Result(request, result);
    }

    private async Task<NetworkMessage> CompleteAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        string? remoteIpAddress,
        CancellationToken cancellationToken)
    {
        MachineHandshakeCompleteRequest? payload;
        try
        {
            payload = request.Data.Deserialize<MachineHandshakeCompleteRequest>(
                JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidRequest(request);
        }

        if (payload is null || payload.ChallengeId == Guid.Empty)
        {
            return InvalidRequest(request);
        }

        SessionResult<VerifiedMachine> result =
            await _handshakes.CompleteAsync(
                payload.ChallengeId,
                payload.ProofBase64,
                remoteIpAddress,
                cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            context.SetVerifiedMachine(
                result.Value,
                result.Value.VerifiedAt);
        }

        return NetworkResponseFactory.Result(request, result);
    }

    private async Task<NetworkMessage> HeartbeatAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedMachine is null)
        {
            return NetworkResponseFactory.Error(
                request,
                "HEARTBEAT_MACHINE_REQUIRED",
                "May tram phai hoan tat handshake truoc khi gui heartbeat.");
        }

        MachineHeartbeatRequest? payload;
        try
        {
            payload = request.Data.Deserialize<MachineHeartbeatRequest>(
                JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidRequest(request);
        }

        if (payload is null)
        {
            return InvalidRequest(request);
        }

        SessionResult<MachineHeartbeatAck> result =
            await _heartbeats.RecordAsync(
                context.VerifiedMachine,
                cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            context.MarkHeartbeat(result.Value.ReceivedAt);
            if (_sessions is not null)
            {
                SessionBalanceStatus? balance =
                    await _sessions.EvaluateBalanceOnHeartbeatAsync(
                        context,
                        cancellationToken);
                if (balance is not null)
                {
                    result = SessionResult<MachineHeartbeatAck>.Success(
                        result.Value with { BalanceStatus = balance },
                        result.Message);
                }
            }

            if (_remoteCommands is not null)
            {
                PendingRemoteCommand? command =
                    await _remoteCommands.TakePendingAsync(
                        context.VerifiedMachine,
                        cancellationToken);
                result = SessionResult<MachineHeartbeatAck>.Success(
                    result.Value! with { PendingCommand = command },
                    result.Message);
            }
        }

        return NetworkResponseFactory.Result(request, result);
    }

    private async Task<NetworkMessage> RemoteCommandResultAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedMachine is null || _remoteCommands is null)
        {
            return NetworkResponseFactory.Error(
                request,
                "COMMAND_MACHINE_REQUIRED",
                "Máy trạm phải hoàn tất handshake trước khi trả kết quả lệnh.");
        }

        RemoteCommandResultRequest? payload;
        try
        {
            payload = request.Data.Deserialize<RemoteCommandResultRequest>(
                JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidRequest(request);
        }

        if (payload is null)
        {
            return InvalidRequest(request);
        }

        SessionResult<RemoteCommandSummary> result =
            await _remoteCommands.CompleteAsync(
                context.VerifiedMachine,
                payload,
                cancellationToken);
        return NetworkResponseFactory.Result(request, result);
    }

    public Task MarkDisconnectedAsync(
        TcpConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.VerifiedMachine is null
            ? Task.CompletedTask
            : _heartbeats.MarkDisconnectedAsync(
                context.VerifiedMachine,
                cancellationToken);
    }

    private static NetworkMessage InvalidRequest(NetworkMessage request) =>
        NetworkResponseFactory.Error(
            request,
            "MACHINE_INVALID_REQUEST",
            "Dữ liệu xác thực máy trạm không hợp lệ.");
}
