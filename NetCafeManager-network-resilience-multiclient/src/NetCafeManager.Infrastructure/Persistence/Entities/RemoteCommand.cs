namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class RemoteCommand
{
    public Guid Id { get; set; }
    public int ComputerId { get; set; }
    public Guid AdministratorAccountId { get; set; }
    public required string CommandType { get; set; }
    public string? Payload { get; set; }
    public required string Status { get; set; }
    public DateTime SentAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorCode { get; set; }

    public Computer Computer { get; set; } = null!;
    public Account AdministratorAccount { get; set; } = null!;
}
