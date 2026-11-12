namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class TopUpTransaction
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public int HourPackageId { get; set; }
    public decimal Amount { get; set; }
    public int HoursAdded { get; set; }
    public Guid PerformedByAccountId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? Note { get; set; }

    public Account Account { get; set; } = null!;
    public HourPackage HourPackage { get; set; } = null!;
    public Account PerformedByAccount { get; set; } = null!;
}
