namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class Account
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordAlgorithm { get; set; }
    public int PasswordIterations { get; set; }
    public required string PasswordSaltBase64 { get; set; }
    public required string PasswordHashBase64 { get; set; }
    public required string Email { get; set; }
    public string? FullName { get; set; }
    public required string Role { get; set; }
    public decimal Balance { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<UsageSession> UsageSessions { get; } = [];
    public ICollection<TopUpTransaction> ReceivedTopUps { get; } = [];
    public ICollection<TopUpTransaction> PerformedTopUps { get; } = [];
    public ICollection<PasswordResetOtp> PasswordResetOtps { get; } = [];
    public ICollection<RemoteCommand> RemoteCommands { get; } = [];
    public ICollection<BlockPolicy> CreatedBlockPolicies { get; } = [];
    public ICollection<PolicyViolation> PolicyViolations { get; } = [];
}
