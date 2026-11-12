namespace NetCafeManager.Server.Authentication;

public interface IPasswordResetCodeSender
{
    Task SendAsync(
        string email,
        string otp,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}
