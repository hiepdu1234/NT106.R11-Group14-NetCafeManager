
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetCafeManager.Shared.Networking;

public sealed record NetworkMessage(
    [property: JsonPropertyName("id")]
    string Id,

    [property: JsonPropertyName("type")]
    string Type,

    [property: JsonPropertyName("machineId")]
    string? MachineId,

    [property: JsonPropertyName("replyTo")]
    string? ReplyTo,

    [property: JsonPropertyName("data")]
    JsonElement Data
);
