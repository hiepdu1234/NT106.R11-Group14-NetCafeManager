namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class Invoice
{
    public long Id { get; set; }
    public long UsageSessionId { get; set; }
    public decimal UsageCost { get; set; }
    public decimal ServiceCost { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public required string Status { get; set; }

    public UsageSession UsageSession { get; set; } = null!;
}
