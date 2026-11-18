namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class ServiceItem
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public int? StockQuantity { get; set; }
    public int? ReferenceStockQuantity { get; set; }

    public ICollection<ServiceUsage> ServiceUsages { get; } = [];
}
