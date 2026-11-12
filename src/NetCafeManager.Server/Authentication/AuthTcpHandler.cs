
using System.Text.Json;
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
        CancellationToken cancellationToken = default)
    {
        if (request.Type != "AUTH_LOGIN")
        {
            return null;
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

        return new NetworkMessage(
            Guid.NewGuid().ToString("N"),
            "COMMAND_RESULT",
            request.MachineId,
            request.Id,
            JsonSerializer.SerializeToElement(result, JsonOptions));
    }

    private static NetworkMessage CreateInvalidRequestResponse(
        NetworkMessage request)
    {
        return new NetworkMessage(
            Guid.NewGuid().ToString("N"),
            "ERROR",
            request.MachineId,
            request.Id,
            JsonSerializer.SerializeToElement(new
            {
                code = "AUTH_INVALID_REQUEST",
                message = "Du lieu dang nhap khong hop le."
            }));
    }
}

