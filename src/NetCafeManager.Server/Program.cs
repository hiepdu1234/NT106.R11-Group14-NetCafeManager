using System.Globalization;
using System.Net;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Server.Machines;
using NetCafeManager.Server.Networking;
using NetCafeManager.Server.Sessions;

namespace NetCafeManager.Server;

internal static class Program
{
    private static async Task Main()
    {
        using var cancellationTokenSource =
            new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        string connectionString = RequireEnvironment(
            "NETCAFE_DB_CONNECTION");
        byte[] otpHashSecret = ReadBase64Secret(
            "NETCAFE_OTP_HASH_SECRET_BASE64");
        decimal hourlyRate = ReadPositiveDecimal(
            "NETCAFE_HOURLY_RATE");

        try
        {
            IPasswordResetCodeSender codeSender =
                new MailKitPasswordResetCodeSender(ReadSmtpOptions());
            AuthenticationServices authentication =
                AuthenticationSetup.CreateServicesForSqlServer(
                    connectionString,
                    codeSender,
                    otpHashSecret);
            var handshakes = new MachineHandshakeService(
                connectionString,
                EnvironmentMachineKeyProvider.Create());
            var usageSessions = new UsageSessionService(
                connectionString,
                hourlyRate);

            var authHandler = new AuthTcpHandler(
                authentication.Authentication);
            var machineHandler = new MachineTcpHandler(handshakes);
            var sessionHandler = new SessionTcpHandler(
                usageSessions,
                authentication.Authentication);
            var passwordResetHandler = new PasswordResetTcpHandler(
                authentication.PasswordReset);

            int port = ReadPort();
            var server = new TcpServer(
                IPAddress.Loopback,
                port,
                authHandler,
                machineHandler,
                sessionHandler,
                passwordResetHandler);

            Console.WriteLine(
                "NetCafeManager TCP Authentication Server");
            Console.WriteLine(
                "Chỉ lắng nghe loopback; cần TLS trước khi mở ra mạng LAN.");
            await server.StartAsync(cancellationTokenSource.Token);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(
                otpHashSecret);
        }
    }

    private static SmtpOptions ReadSmtpOptions()
    {
        string modeText = Environment.GetEnvironmentVariable(
            "NETCAFE_SMTP_SECURITY") ?? nameof(SmtpSecurityMode.StartTls);
        if (!Enum.TryParse(
            modeText,
            true,
            out SmtpSecurityMode securityMode))
        {
            throw new InvalidOperationException(
                "NETCAFE_SMTP_SECURITY phải là Auto, StartTls hoặc SslOnConnect.");
        }

        return new SmtpOptions(
            RequireEnvironment("NETCAFE_SMTP_HOST"),
            ReadInteger("NETCAFE_SMTP_PORT", 1, 65535),
            securityMode,
            Environment.GetEnvironmentVariable("NETCAFE_SMTP_USERNAME") ?? string.Empty,
            Environment.GetEnvironmentVariable("NETCAFE_SMTP_PASSWORD") ?? string.Empty,
            RequireEnvironment("NETCAFE_SMTP_FROM"),
            Environment.GetEnvironmentVariable("NETCAFE_SMTP_FROM_NAME")
                ?? "NetCafeManager");
    }

    private static byte[] ReadBase64Secret(string variableName)
    {
        try
        {
            byte[] secret = Convert.FromBase64String(
                RequireEnvironment(variableName));
            if (secret.Length < 32)
            {
                throw new InvalidOperationException(
                    $"{variableName} phải chứa ít nhất 32 byte ngẫu nhiên.");
            }

            return secret;
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"{variableName} không phải Base64 hợp lệ.",
                exception);
        }
    }

    private static decimal ReadPositiveDecimal(string variableName)
    {
        string raw = RequireEnvironment(variableName);
        if (!decimal.TryParse(
            raw,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal value) || value <= 0)
        {
            throw new InvalidOperationException(
                $"{variableName} phải là số dương theo định dạng invariant.");
        }

        return value;
    }

    private static int ReadPort() =>
        Environment.GetEnvironmentVariable("NETCAFE_TCP_PORT") is null
            ? 5050
            : ReadInteger("NETCAFE_TCP_PORT", 1, 65535);

    private static int ReadInteger(
        string variableName,
        int minimum,
        int maximum)
    {
        string raw = RequireEnvironment(variableName);
        if (!int.TryParse(raw, out int value) ||
            value < minimum ||
            value > maximum)
        {
            throw new InvalidOperationException(
                $"{variableName} phải nằm trong khoảng {minimum}-{maximum}.");
        }

        return value;
    }

    private static string RequireEnvironment(string variableName) =>
        Environment.GetEnvironmentVariable(variableName) is { } value &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Thiếu biến môi trường {variableName}.");
}
