
using System.Text.Json;
using NetCafeManager.Server.Networking;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Server.Authentication;

public sealed class AuthTcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly AuthenticationService _authentication;

    public AuthTcpHandler(AuthenticationService authentication)
    {
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

        if (request.Type == NetworkMessageTypes.AuthLogout)
        {
            bool revoked = context.SessionToken is not null &&
                           _authentication.Logout(context.SessionToken);
            context.ClearAuthentication();
            return NetworkResponseFactory.Result(request, new
            {
                isSuccess = revoked,
                message = revoked
                    ? "Đăng xuất thành công."
                    : "Phiên đăng nhập không còn hiệu lực."
            });
        }

        if (request.Type != NetworkMessageTypes.AuthLogin)
        {
            return null;
        }

        if (context.ActiveUsageSession is not null)
        {
            return NetworkResponseFactory.Error(
                request,
                "AUTH_ACTIVE_USAGE_SESSION",
                "Hãy kết thúc phiên sử dụng trước khi đăng nhập lại.");
        }

        LoginRequest? loginRequest;

        try
        {
            loginRequest = request.Data.Deserialize<LoginRequest>(
                JsonOptions);
        }
        catch (JsonException)
        {
            return CreateInvalidRequestResponse(request);
        }

        if (loginRequest is null)
        {
            return CreateInvalidRequestResponse(request);
        }

        LoginResponse result = await _authentication.LoginAsync(
            loginRequest,
            cancellationToken);

        if (result.IsSuccess &&
            result.User is not null &&
            result.SessionToken is not null)
        {
            if (context.SessionToken is not null)
            {
                _authentication.Logout(context.SessionToken);
            }

            context.User = result.User;
            context.SessionToken = result.SessionToken;
        }

        return NetworkResponseFactory.Result(request, result);
    }

    // Giữ overload cũ cho các bài kiểm tra chỉ kiểm tra AUTH_LOGIN.
    public Task<NetworkMessage?> HandleAsync(
        NetworkMessage request,
        CancellationToken cancellationToken = default) =>
        HandleAsync(request, new TcpConnectionContext(), cancellationToken);

    private static NetworkMessage CreateInvalidRequestResponse(
        NetworkMessage request)
    {
        return NetworkResponseFactory.Error(
            request,
            "AUTH_INVALID_REQUEST",
            "Dữ liệu đăng nhập không hợp lệ.");
    }
}

