using System.Text.Json;
using NetCafeManager.Server.Machines;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Networking;

public sealed class MachineTcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly MachineHandshakeService _handshakes;

    public MachineTcpHandler(MachineHandshakeService handshakes)
    {
        _handshakes = handshakes
            ?? throw new ArgumentNullException(nameof(handshakes));
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
            context.VerifiedMachine = result.Value;
        }

        return NetworkResponseFactory.Result(request, result);
    }

    private static NetworkMessage InvalidRequest(NetworkMessage request) =>
        NetworkResponseFactory.Error(
            request,
            "MACHINE_INVALID_REQUEST",
            "Dữ liệu xác thực máy trạm không hợp lệ.");
}
