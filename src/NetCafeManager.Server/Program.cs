using NetCafeManager.Infrastructure.Security;
using System.Globalization;
using System.Net;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Server.Machines;
using NetCafeManager.Server.Networking;
using NetCafeManager.Server.Sessions;
using NetCafeManager.Server.Inventory;
using NetCafeManager.Server.Billing;
using NetCafeManager.Server.Customers;
using NetCafeManager.Server.Invoices;
using System.Security.Cryptography.X509Certificates;

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
        int passwordHashIterations = ReadOptionalInteger(
            "NETCAFE_PBKDF2_ITERATIONS",
            Pbkdf2PasswordHasher.DefaultIterations,
            Pbkdf2PasswordHasher.MinimumAcceptedIterations,
            Pbkdf2PasswordHasher.MaximumAcceptedIterations);

        try
        {
            IPasswordResetCodeSender codeSender =
                new MailKitPasswordResetCodeSender(ReadSmtpOptions());
            AuthenticationServices authentication =
                AuthenticationSetup.CreateServicesForSqlServer(
                    connectionString,
                    codeSender,
                    otpHashSecret,
                    passwordHashIterations: passwordHashIterations);
            var handshakes = new MachineHandshakeService(
                connectionString,
                EnvironmentMachineKeyProvider.Create());
            var heartbeats = new MachineHeartbeatService(connectionString);
            var usageSessions = new UsageSessionService(
                connectionString,
                hourlyRate);

            var authHandler = new AuthTcpHandler(
                authentication.Authentication);
            var sessionHandler = new SessionTcpHandler(
                usageSessions,
                authentication.Authentication);
            var machineHandler = new MachineTcpHandler(
                handshakes,
                heartbeats,
                sessionHandler);
            var passwordResetHandler = new PasswordResetTcpHandler(
                authentication.PasswordReset);
            var productPurchaseHandler = new ProductPurchaseTcpHandler(
                new ProductPurchaseService(connectionString),
                new ProductInventoryService(connectionString),
                authentication.Authentication);
            var adminOperationsHandler = new AdminOperationsTcpHandler(
                new CustomerManagementService(connectionString),
                new HourPackageService(connectionString),
                new TopUpService(connectionString),
                new ProductInventoryService(connectionString),
                new InvoiceService(connectionString),
                new BalanceRefundService(connectionString),
                authentication.Authentication);

            int port = ReadPort();
            IPAddress listenAddress = ReadListenAddress();
            using X509Certificate2? serverCertificate = LoadServerCertificate();
            if (!IPAddress.IsLoopback(listenAddress) && serverCertificate is null)
            {
                throw new InvalidOperationException(
                    "Khi mở Server ra LAN, phải cấu hình chứng thư TLS bằng " +
                    "NETCAFE_TLS_CERTIFICATE_PATH.");
            }
            var server = new TcpServer(
                listenAddress,
                port,
                authHandler,
                machineHandler,
                sessionHandler,
                passwordResetHandler,
                productPurchaseHandler,
                adminOperationsHandler,
                serverCertificate);

            Console.WriteLine(
                "NetCafeManager TCP Authentication Server");
            Console.WriteLine(serverCertificate is null
                ? "Chế độ demo loopback không TLS."
                : "TLS đã bật cho kết nối TCP.");
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

    private static int ReadOptionalInteger(
        string variableName,
        int fallback,
        int minimum,
        int maximum)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, out int value) ||
            value < minimum ||
            value > maximum)
        {
            throw new InvalidOperationException(
                $"{variableName} phải nằm trong khoảng {minimum}-{maximum}.");
        }

        return value;
    }

    private static int ReadPort() =>
        Environment.GetEnvironmentVariable("NETCAFE_TCP_PORT") is null
            ? 5050
            : ReadInteger("NETCAFE_TCP_PORT", 1, 65535);

    private static IPAddress ReadListenAddress()
    {
        string raw = Environment.GetEnvironmentVariable(
            "NETCAFE_LISTEN_ADDRESS") ?? IPAddress.Loopback.ToString();
        return IPAddress.TryParse(raw, out IPAddress? address)
            ? address
            : throw new InvalidOperationException(
                "NETCAFE_LISTEN_ADDRESS phải là địa chỉ IP hợp lệ.");
    }

    private static X509Certificate2? LoadServerCertificate()
    {
        string? path = Environment.GetEnvironmentVariable(
            "NETCAFE_TLS_CERTIFICATE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "Không tìm thấy tệp chứng thư TLS của Server.");
        }

#pragma warning disable SYSLIB0057
        var certificate = new X509Certificate2(
            path,
            Environment.GetEnvironmentVariable("NETCAFE_TLS_CERTIFICATE_PASSWORD"),
            X509KeyStorageFlags.EphemeralKeySet |
            X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "Chứng thư TLS phải chứa private key.");
        }

        return certificate;
    }

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
