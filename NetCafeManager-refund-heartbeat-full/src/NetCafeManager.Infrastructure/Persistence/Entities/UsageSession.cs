namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class UsageSession
{
    public long Id { get; set; }
    public int ComputerId { get; set; }
    public Guid AccountId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public decimal HourlyRateApplied { get; set; }
    public decimal UsageCost { get; set; }
    public required string Status { get; set; }
    public string? EndReason { get; set; }

    public Computer Computer { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public ICollection<ServiceUsage> ServiceUsages { get; } = [];
    public Invoice? Invoice { get; set; }
}
