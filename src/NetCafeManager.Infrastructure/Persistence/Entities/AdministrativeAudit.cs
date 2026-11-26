namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class AdministrativeAudit
{
    public long Id { get; set; }
    public Guid AdministratorAccountId { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public string? TargetId { get; set; }
    public bool Succeeded { get; set; }
    public required string Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Account AdministratorAccount { get; set; } = null!;
}

