namespace NetCafeManager.Infrastructure.Persistence.Entities;

public sealed class Computer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public required string Status { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public string? MaintenanceReason { get; set; }

    public ICollection<UsageSession> UsageSessions { get; } = [];
    public ICollection<RemoteCommand> RemoteCommands { get; } = [];
    public ICollection<PolicyViolation> PolicyViolations { get; } = [];
}
