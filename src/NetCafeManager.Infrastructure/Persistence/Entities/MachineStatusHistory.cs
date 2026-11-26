namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class MachineStatusHistory
{
    public long Id { get; set; }
    public int ComputerId { get; set; }
    public Guid AdministratorAccountId { get; set; }
    public required string PreviousStatus { get; set; }
    public required string NewStatus { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAtUtc { get; set; }

    public Computer Computer { get; set; } = null!;
    public Account AdministratorAccount { get; set; } = null!;
}

