using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Client.Networking;

internal sealed class TcpClientAgent : IAsyncDisposable
{
    private TcpClient? _client;
    private Stream? _stream;

    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly object _disposeSync = new();
    private CancellationTokenSource? _heartbeatCancellation;
    private Task? _heartbeatTask;
    private Task? _disposeTask;
    private int _heartbeatStarted;
    private int _disposeStarted;

    public event Action<TcpClientAgent, Exception>? HeartbeatFailed;
    public event Action<TcpClientAgent, SessionBalanceStatus>? BalanceStatusChanged;
    public event Action<TcpClientAgent, MachineHeartbeatAck>? HeartbeatReceived;

    public bool IsDisposed => Volatile.Read(ref _disposeStarted) == 1;

    public async Task ConnectAsync(
        string serverIp,
        int port,
        CancellationToken cancellationToken = default,
        bool useTls = false,
        string? expectedCertificateSha256 = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        TcpClient client = new();
        if (Interlocked.CompareExchange(ref _client, client, null) is not null)
        {
            client.Dispose();
            throw new InvalidOperationException("Client đã có kết nối TCP.");
        }

        try
        {
            await client.ConnectAsync(serverIp, port, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Stream stream = client.GetStream();
            if (useTls)
            {
                string? expected = NormalizeThumbprint(expectedCertificateSha256);
                var secureStream = new SslStream(
                    stream,
                    leaveInnerStreamOpen: false,
                    (_, certificate, _, policyErrors) =>
                        ValidateServerCertificate(
                            certificate,
                            policyErrors,
                            expected));
                await secureStream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = serverIp,
                        EnabledSslProtocols =
                            SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode =
                            X509RevocationMode.NoCheck
                    },
                    cancellationToken).ConfigureAwait(false);
                stream = secureStream;
            }
            _stream = stream;
        }
        catch
        {
            Interlocked.CompareExchange(ref _client, null, client);
            client.Dispose();
            throw;
        }
    }

    public async Task<NetworkMessage> SendRequestAsync(
        NetworkMessage request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Stream stream = _stream
                ?? throw new InvalidOperationException("Client chưa kết nối Server.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            await FrameProtocol.SendAsync(stream, request, timeout.Token)
                .ConfigureAwait(false);
            NetworkMessage? response = await FrameProtocol.ReceiveAsync(
                stream,
                timeout.Token).ConfigureAwait(false);

            if (response is null)
            {
                throw new EndOfStreamException("Server đã đóng kết nối.");
            }

            if (response.ReplyTo != request.Id)
            {
                throw new InvalidDataException("Response không khớp RequestId.");
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_stream is null)
        {
            throw new InvalidOperationException("Client chưa kết nối Server.");
        }

        if (Interlocked.CompareExchange(ref _heartbeatStarted, 1, 0) != 0)
        {
            throw new InvalidOperationException("Heartbeat đã được khởi động.");
        }

        var cancellation = new CancellationTokenSource();
        _heartbeatCancellation = cancellation;
        _heartbeatTask = RunHeartbeatAsync(
            machineName.Trim(),
            cancellation.Token);
    }

    private async Task RunHeartbeatAsync(
        string machineName,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(HeartbeatDefaults.Interval, cancellationToken)
                    .ConfigureAwait(false);

                var request = new NetworkMessage(
                    Guid.NewGuid().ToString("N"),
                    NetworkMessageTypes.MachineHeartbeat,
                    machineName,
                    null,
                    JsonSerializer.SerializeToElement(
                        new MachineHeartbeatRequest(DateTimeOffset.UtcNow)));

                NetworkMessage response = await SendRequestAsync(
                    request,
                    cancellationToken).ConfigureAwait(false);
                SessionResult<MachineHeartbeatAck>? result =
                    response.Data.Deserialize<SessionResult<MachineHeartbeatAck>>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (response.Type != NetworkMessageTypes.CommandResult ||
                    result?.IsSuccess != true)
                {
                    throw new InvalidDataException(
                        result?.Message ?? "Server từ chối heartbeat.");
                }

                if (result.Value is { } acknowledgement)
                {
                    HeartbeatReceived?.Invoke(this, acknowledgement);
                    if (acknowledgement.BalanceStatus is { } balanceStatus)
                    {
                        BalanceStatusChanged?.Invoke(this, balanceStatus);
                    }
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Heartbeat được hủy khi kết nối bị thay thế hoặc ứng dụng đóng.
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Socket có thể đóng đồng thời với thao tác hủy heartbeat.
        }
        catch (Exception exception)
        {
            try
            {
                HeartbeatFailed?.Invoke(this, exception);
            }
            catch
            {
                // Lỗi của callback UI không được làm lỗi tác vụ heartbeat.
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private static bool ValidateServerCertificate(
        X509Certificate? certificate,
        SslPolicyErrors policyErrors,
        string? expectedSha256)
    {
        if (certificate is null)
        {
            return false;
        }

        if (expectedSha256 is null)
        {
            return policyErrors == SslPolicyErrors.None;
        }

        using var certificate2 = new X509Certificate2(certificate);
        string actual = NormalizeThumbprint(
            certificate2.GetCertHashString(HashAlgorithmName.SHA256))!;
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(expectedSha256));
    }

    private static string? NormalizeThumbprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = string.Concat(
            value.Where(Uri.IsHexDigit)).ToUpperInvariant();
        if (normalized.Length != 64)
        {
            throw new InvalidOperationException(
                "Dấu vân tay chứng thư phải là SHA-256 gồm 64 ký tự hex.");
        }

        return normalized;
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposeStarted, 1);

        CancellationTokenSource? heartbeatCancellation =
            Interlocked.Exchange(ref _heartbeatCancellation, null);
        Task? heartbeatTask = Interlocked.Exchange(ref _heartbeatTask, null);

        try
        {
            heartbeatCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Một luồng khác đã hoàn tất việc giải phóng.
        }

        // Đóng socket để giải phóng cả request đang chờ đọc/ghi.
        _stream?.Dispose();
        _client?.Dispose();

        if (heartbeatTask is not null)
        {
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Đã hủy heartbeat có chủ đích.
            }
        }

        // Chờ mọi request đang chạy thoát khỏi vùng đọc/ghi trước khi kết thúc.
        await _requestLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _stream = null;
            _client = null;
        }
        finally
        {
            _requestLock.Release();
        }

        heartbeatCancellation?.Dispose();
    }
}
