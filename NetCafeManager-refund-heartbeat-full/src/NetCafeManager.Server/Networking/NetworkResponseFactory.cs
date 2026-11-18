using System.Text.Json;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Server.Networking;

internal static class NetworkResponseFactory
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static NetworkMessage Result<T>(
        NetworkMessage request,
        T result) =>
        new(
            Guid.NewGuid().ToString("N"),
            NetworkMessageTypes.CommandResult,
            request.MachineId,
            request.Id,
            JsonSerializer.SerializeToElement(result, JsonOptions));

    public static NetworkMessage Error(
        NetworkMessage request,
        string errorCode,
        string errorMessage) =>
        new(
            Guid.NewGuid().ToString("N"),
            NetworkMessageTypes.Error,
            request.MachineId,
            request.Id,
            JsonSerializer.SerializeToElement(new
            {
                code = errorCode,
                message = errorMessage
            }, JsonOptions));
}
