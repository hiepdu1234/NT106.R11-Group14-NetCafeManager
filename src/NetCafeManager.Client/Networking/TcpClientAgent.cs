
using System.Net.Sockets;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Client.Networking;

internal sealed class TcpClientAgent : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;

    private readonly SemaphoreSlim _requestLock = new(1, 1);

    // Ket noi den TCP Server
    public async Task ConnectAsync(
        string serverIp,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (_client != null)
        {
            throw new InvalidOperationException(
                "Client da duoc ket noi.");
        }

        TcpClient client = new TcpClient();

        try
        {
            await client.ConnectAsync(
                serverIp,
                port,
                cancellationToken);

            _stream = client.GetStream();
            _client = client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    // Gui yeu cau va nhan phan hoi
    public async Task<NetworkMessage> SendRequestAsync(
        NetworkMessage request,
        CancellationToken cancellationToken = default)
    {
        if (_stream == null)
        {
            throw new InvalidOperationException(
                "Client chua ket noi Server.");
        }

        await _requestLock.WaitAsync(cancellationToken);

        try
        {
            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            // Timeout tam thoi cho bai kiem thu
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            await FrameProtocol.SendAsync(
                _stream,
                request,
                timeout.Token);

            NetworkMessage? response =
                await FrameProtocol.ReceiveAsync(
                    _stream,
                    timeout.Token);

            if (response == null)
            {
                throw new EndOfStreamException(
                    "Server da dong ket noi.");
            }

            if (response.ReplyTo != request.Id)
            {
                throw new InvalidDataException(
                    "Response khong khop RequestId.");
            }

            return response;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    // Giai phong ket noi
    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _requestLock.Dispose();
    }
}

