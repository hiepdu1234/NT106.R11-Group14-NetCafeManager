namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class ServiceUsage
{
    public long Id { get; set; }
    public Guid CommandId { get; set; }
    public long UsageSessionId { get; set; }
    public int ServiceItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceApplied { get; set; }
    public decimal TotalAmount { get; private set; }
    public int StockAfter { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime PurchasedAtUtc { get; set; }

    public UsageSession UsageSession { get; set; } = null!;
    public ServiceItem ServiceItem { get; set; } = null!;
}

