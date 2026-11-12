namespace NetCafeManager.Server.Authentication;

public enum SmtpSecurityMode
{
    Auto = 1,
    StartTls = 2,
    SslOnConnect = 3
}

public sealed record SmtpOptions(
    string Host,
    int Port,
    SmtpSecurityMode SecurityMode,
    string Username,
    string Password,
    string FromEmail,
    string FromName);
