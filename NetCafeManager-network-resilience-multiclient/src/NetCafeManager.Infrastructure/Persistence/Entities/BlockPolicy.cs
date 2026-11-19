namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class BlockPolicy
{
    public int Id { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required string Mode { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedByAccountId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Account CreatedByAccount { get; set; } = null!;
    public ICollection<PolicyViolation> PolicyViolations { get; } = [];
}
