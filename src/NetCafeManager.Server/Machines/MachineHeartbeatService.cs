using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Machines;

public sealed class MachineHeartbeatService
{
    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly TimeProvider _timeProvider;

    public MachineHeartbeatService(
        string connectionString,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SessionResult<MachineHeartbeatAck>> RecordAsync(
        VerifiedMachine machine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (machine.ExpiresAt <= now)
        {
            return SessionResult<MachineHeartbeatAck>.Failure(
                "HEARTBEAT_VERIFICATION_EXPIRED",
                "Xac thuc may tram da het han.");
        }

        await using var db = new NetCafeDbContext(_options);
        Computer? computer = await db.Computers.SingleOrDefaultAsync(
            row => row.Id == machine.ComputerId &&
                   row.Name == machine.MachineName,
            cancellationToken);
        if (computer is null)
        {
            return SessionResult<MachineHeartbeatAck>.Failure(
                "HEARTBEAT_MACHINE_NOT_FOUND",
                "Khong tim thay may tram.");
        }

        if (computer.Status is ComputerStatuses.Locked or
            ComputerStatuses.Maintenance or
            ComputerStatuses.Incident)
        {
            return SessionResult<MachineHeartbeatAck>.Failure(
                "HEARTBEAT_MACHINE_UNAVAILABLE",
                "May tram dang bi khoa, bao tri hoac gap su co.");
        }

        computer.LastHeartbeatAtUtc = now.UtcDateTime;
        if (computer.Status == ComputerStatuses.Disconnected)
        {
            bool hasActiveSession = await db.UsageSessions.AnyAsync(
                row => row.ComputerId == computer.Id &&
                       row.EndedAtUtc == null &&
                       row.Status != UsageSessionStatuses.Ended,
                cancellationToken);
            computer.Status = hasActiveSession
                ? ComputerStatuses.InUse
                : ComputerStatuses.Available;
        }

        await db.SaveChangesAsync(cancellationToken);
        return SessionResult<MachineHeartbeatAck>.Success(
            new MachineHeartbeatAck(
                computer.Id,
                now,
                (int)HeartbeatDefaults.Interval.TotalSeconds),
            "Server da nhan heartbeat.");
    }

    public async Task MarkDisconnectedAsync(
        VerifiedMachine machine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        await using var db = new NetCafeDbContext(_options);
        Computer? computer = await db.Computers.SingleOrDefaultAsync(
            row => row.Id == machine.ComputerId &&
                   row.Name == machine.MachineName,
            cancellationToken);
        if (computer is null ||
            computer.Status is ComputerStatuses.Locked or
                ComputerStatuses.Maintenance or
                ComputerStatuses.Incident)
        {
            return;
        }

        computer.Status = ComputerStatuses.Disconnected;
        await db.SaveChangesAsync(cancellationToken);
    }
}
