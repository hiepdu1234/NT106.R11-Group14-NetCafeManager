using System.Text.Json;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Server.Networking;

public sealed class PasswordResetTcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly PasswordResetService _passwordReset;

    public PasswordResetTcpHandler(PasswordResetService passwordReset)
    {
        _passwordReset = passwordReset
            ?? throw new ArgumentNullException(nameof(passwordReset));
    }

    public async Task<NetworkMessage?> HandleAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return request.Type switch
        {
            NetworkMessageTypes.AuthForgotPassword =>
                await HandleRequestAsync(request, cancellationToken),
            NetworkMessageTypes.AuthVerifyOtp =>
                await HandleVerifyAsync(request, cancellationToken),
            NetworkMessageTypes.AuthResetPassword =>
                context.ActiveUsageSession is not null
                    ? NetworkResponseFactory.Error(
                        request,
                        "AUTH_ACTIVE_USAGE_SESSION",
                        "Hãy kết thúc phiên sử dụng trước khi đặt lại mật khẩu.")
                    : await HandleCompleteAsync(request, cancellationToken),
            _ => null
        };
    }

    public Task<NetworkMessage?> HandleAsync(
        NetworkMessage request,
        CancellationToken cancellationToken = default) =>
        HandleAsync(request, new TcpConnectionContext(), cancellationToken);

    private async Task<NetworkMessage> HandleRequestAsync(
        NetworkMessage request,
        CancellationToken cancellationToken)
    {
        RequestPasswordResetRequest? payload =
            Deserialize<RequestPasswordResetRequest>(request);
        if (payload is null)
        {
            return InvalidRequest(request);
        }

        PasswordResetResponse result = await _passwordReset.RequestAsync(
            payload,
            cancellationToken);
        return NetworkResponseFactory.Result(request, result);
    }

    private async Task<NetworkMessage> HandleVerifyAsync(
        NetworkMessage request,
        CancellationToken cancellationToken)
    {
        VerifyPasswordResetOtpRequest? payload =
            Deserialize<VerifyPasswordResetOtpRequest>(request);
        if (payload is null)
        {
            return InvalidRequest(request);
        }

        PasswordResetResponse result = await _passwordReset.VerifyOtpAsync(
            payload,
            cancellationToken);
        return NetworkResponseFactory.Result(request, result);
    }

    private async Task<NetworkMessage> HandleCompleteAsync(
        NetworkMessage request,
        CancellationToken cancellationToken)
    {
        CompletePasswordResetRequest? payload =
            Deserialize<CompletePasswordResetRequest>(request);
        if (payload is null)
        {
            return InvalidRequest(request);
        }

        PasswordResetResponse result = await _passwordReset.CompleteAsync(
            payload,
            cancellationToken);
        return NetworkResponseFactory.Result(request, result);
    }

    private static T? Deserialize<T>(NetworkMessage request)
        where T : class
    {
        try
        {
            return request.Data.Deserialize<T>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NetworkMessage InvalidRequest(NetworkMessage request) =>
        NetworkResponseFactory.Error(
            request,
            "AUTH_INVALID_REQUEST",
            "Dữ liệu khôi phục mật khẩu không hợp lệ.");
}
