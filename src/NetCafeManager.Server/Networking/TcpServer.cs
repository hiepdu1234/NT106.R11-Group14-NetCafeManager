
using System.Net;
using System.Net.Sockets;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Server.Networking;

internal sealed class TcpServer
{
    private readonly TcpListener _listener;
    private readonly AuthTcpHandler? _authHandler;
    private readonly MachineTcpHandler? _machineHandler;
    private readonly SessionTcpHandler? _sessionHandler;
    private readonly PasswordResetTcpHandler? _passwordResetHandler;

    public TcpServer(
        IPAddress ipAddress,
        int port,
        AuthTcpHandler? authHandler = null,
        MachineTcpHandler? machineHandler = null,
        SessionTcpHandler? sessionHandler = null,
        PasswordResetTcpHandler? passwordResetHandler = null)
    {
        _listener = new TcpListener(ipAddress, port);
        _authHandler = authHandler;
        _machineHandler = machineHandler;
        _sessionHandler = sessionHandler;
        _passwordResetHandler = passwordResetHandler;
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
                            cancellationToken);

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
                        cancellationToken);

                    // Gui phan hoi cho Client
                    await FrameProtocol.SendAsync(
                        stream,
                        response,
                        cancellationToken);
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
            }

            Console.WriteLine("Client da ngat ket noi.");
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
