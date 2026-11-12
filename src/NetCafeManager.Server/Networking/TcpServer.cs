
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Server.Networking;

internal sealed class TcpServer
{
    private readonly TcpListener _listener;
    private readonly AuthTcpHandler? _authHandler;

    public TcpServer(
        IPAddress ipAddress,
        int port,
        AuthTcpHandler? authHandler = null)
    {
        _listener = new TcpListener(ipAddress, port);
        _authHandler = authHandler;
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

                    NetworkMessage response;

                    // Chuyen yeu cau xac thuc den handler
                    NetworkMessage? authResponse = null;

                    if (_authHandler is not null)
                    {
                        authResponse =
                            await _authHandler.HandleAsync(
                                request,
                                cancellationToken);
                    }

                    if (authResponse is not null)
                    {
                        response = authResponse;
                    }
                    else if (request.Type == "MACHINE_HANDSHAKE")
                    {
                        // Phan 3 chua duoc tich hop.
                        // Tam thoi tu choi xac thuc may.
                        response = CreateErrorResponse(
                            request,
                            "E004",
                            "May chua hoan tat xac thuc.");
                    }
                    else
                    {
                        response = CreateErrorResponse(
                            request,
                            "E001",
                            "Lenh chua duoc ho tro.");
                    }

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

            Console.WriteLine("Client da ngat ket noi.");
        }
    }

    private static NetworkMessage CreateErrorResponse(
        NetworkMessage request,
        string errorCode,
        string errorMessage)
    {
        return new NetworkMessage(
            Guid.NewGuid().ToString("N"),
            "ERROR",
            request.MachineId,
            request.Id,
            JsonSerializer.SerializeToElement(new
            {
                code = errorCode,
                message = errorMessage
            }));
    }
}
