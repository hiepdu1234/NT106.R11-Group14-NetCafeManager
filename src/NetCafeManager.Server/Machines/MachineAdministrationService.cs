using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Administration;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Machines;

public sealed class MachineAdministrationService
{
    private static readonly HashSet<string> EditableStatuses =
        new(StringComparer.Ordinal)
        {
            ComputerStatuses.Available,
            ComputerStatuses.Locked,
            ComputerStatuses.Maintenance,
            ComputerStatuses.Incident
        };

    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly TimeProvider _clock;

    public MachineAdministrationService(
        string connectionString,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<MachineStatusHistorySummary>> ListHistoryAsync(
        AuthenticatedUser actor,
        int computerId,
        CancellationToken cancellationToken = default)
    {
        EnsureAdministrator(actor);
        if (computerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(computerId));
        }

        await using var db = new NetCafeDbContext(_options);
        return await db.MachineStatusHistories
            .AsNoTracking()
            .Where(history => history.ComputerId == computerId)
            .OrderByDescending(history => history.ChangedAtUtc)
            .Take(200)
            .Select(history => new MachineStatusHistorySummary(
                history.Id,
                history.ComputerId,
                history.Computer.Name,
                history.PreviousStatus,
                history.NewStatus,
                history.Reason,
                history.AdministratorAccount.Username,
                new DateTimeOffset(history.ChangedAtUtc, TimeSpan.Zero)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MachineAdminSummary>> ListAsync(
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        EnsureAdministrator(actor);
        await using var db = new NetCafeDbContext(_options);
        return await db.Computers
            .AsNoTracking()
            .OrderBy(machine => machine.Name)
            .Select(machine => new MachineAdminSummary(
                machine.Id,
                machine.Name,
                machine.IpAddress,
                machine.MacAddress,
                machine.Status,
                machine.LastHeartbeatAtUtc == null
                    ? null
                    : new DateTimeOffset(machine.LastHeartbeatAtUtc.Value, TimeSpan.Zero),
                machine.MaintenanceReason,
                machine.UsageSessions.Any(session =>
                    session.EndedAtUtc == null &&
                    session.Status != UsageSessionStatuses.Ended)))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminResult<MachineAdminSummary>> SetStatusAsync(
        AuthenticatedUser actor,
        SetMachineStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return AdminResult<MachineAdminSummary>.Failure(
                "MACHINE_FORBIDDEN",
                "Administrator role is required.");
        }

        string status = request.Status?.Trim() ?? string.Empty;
        string? reason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason.Trim();
        if (request.ComputerId <= 0 || !EditableStatuses.Contains(status) || reason?.Length > 500)
        {
            return AdminResult<MachineAdminSummary>.Failure(
                "MACHINE_INVALID_INPUT",
                "Machine status or reason is invalid.");
        }

        await using var db = new NetCafeDbContext(_options);
        var machine = await db.Computers
            .Include(row => row.UsageSessions)
            .SingleOrDefaultAsync(row => row.Id == request.ComputerId, cancellationToken);
        if (machine is null)
        {
            return AdminResult<MachineAdminSummary>.Failure(
                "MACHINE_NOT_FOUND",
                "Machine was not found.");
        }

        bool hasActiveSession = machine.UsageSessions.Any(session =>
            session.EndedAtUtc == null &&
            session.Status != UsageSessionStatuses.Ended);
        if (hasActiveSession)
        {
            return AdminResult<MachineAdminSummary>.Failure(
                "MACHINE_HAS_ACTIVE_SESSION",
                "End the active usage session before changing machine status.");
        }

        string previousStatus = machine.Status;
        string? previousReason = machine.MaintenanceReason;
        string? newReason = status is ComputerStatuses.Maintenance or ComputerStatuses.Incident
            ? reason
            : null;
        if (previousStatus == status && previousReason == newReason)
        {
            return AdminResult<MachineAdminSummary>.Success(
                new MachineAdminSummary(
                    machine.Id,
                    machine.Name,
                    machine.IpAddress,
                    machine.MacAddress,
                    machine.Status,
                    machine.LastHeartbeatAtUtc == null
                        ? null
                        : new DateTimeOffset(machine.LastHeartbeatAtUtc.Value, TimeSpan.Zero),
                    machine.MaintenanceReason,
                    false),
                "Machine status was unchanged.");
        }

        machine.Status = status;
        machine.MaintenanceReason = newReason;
        db.MachineStatusHistories.Add(new MachineStatusHistory
        {
            ComputerId = machine.Id,
            AdministratorAccountId = actor.Id,
            PreviousStatus = previousStatus,
            NewStatus = status,
            Reason = newReason,
            ChangedAtUtc = _clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(cancellationToken);

        return AdminResult<MachineAdminSummary>.Success(
            new MachineAdminSummary(
                machine.Id,
                machine.Name,
                machine.IpAddress,
                machine.MacAddress,
                machine.Status,
                machine.LastHeartbeatAtUtc == null
                    ? null
                    : new DateTimeOffset(machine.LastHeartbeatAtUtc.Value, TimeSpan.Zero),
                machine.MaintenanceReason,
                false),
            "Machine status was updated.");
    }

    private static void EnsureAdministrator(AuthenticatedUser actor)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            throw new UnauthorizedAccessException("Administrator role is required.");
        }
    }
}

