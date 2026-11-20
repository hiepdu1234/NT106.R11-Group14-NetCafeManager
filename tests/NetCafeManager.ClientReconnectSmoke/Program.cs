using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using NetCafeManager.Client.Networking;
using NetCafeManager.Shared.Networking;

await using SmokeServer firstServer = await SmokeServer.StartAsync();
int port = firstServer.Port;

await using var originalClient = new TcpClientAgent();
await originalClient.ConnectAsync(IPAddress.Loopback.ToString(), port);
await RequireRoundTripAsync(originalClient, "before-restart");

await firstServer.StopAsync();
await RequireConnectionFailureAsync(
    originalClient,
    "Client cu phai nhan biet socket da bi dong khi Server dung.");

await using var reconnectingClient = new TcpClientAgent();
Task reconnectTask = ConnectWithRetryAsync(
    reconnectingClient,
    IPAddress.Loopback.ToString(),
    port,
    attempts: 20,
    retryDelay: TimeSpan.FromMilliseconds(100));

// Bao dam Client da trai qua it nhat mot lan ket noi that bai truoc khi
// Server quay lai tren cung cong.
await Task.Delay(300);
await using SmokeServer restartedServer = await SmokeServer.StartAsync(port);

await reconnectTask.WaitAsync(TimeSpan.FromSeconds(5));
await RequireRoundTripAsync(reconnectingClient, "after-restart");

using X509Certificate2 certificate = CreateServerCertificate();
await using SmokeServer tlsServer = await SmokeServer.StartAsync(
    certificate: certificate);
await using var tlsClient = new TcpClientAgent();
await tlsClient.ConnectAsync(
    IPAddress.Loopback.ToString(),
    tlsServer.Port,
    useTls: true,
    expectedCertificateSha256:
        certificate.GetCertHashString(HashAlgorithmName.SHA256));
await RequireRoundTripAsync(tlsClient, "tls-round-trip");

await using var rejectedTlsClient = new TcpClientAgent();
await RequireTlsRejectionAsync(rejectedTlsClient, tlsServer.Port);

Console.WriteLine(
    "Client reconnect smoke passed: phat hien mat ket noi, retry khi Server " +
    "dang dung, gui request sau khi Server khoi dong lai va xac minh TLS SHA-256.");

static X509Certificate2 CreateServerCertificate()
{
    using RSA rsa = RSA.Create(2048);
    var request = new CertificateRequest(
        "CN=localhost",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    var names = new SubjectAlternativeNameBuilder();
    names.AddDnsName("localhost");
    names.AddIpAddress(IPAddress.Loopback);
    request.CertificateExtensions.Add(names.Build());
    request.CertificateExtensions.Add(
        new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature |
            X509KeyUsageFlags.KeyEncipherment,
            critical: true));
    return request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddMinutes(-5),
        DateTimeOffset.UtcNow.AddDays(1));
}

static async Task RequireTlsRejectionAsync(
    TcpClientAgent client,
    int port)
{
    try
    {
        await client.ConnectAsync(
            IPAddress.Loopback.ToString(),
            port,
            useTls: true,
            expectedCertificateSha256: new string('0', 64));
    }
    catch (AuthenticationException)
    {
        return;
    }

    throw new InvalidOperationException(
        "Client phai tu choi Server co SHA-256 khong khop.");
}

static async Task ConnectWithRetryAsync(
    TcpClientAgent client,
    string host,
    int port,
    int attempts,
    TimeSpan retryDelay)
{
    Exception? lastError = null;
    for (int attempt = 1; attempt <= attempts; attempt++)
    {
        try
        {
            await client.ConnectAsync(host, port);
            return;
        }
        catch (Exception exception) when (
            exception is SocketException or IOException)
        {
            lastError = exception;
            if (attempt < attempts)
            {
                await Task.Delay(retryDelay);
            }
        }
    }

    throw new InvalidOperationException(
        $"Client khong ket noi lai sau {attempts} lan thu.",
        lastError);
}

static async Task RequireRoundTripAsync(
    TcpClientAgent client,
    string requestId)
{
    var request = new NetworkMessage(
        requestId,
        "RECONNECT_SMOKE",
        "CLIENT-RECONNECT-SMOKE",
        null,
        JsonSerializer.SerializeToElement(new { requestId }));

    NetworkMessage response = await client.SendRequestAsync(request)
        .WaitAsync(TimeSpan.FromSeconds(5));
    Require(
        response.Type == NetworkMessageTypes.CommandResult &&
        response.ReplyTo == requestId,
        "Client khong nhan duoc response dung sau khi ket noi.");
}

static async Task RequireConnectionFailureAsync(
    TcpClientAgent client,
    string failureMessage)
{
    try
    {
        await RequireRoundTripAsync(client, "server-is-stopped");
    }
    catch (Exception exception) when (
        exception is IOException or SocketException or ObjectDisposedException or
        InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException(failureMessage);
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class SmokeServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly X509Certificate2? _certificate;
    private readonly Task _runTask;
    private int _stopped;

    private SmokeServer(
        TcpListener listener,
        X509Certificate2? certificate)
    {
        _listener = listener;
        _certificate = certificate;
        _runTask = RunAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static Task<SmokeServer> StartAsync(
        int port = 0,
        X509Certificate2? certificate = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return Task.FromResult(new SmokeServer(listener, certificate));
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(
                    _lifetime.Token);
                _clients.TryAdd(client, 0);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException)
            when (_lifetime.IsCancellationRequested)
        {
        }
        catch (SocketException)
            when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                Stream stream = client.GetStream();
                if (_certificate is not null)
                {
                    var secureStream = new SslStream(stream, false);
                    await secureStream.AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions
                        {
                            ServerCertificate = _certificate,
                            EnabledSslProtocols =
                                SslProtocols.Tls12 | SslProtocols.Tls13,
                            ClientCertificateRequired = false,
                            CertificateRevocationCheckMode =
                                X509RevocationMode.NoCheck
                        });
                    stream = secureStream;
                }

                using (stream)
                {
                while (!_lifetime.IsCancellationRequested)
                {
                    NetworkMessage? request = await FrameProtocol.ReceiveAsync(
                        stream,
                        _lifetime.Token);
                    if (request is null)
                    {
                        return;
                    }

                    var response = new NetworkMessage(
                        Guid.NewGuid().ToString("N"),
                        NetworkMessageTypes.CommandResult,
                        "SMOKE-SERVER",
                        request.Id,
                        JsonSerializer.SerializeToElement(new { accepted = true }));
                    await FrameProtocol.SendAsync(
                        stream,
                        response,
                        _lifetime.Token);
                }
                }
            }
        }
        catch (Exception) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (AuthenticationException)
        {
        }
        finally
        {
            _clients.TryRemove(client, out _);
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _listener.Stop();
        foreach (TcpClient client in _clients.Keys)
        {
            client.Dispose();
        }

        await _runTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifetime.Dispose();
    }
}

