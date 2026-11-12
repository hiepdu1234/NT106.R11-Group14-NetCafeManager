
using System.Buffers.Binary;
using System.Text.Json;

namespace NetCafeManager.Shared.Networking;

public static class FrameProtocol
{
    private const int MaxPayloadSize = 64 * 1024;

    public static async Task SendAsync(
        Stream stream,
        NetworkMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        byte[] payload =
            JsonSerializer.SerializeToUtf8Bytes(message);

        if (payload.Length == 0 ||
            payload.Length > MaxPayloadSize)
        {
            throw new InvalidDataException(
                "Kich thuoc thong diep khong hop le.");
        }

        byte[] header = new byte[4];

        BinaryPrimitives.WriteInt32BigEndian(
            header, payload.Length);

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<NetworkMessage?> ReceiveAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[4];

        int firstRead = await stream.ReadAsync(
            header.AsMemory(), cancellationToken);

        if (firstRead == 0)
            return null;

        await stream.ReadExactlyAsync(
            header.AsMemory(firstRead),
            cancellationToken);

        int payloadLength =
            BinaryPrimitives.ReadInt32BigEndian(header);

        if (payloadLength <= 0 ||
            payloadLength > MaxPayloadSize)
        {
            throw new InvalidDataException(
                "Length-Prefix khong hop le.");
        }

        byte[] payload = new byte[payloadLength];

        await stream.ReadExactlyAsync(
            payload, cancellationToken);

        NetworkMessage? message =
            JsonSerializer.Deserialize<NetworkMessage>(payload);

        return message ?? throw new InvalidDataException(
            "Thong diep JSON khong hop le.");
    }
}

