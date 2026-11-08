namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class HourPackage
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Hours { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<TopUpTransaction> TopUpTransactions { get; } = [];
}
