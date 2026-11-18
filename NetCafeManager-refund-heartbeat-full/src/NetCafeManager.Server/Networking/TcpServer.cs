
using System.Net;
using System.Net.Sockets;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Networking;

internal sealed class TcpServer
{
    private readonly TcpListener _listener;
    private readonly AuthTcpHandler? _authHandler;
    private readonly MachineTcpHandler? _machineHandler;
    private readonly SessionTcpHandler? _sessionHandler;
    private readonly PasswordResetTcpHandler? _passwordResetHandler;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _heartbeatTimeout;

    public TcpServer(
        IPAddress ipAddress,
        int port,
        AuthTcpHandler? authHandler = null,
        MachineTcpHandler? machineHandler = null,
        SessionTcpHandler? sessionHandler = null,
        PasswordResetTcpHandler? passwordResetHandler = null,
        TimeProvider? timeProvider = null,
        TimeSpan? heartbeatTimeout = null)
    {
        _listener = new TcpListener(ipAddress, port);
        _authHandler = authHandler;
        _machineHandler = machineHandler;
        _sessionHandler = sessionHandler;
        _passwordResetHandler = passwordResetHandler;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _heartbeatTimeout = heartbeatTimeout ?? HeartbeatDefaults.Timeout;
        if (_heartbeatTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout));
        }
    }

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        _listener.Start();

        Console.WriteLine("TCP Server da khoi dong.");
        Console.WriteLine(
            $"Dang lang nghe tai {_listener.LocalEndpoint}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client =
                    await _listener.AcceptTcpClientAsync(
                        cancellationToken);

                // Xu ly nhieu Client dong thoi
                _ = HandleClientAsync(
                    client,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Server dang dung...");
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        using (client)
        {
            Console.WriteLine("Co Client vua ket noi!");
            var context = new TcpConnectionContext();
            using var connectionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            Task heartbeatMonitor = MonitorHeartbeatAsync(
                client,
                context,
                connectionCancellation.Token);
            string? remoteIpAddress =
                (client.Client.RemoteEndPoint as IPEndPoint)?
                .Address
                .ToString();

            try
            {
                using NetworkStream stream =
                    client.GetStream();

                while (!cancellationToken.IsCancellationRequested)
                {
                    NetworkMessage? request =
                        await FrameProtocol.ReceiveAsync(
                            stream,
                            connectionCancellation.Token);

                    // Client dong ket noi
                    if (request is null)
                    {
                        break;
                    }

                    Console.WriteLine(
                        $"Nhan thong diep: {request.Type}, ID: {request.Id}");

                    NetworkMessage response = await DispatchAsync(
                        request,
                        context,
                        remoteIpAddress,
                        connectionCancellation.Token);

                    // Gui phan hoi cho Client
                    await FrameProtocol.SendAsync(
                        stream,
                        response,
                        connectionCancellation.Token);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // Server dang dung
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Loi ket noi: {ex.GetType().Name}");
            }
            finally
            {
                connectionCancellation.Cancel();
                try
                {
                    await heartbeatMonitor;
                }
                catch (OperationCanceledException)
                {
                    // Ket noi da dong hoac Server dang dung.
                }

                using var cleanupTimeout =
                    new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    if (_sessionHandler is not null)
                    {
                        await _sessionHandler.EndOnDisconnectAsync(
                            context,
                            "MatKetNoi",
                            cleanupTimeout.Token);
                    }

                    if (_authHandler is not null &&
                        context.SessionToken is not null)
                    {
                        _ = await _authHandler.HandleAsync(
                            new NetworkMessage(
                                Guid.NewGuid().ToString("N"),
                                NetworkMessageTypes.AuthLogout,
                                context.VerifiedMachine?.MachineName,
                                null,
                                default),
                            context,
                            cleanupTimeout.Token);
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Loi don dep ket noi: {ex.GetType().Name}");
                }

                try
                {
                    if (_machineHandler is not null)
                    {
                        using var machineCleanupTimeout =
                            new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await _machineHandler.MarkDisconnectedAsync(
                            context,
                            machineCleanupTimeout.Token);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Loi cap nhat MatKetNoi: {ex.GetType().Name}");
                }
            }

            Console.WriteLine("Client da ngat ket noi.");
        }
    }

    private async Task MonitorHeartbeatAsync(
        TcpClient client,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(1),
                _timeProvider,
                cancellationToken);

            if (!HeartbeatTimeoutPolicy.IsTimedOut(
                    context.LastHeartbeatReceivedAt,
                    _timeProvider.GetUtcNow(),
                    _heartbeatTimeout))
            {
                continue;
            }

            context.HeartbeatTimedOut = true;
            Console.WriteLine(
                $"Heartbeat timeout cho may {context.VerifiedMachine?.MachineName ?? "chua-xac-thuc"}.");
            try
            {
                client.Client.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {
                // Socket co the da dong dong thoi.
            }
            catch (ObjectDisposedException)
            {
                // Socket da duoc giai phong.
            }

            client.Close();
            return;
        }
    }

    private async Task<NetworkMessage> DispatchAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        string? remoteIpAddress,
        CancellationToken cancellationToken)
    {
        NetworkMessage? response = null;

        if (_machineHandler is not null)
        {
            response = await _machineHandler.HandleAsync(
                request,
                context,
                remoteIpAddress,
                cancellationToken);
        }

        if (response is null && _passwordResetHandler is not null)
        {
            response = await _passwordResetHandler.HandleAsync(
                request,
                context,
                cancellationToken);
        }

        if (response is null && _sessionHandler is not null)
        {
            if (request.Type == NetworkMessageTypes.AuthLogout)
            {
                await _sessionHandler.EndOnDisconnectAsync(
                    context,
                    "DangXuat",
                    cancellationToken);
            }

            response = await _sessionHandler.HandleAsync(
                request,
                context,
                cancellationToken);
        }

        if (response is null && _authHandler is not null)
        {
            response = await _authHandler.HandleAsync(
                request,
                context,
                cancellationToken);
        }

        return response ?? NetworkResponseFactory.Error(
            request,
            "E001",
            "Lệnh chưa được hỗ trợ.");
    }
}
