using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NetCafeManager.Server.Networking;
using NetCafeManager.Shared.Networking;

const int clientCount = 24;
const int messagesPerClient = 4;

RunningServer server = await StartServerAsync();
try
{
    await TestManyClientsAsync(
        server.Endpoint,
        clientCount,
        messagesPerClient);
    await TestAbruptDisconnectAsync(server.Endpoint);
    await TestMalformedFramesAsync(server.Endpoint);

    int restartPort = server.Endpoint.Port;
    await StopServerAsync(server);
    server = await StartServerAsync(restartPort);
    await SendUnsupportedCommandAsync(server.Endpoint, "restart-check");
}
finally
{
    await StopServerAsync(server);
}

await TestHeartbeatTimeoutClosesConnectionAsync();

Console.WriteLine(
    $"Network resilience smoke passed: {clientCount} clients, " +
    $"{clientCount * messagesPerClient} framed requests, abrupt close, " +
    "malformed frames, server restart and heartbeat timeout.");

static async Task TestManyClientsAsync(
    IPEndPoint endpoint,
    int clients,
    int messagesPerClient)
{
    var startGate = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);

    Task[] tasks = Enumerable.Range(0, clients)
        .Select(clientNumber => Task.Run(async () =>
        {
            await startGate.Task;
            using var client = new TcpClient();
            await client.ConnectAsync(
                endpoint.Address,
                endpoint.Port).WaitAsync(TimeSpan.FromSeconds(5));
            using NetworkStream stream = client.GetStream();

            for (int sequence = 0; sequence < messagesPerClient; sequence++)
            {
                string requestId = $"client-{clientNumber}-message-{sequence}";
                await SendUnsupportedCommandOnStreamAsync(
                    stream,
                    requestId,
                    $"CLIENT-{clientNumber}");
            }
        }))
        .ToArray();

    startGate.SetResult();
    await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(20));
}

static async Task TestAbruptDisconnectAsync(IPEndPoint endpoint)
{
    var client = new TcpClient();
    await client.ConnectAsync(
        endpoint.Address,
        endpoint.Port).WaitAsync(TimeSpan.FromSeconds(5));
    client.Client.LingerState = new LingerOption(true, 0);
    client.Dispose();

    await Task.Delay(150);
    await SendUnsupportedCommandAsync(endpoint, "after-abrupt-disconnect");
}

static async Task TestMalformedFramesAsync(IPEndPoint endpoint)
{
    await SendMalformedFrameAsync(
        endpoint,
        headerLength: 64 * 1024 + 1,
        payload: []);
    await SendUnsupportedCommandAsync(endpoint, "after-oversized-frame");

    byte[] invalidJson = Encoding.UTF8.GetBytes("abcde");
    await SendMalformedFrameAsync(
        endpoint,
        invalidJson.Length,
        invalidJson);
    await SendUnsupportedCommandAsync(endpoint, "after-invalid-json");
}

static async Task SendMalformedFrameAsync(
    IPEndPoint endpoint,
    int headerLength,
    byte[] payload)
{
    using var client = new TcpClient();
    await client.ConnectAsync(
        endpoint.Address,
        endpoint.Port).WaitAsync(TimeSpan.FromSeconds(5));
    using NetworkStream stream = client.GetStream();

    byte[] header = new byte[4];
    BinaryPrimitives.WriteInt32BigEndian(header, headerLength);
    await stream.WriteAsync(header);
    if (payload.Length > 0)
    {
        await stream.WriteAsync(payload);
    }

    await stream.FlushAsync();
    await RequireRemoteDisconnectAsync(
        stream,
        "Server phai dong rieng ket noi gui frame loi.");
}

static async Task TestHeartbeatTimeoutClosesConnectionAsync()
{
    TcpConnectionContext? timeoutContext = null;
    RunningServer timeoutServer = await StartServerAsync(
        heartbeatTimeout: TimeSpan.FromMilliseconds(250),
        heartbeatCheckInterval: TimeSpan.FromMilliseconds(25),
        contextFactory: () =>
        {
            var context = new TcpConnectionContext();
            context.MarkHeartbeat(
                DateTimeOffset.UtcNow.Subtract(TimeSpan.FromSeconds(1)));
            timeoutContext = context;
            return context;
        });

    try
    {
        using var client = new TcpClient();
        await client.ConnectAsync(
            timeoutServer.Endpoint.Address,
            timeoutServer.Endpoint.Port).WaitAsync(TimeSpan.FromSeconds(5));
        using NetworkStream stream = client.GetStream();

        await WaitUntilAsync(
            () => timeoutContext is not null,
            TimeSpan.FromSeconds(2),
            "Server khong tao context cho ket noi timeout.");
        await RequireRemoteDisconnectAsync(
            stream,
            "Server phai ngat socket khi heartbeat qua timeout.");

        Require(
            timeoutContext!.HeartbeatTimedOut,
            "Context phai duoc danh dau HeartbeatTimedOut.");
    }
    finally
    {
        await StopServerAsync(timeoutServer);
    }
}

static async Task SendUnsupportedCommandAsync(
    IPEndPoint endpoint,
    string requestId)
{
    using var client = new TcpClient();
    await client.ConnectAsync(
        endpoint.Address,
        endpoint.Port).WaitAsync(TimeSpan.FromSeconds(5));
    using NetworkStream stream = client.GetStream();
    await SendUnsupportedCommandOnStreamAsync(
        stream,
        requestId,
        "SMOKE-CLIENT");
}

static async Task SendUnsupportedCommandOnStreamAsync(
    NetworkStream stream,
    string requestId,
    string machineId)
{
    var request = new NetworkMessage(
        requestId,
        "SMOKE_UNSUPPORTED",
        machineId,
        null,
        JsonSerializer.SerializeToElement(new { requestId }));
    await FrameProtocol.SendAsync(stream, request);
    NetworkMessage response =
        await FrameProtocol.ReceiveAsync(stream)
            .WaitAsync(TimeSpan.FromSeconds(5))
        ?? throw new InvalidOperationException(
            "Server khong tra phan hoi.");
    Require(
        response.Type == NetworkMessageTypes.Error,
        "Lenh smoke khong duoc tra ve ERROR.");
    Require(
        response.ReplyTo == requestId,
        "Phan hoi khong dung request ID.");
    Require(
        response.Data.TryGetProperty("code", out JsonElement code) &&
        code.GetString() == "E001",
        "Server khong tra ma E001 cho lenh chua ho tro.");
}

static async Task RequireRemoteDisconnectAsync(
    NetworkStream stream,
    string failureMessage)
{
    byte[] buffer = new byte[1];
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    try
    {
        int bytesRead = await stream.ReadAsync(buffer, timeout.Token);
        Require(bytesRead == 0, failureMessage);
    }
    catch (IOException)
    {
        // Connection reset is an expected disconnect outcome.
    }
    catch (SocketException)
    {
        // Connection reset is an expected disconnect outcome.
    }
    catch (OperationCanceledException)
        when (timeout.IsCancellationRequested)
    {
        throw new InvalidOperationException(failureMessage);
    }
}

static async Task<RunningServer> StartServerAsync(
    int port = 0,
    TimeSpan? heartbeatTimeout = null,
    TimeSpan? heartbeatCheckInterval = null,
    Func<TcpConnectionContext>? contextFactory = null)
{
    var server = new TcpServer(
        IPAddress.Loopback,
        port,
        heartbeatTimeout: heartbeatTimeout,
        heartbeatCheckInterval: heartbeatCheckInterval,
        connectionContextFactory: contextFactory);
    var cancellation = new CancellationTokenSource();
    Task runTask = server.StartAsync(cancellation.Token);

    await WaitUntilAsync(
        () => server.BoundEndpoint is not null,
        TimeSpan.FromSeconds(5),
        "TCP Server khong khoi dong dung han.");

    return new RunningServer(
        server,
        cancellation,
        runTask,
        server.BoundEndpoint!);
}

static async Task StopServerAsync(RunningServer server)
{
    if (!server.Cancellation.IsCancellationRequested)
    {
        server.Cancellation.Cancel();
    }

    await server.RunTask.WaitAsync(TimeSpan.FromSeconds(5));
    server.Cancellation.Dispose();
}

static async Task WaitUntilAsync(
    Func<bool> condition,
    TimeSpan timeout,
    string failureMessage)
{
    DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
    while (!condition())
    {
        if (DateTimeOffset.UtcNow >= deadline)
        {
            throw new InvalidOperationException(failureMessage);
        }

        await Task.Delay(20);
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record RunningServer(
    TcpServer Server,
    CancellationTokenSource Cancellation,
    Task RunTask,
    IPEndPoint Endpoint);
