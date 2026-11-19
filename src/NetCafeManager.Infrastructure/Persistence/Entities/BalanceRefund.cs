namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class BalanceRefund
{
    public long Id { get; set; }
    public Guid CommandId { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public Guid PerformedByAccountId { get; set; }
    public required string Method { get; set; }
    public required string Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Account Account { get; set; } = null!;
    public Account PerformedByAccount { get; set; } = null!;
}
