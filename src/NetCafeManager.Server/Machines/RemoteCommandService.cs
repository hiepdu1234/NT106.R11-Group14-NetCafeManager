using System.Data;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Administration;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Machines;

public sealed class RemoteCommandService
{
    private static readonly HashSet<string> SupportedTypes =
        new(StringComparer.Ordinal)
        {
            RemoteCommandTypes.LockComputer,
            RemoteCommandTypes.LogoutComputer
        };

    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly TimeProvider _clock;

    public RemoteCommandService(
        string connectionString,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<AdminResult<RemoteCommandSummary>> QueueAsync(
        AuthenticatedUser actor,
        QueueRemoteCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return AdminResult<RemoteCommandSummary>.Failure(
                "COMMAND_FORBIDDEN",
                "Administrator role is required.");
        }

        string commandType = request.CommandType?.Trim() ?? string.Empty;
        if (request.ComputerId <= 0 || !SupportedTypes.Contains(commandType))
        {
            return AdminResult<RemoteCommandSummary>.Failure(
                "COMMAND_INVALID",
                "Remote command is invalid.");
        }

        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireLockAsync(db, request.ComputerId, cancellationToken);

        Computer? computer = await db.Computers
            .Include(row => row.UsageSessions)
            .SingleOrDefaultAsync(row => row.Id == request.ComputerId, cancellationToken);
        if (computer is null)
        {
            return AdminResult<RemoteCommandSummary>.Failure(
                "COMMAND_MACHINE_NOT_FOUND",
                "Machine was not found.");
        }

        if (computer.Status is ComputerStatuses.Maintenance or ComputerStatuses.Incident)
        {
            return AdminResult<RemoteCommandSummary>.Failure(
                "COMMAND_UNAVAILABLE",
                "Machine is under maintenance or incident handling.");
        }

        bool hasActiveSession = computer.UsageSessions.Any(session =>
            session.EndedAtUtc == null &&
            session.Status != UsageSessionStatuses.Ended);
        if (commandType == RemoteCommandTypes.LogoutComputer && !hasActiveSession)
        {
            return AdminResult<RemoteCommandSummary>.Failure(
                "COMMAND_NO_SESSION",
                "Machine has no active usage session to end.");
        }

        if (commandType == RemoteCommandTypes.LockComputer &&
            computer.Status == ComputerStatuses.Locked)
        {
            return AdminResult<RemoteCommandSummary>.Failure(
                "COMMAND_ALREADY_LOCKED",
                "Machine is already locked.");
        }

        bool alreadyPending = await db.RemoteCommands.AnyAsync(
            row => row.ComputerId == computer.Id &&
                   (row.Status == RemoteCommandStatuses.Pending ||
                    row.Status == RemoteCommandStatuses.Sent ||
                    row.Status == RemoteCommandStatuses.Acknowledged),
            cancellationToken);
        if (alreadyPending)
        {
            return AdminResult<RemoteCommandSummary>.Failure(
                "COMMAND_ALREADY_PENDING",
                "Machine already has a remote command awaiting completion.");
        }

        Account administrator = await db.Accounts.SingleAsync(
            row => row.Id == actor.Id,
            cancellationToken);
        var command = new RemoteCommand
        {
            Id = Guid.NewGuid(),
            ComputerId = computer.Id,
            AdministratorAccountId = administrator.Id,
            CommandType = commandType,
            Status = RemoteCommandStatuses.Pending,
            SentAtUtc = _clock.GetUtcNow().UtcDateTime,
            Computer = computer,
            AdministratorAccount = administrator
        };
        db.RemoteCommands.Add(command);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AdminResult<RemoteCommandSummary>.Success(
            ToSummary(command),
            commandType == RemoteCommandTypes.LockComputer
                ? "Lock command was queued."
                : "Logout command was queued.");
    }

    public async Task<IReadOnlyList<RemoteCommandSummary>> ListAsync(
        AuthenticatedUser actor,
        int computerId,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            throw new UnauthorizedAccessException("Administrator role is required.");
        }

        await using var db = new NetCafeDbContext(_options);
        return await db.RemoteCommands
            .AsNoTracking()
            .Where(row => computerId <= 0 || row.ComputerId == computerId)
            .OrderByDescending(row => row.SentAtUtc)
            .Take(200)
            .Select(row => new RemoteCommandSummary(
                row.Id,
                row.ComputerId,
                row.Computer.Name,
                row.AdministratorAccount.Username,
                row.CommandType,
                row.Status,
                new DateTimeOffset(row.SentAtUtc, TimeSpan.Zero),
                row.AcknowledgedAtUtc == null
                    ? null
                    : new DateTimeOffset(row.AcknowledgedAtUtc.Value, TimeSpan.Zero),
                row.CompletedAtUtc == null
                    ? null
                    : new DateTimeOffset(row.CompletedAtUtc.Value, TimeSpan.Zero),
                row.ErrorCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<PendingRemoteCommand?> TakePendingAsync(
        VerifiedMachine machine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireLockAsync(db, machine.ComputerId, cancellationToken);

        RemoteCommand? command = await db.RemoteCommands
            .Where(row => row.ComputerId == machine.ComputerId &&
                          (row.Status == RemoteCommandStatuses.Pending ||
                           row.Status == RemoteCommandStatuses.Sent))
            .OrderBy(row => row.SentAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (command is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        command.Status = RemoteCommandStatuses.Sent;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PendingRemoteCommand(
            command.Id,
            command.CommandType,
            command.Payload);
    }

    public async Task<SessionResult<RemoteCommandSummary>> CompleteAsync(
        VerifiedMachine machine,
        RemoteCommandResultRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        if (request.CommandId == Guid.Empty)
        {
            return SessionResult<RemoteCommandSummary>.Failure(
                "COMMAND_INVALID",
                "Remote command result is invalid.");
        }

        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireLockAsync(db, machine.ComputerId, cancellationToken);
        RemoteCommand? command = await db.RemoteCommands
            .Include(row => row.Computer)
                .ThenInclude(row => row.UsageSessions)
            .Include(row => row.AdministratorAccount)
            .SingleOrDefaultAsync(
                row => row.Id == request.CommandId &&
                       row.ComputerId == machine.ComputerId,
                cancellationToken);
        if (command is null)
        {
            return SessionResult<RemoteCommandSummary>.Failure(
                "COMMAND_NOT_FOUND",
                "Remote command was not found for this machine.");
        }

        if (command.Status is RemoteCommandStatuses.Succeeded or RemoteCommandStatuses.Failed)
        {
            await transaction.CommitAsync(cancellationToken);
            return SessionResult<RemoteCommandSummary>.Success(
                ToSummary(command),
                "Remote command result was already recorded.");
        }

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        command.AcknowledgedAtUtc ??= now;
        command.Status = RemoteCommandStatuses.Acknowledged;

        bool activeSession = command.Computer.UsageSessions.Any(session =>
            session.EndedAtUtc == null &&
            session.Status != UsageSessionStatuses.Ended);
        bool succeeded = request.Succeeded && !activeSession;
        if (succeeded && command.CommandType == RemoteCommandTypes.LockComputer)
        {
            string previousStatus = command.Computer.Status;
            command.Computer.Status = ComputerStatuses.Locked;
            command.Computer.MaintenanceReason = null;
            if (previousStatus != ComputerStatuses.Locked)
            {
                db.MachineStatusHistories.Add(new MachineStatusHistory
                {
                    ComputerId = command.ComputerId,
                    AdministratorAccountId = command.AdministratorAccountId,
                    PreviousStatus = previousStatus,
                    NewStatus = ComputerStatuses.Locked,
                    Reason = "Remote command",
                    ChangedAtUtc = now
                });
            }
        }
        else if (succeeded && command.CommandType == RemoteCommandTypes.LogoutComputer &&
                 command.Computer.Status == ComputerStatuses.InUse)
        {
            command.Computer.Status = ComputerStatuses.Available;
        }

        command.CompletedAtUtc = now;
        command.Status = succeeded
            ? RemoteCommandStatuses.Succeeded
            : RemoteCommandStatuses.Failed;
        command.ErrorCode = succeeded
            ? null
            : activeSession
                ? "ACTIVESES"
                : NormalizeErrorCode(request.ErrorCode);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SessionResult<RemoteCommandSummary>.Success(
            ToSummary(command),
            succeeded
                ? "Remote command completed successfully."
                : "Remote command failed.");
    }

    private static string? NormalizeErrorCode(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0) return "CLIENTERR";
        return normalized.Length <= 10 ? normalized : normalized[..10];
    }

    private static Task<int> AcquireLockAsync(
        NetCafeDbContext db,
        int computerId,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = {"NetCafeManager:RemoteCommand:Computer:" + computerId},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            IF @LockResult < 0
                THROW 51003, N'Cannot lock remote command queue.', 1;
            """, cancellationToken);

    private static RemoteCommandSummary ToSummary(RemoteCommand command) =>
        new(
            command.Id,
            command.ComputerId,
            command.Computer.Name,
            command.AdministratorAccount.Username,
            command.CommandType,
            command.Status,
            new DateTimeOffset(command.SentAtUtc, TimeSpan.Zero),
            command.AcknowledgedAtUtc == null
                ? null
                : new DateTimeOffset(command.AcknowledgedAtUtc.Value, TimeSpan.Zero),
            command.CompletedAtUtc == null
                ? null
                : new DateTimeOffset(command.CompletedAtUtc.Value, TimeSpan.Zero),
            command.ErrorCode);
}

