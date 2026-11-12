
using System.Net;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Server.Networking;

namespace NetCafeManager.Server;

internal static class Program
{
    private static async Task Main()
    {
        using var cancellationTokenSource =
            new CancellationTokenSource();

        // Cho phep dung Server bang Ctrl + C
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        // Repository tam thoi phuc vu phat trien
        // Chua co tai khoan mau trong bo nho
        IUserAccountRepository accounts =
            new InMemoryUserAccountRepository();

        // Su dung cac thanh phan xac thuc san co
        var passwordHasher = new Pbkdf2PasswordHasher();
        var sessionManager = new SessionManager();

        var authenticationService =
            new AuthenticationService(
                accounts,
                passwordHasher,
                sessionManager);

        // Ket noi AuthenticationService voi TCP
        var authHandler =
            new AuthTcpHandler(authenticationService);

        // TCP Server chay tren localhost:5050
        var server = new TcpServer(
            IPAddress.Loopback,
            5050,
            authHandler);

        Console.WriteLine(
            "NetCafeManager TCP Authentication Server");

        await server.StartAsync(
            cancellationTokenSource.Token);
    }
}
