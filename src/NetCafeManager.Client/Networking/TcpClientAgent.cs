
using System.Net.Sockets;
using System.Text.Json;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Client.Networking;

internal sealed class TcpClientAgent : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;

    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private CancellationTokenSource? _heartbeatCancellation;
    private Task? _heartbeatTask;

    public event Action<TcpClientAgent, Exception>? HeartbeatFailed;

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

    public void StartHeartbeat(string machineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        if (_stream is null)
        {
            throw new InvalidOperationException(
                "Client chua ket noi Server.");
        }

        if (_heartbeatTask is not null)
        {
            throw new InvalidOperationException(
                "Heartbeat da duoc khoi dong.");
        }

        _heartbeatCancellation = new CancellationTokenSource();
        _heartbeatTask = RunHeartbeatAsync(
            machineName.Trim(),
            _heartbeatCancellation.Token);
    }

    private async Task RunHeartbeatAsync(
        string machineName,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    HeartbeatDefaults.Interval,
                    cancellationToken);

                var request = new NetworkMessage(
                    Guid.NewGuid().ToString("N"),
                    NetworkMessageTypes.MachineHeartbeat,
                    machineName,
                    null,
                    JsonSerializer.SerializeToElement(
                        new MachineHeartbeatRequest(DateTimeOffset.UtcNow)));
                NetworkMessage response = await SendRequestAsync(
                    request,
                    cancellationToken);
                SessionResult<MachineHeartbeatAck>? result =
                    response.Data.Deserialize<SessionResult<MachineHeartbeatAck>>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (response.Type != NetworkMessageTypes.CommandResult ||
                    result?.IsSuccess != true)
                {
                    throw new InvalidDataException(
                        result?.Message ?? "Server tu choi heartbeat.");
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Heartbeat duoc dung khi dong hoac doi ket noi.
        }
        catch (Exception)
            when (cancellationToken.IsCancellationRequested)
        {
            // Socket co the dong dong thoi voi heartbeat.
        }
        catch (Exception exception)
        {
            HeartbeatFailed?.Invoke(this, exception);
        }
    }

    // Giai phong ket noi
    public void Dispose()
    {
        _heartbeatCancellation?.Cancel();
        _heartbeatCancellation?.Dispose();
        _heartbeatCancellation = null;
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }
}

