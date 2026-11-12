namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class PolicyViolation
{
    public long Id { get; set; }
    public int ComputerId { get; set; }
    public Guid? AccountId { get; set; }
    public int BlockPolicyId { get; set; }
    public required string DetectedValue { get; set; }
    public DateTime DetectedAtUtc { get; set; }
    public required string ActionTaken { get; set; }

    public Computer Computer { get; set; } = null!;
    public Account? Account { get; set; }
    public BlockPolicy BlockPolicy { get; set; } = null!;
}
