using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace NetCafeManager.Server.Authentication;

public sealed class MailKitPasswordResetCodeSender : IPasswordResetCodeSender
{
    private readonly SmtpOptions _options;

    public MailKitPasswordResetCodeSender(SmtpOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FromEmail);

        if (options.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SMTP port must be between 1 and 65535.");
        }
    }

    public async Task SendAsync(
        string email,
        string otp,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(otp);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = "Mã xác nhận đặt lại mật khẩu NetCafeManager";
        message.Body = new TextPart("plain")
        {
            Text = $"Mã xác nhận của bạn là: {otp}\n"
                + $"Mã có hiệu lực đến {expiresAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC.\n\n"
                + "Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này."
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            ToSecureSocketOptions(_options.SecurityMode),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(
                _options.Username,
                _options.Password,
                cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static SecureSocketOptions ToSecureSocketOptions(SmtpSecurityMode mode) => mode switch
    {
        SmtpSecurityMode.Auto => SecureSocketOptions.Auto,
        SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
