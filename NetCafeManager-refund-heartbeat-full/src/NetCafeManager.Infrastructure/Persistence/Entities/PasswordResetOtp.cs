namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class PasswordResetOtp
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public required byte[] OtpHash { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public int FailedAttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Account Account { get; set; } = null!;
}
