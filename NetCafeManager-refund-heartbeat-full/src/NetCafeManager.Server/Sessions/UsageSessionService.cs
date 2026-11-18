using System.Data;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Sessions;

public sealed class UsageSessionService
{
    private const int MaxEndReasonLength = 30;

    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly decimal _hourlyRate;
    private readonly TimeProvider _timeProvider;

    public UsageSessionService(
        string connectionString,
        decimal hourlyRate,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (hourlyRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hourlyRate));
        }

        _hourlyRate = hourlyRate;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    public async Task<SessionResult<UsageSessionSummary>> StartAsync(
        AuthenticatedUser actor,
        VerifiedMachine machine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(machine);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (actor.Role != UserRole.Client)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_CLIENT_REQUIRED",
                "Chỉ tài khoản khách hàng được bắt đầu phiên chơi.");
        }

        if (machine.ExpiresAt <= now)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_MACHINE_VERIFICATION_EXPIRED",
                "Xác thực máy trạm đã hết hạn.");
        }

        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireLockAsync(
            db,
            $"NetCafeManager:UsageSession:Account:{actor.Id:N}",
            cancellationToken);
        await AcquireLockAsync(
            db,
            $"NetCafeManager:UsageSession:Computer:{machine.ComputerId}",
            cancellationToken);

        Account? account = await db.Accounts.SingleOrDefaultAsync(
            row => row.Id == actor.Id,
            cancellationToken);
        if (account is null ||
            !account.IsActive ||
            account.Role != nameof(UserRole.Client))
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_ACCOUNT_UNAVAILABLE",
                "Tài khoản không tồn tại hoặc đang bị khóa.");
        }

        if (account.Balance <= 0)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_INSUFFICIENT_BALANCE",
                "Tài khoản không đủ số dư để bắt đầu phiên.");
        }

        Computer? computer = await db.Computers.SingleOrDefaultAsync(
            row => row.Id == machine.ComputerId &&
                   row.Name == machine.MachineName,
            cancellationToken);
        if (computer is null)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_COMPUTER_UNAVAILABLE",
                "Máy trạm không sẵn sàng để bắt đầu phiên.");
        }

        if (computer.Status == ComputerStatuses.InUse)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_COMPUTER_BUSY",
                "Máy trạm đang có phiên sử dụng khác.");
        }

        if (computer.Status != ComputerStatuses.Available)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_COMPUTER_UNAVAILABLE",
                "Máy trạm không sẵn sàng để bắt đầu phiên.");
        }

        bool accountHasActiveSession = await db.UsageSessions.AnyAsync(
            row => row.AccountId == account.Id &&
                   row.EndedAtUtc == null &&
                   row.Status != UsageSessionStatuses.Ended,
            cancellationToken);
        if (accountHasActiveSession)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_ACCOUNT_ALREADY_ACTIVE",
                "Tài khoản đang sử dụng một máy khác.");
        }

        bool computerHasActiveSession = await db.UsageSessions.AnyAsync(
            row => row.ComputerId == computer.Id &&
                   row.EndedAtUtc == null &&
                   row.Status != UsageSessionStatuses.Ended,
            cancellationToken);
        if (computerHasActiveSession)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_COMPUTER_BUSY",
                "Máy trạm đang có phiên sử dụng khác.");
        }

        var session = new UsageSession
        {
            ComputerId = computer.Id,
            AccountId = account.Id,
            StartedAtUtc = now.UtcDateTime,
            HourlyRateApplied = _hourlyRate,
            UsageCost = 0m,
            Status = UsageSessionStatuses.InUse
        };
        db.UsageSessions.Add(session);
        computer.Status = ComputerStatuses.InUse;
        computer.LastHeartbeatAtUtc = now.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return SessionResult<UsageSessionSummary>.Success(
            ToSummary(session, computer, account),
            "Đã bắt đầu phiên sử dụng.");
    }

    public async Task<SessionResult<UsageSessionSummary>> EndAsync(
        AuthenticatedUser actor,
        long sessionId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (sessionId <= 0)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_INVALID_ID",
                "Mã phiên không hợp lệ.");
        }

        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireLockAsync(
            db,
            $"NetCafeManager:UsageSession:Session:{sessionId}",
            cancellationToken);

        UsageSession? session = await db.UsageSessions
            .Include(row => row.Account)
            .Include(row => row.Computer)
            .SingleOrDefaultAsync(row => row.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_NOT_FOUND",
                "Không tìm thấy phiên sử dụng.");
        }

        if (actor.Role != UserRole.Administrator && actor.Id != session.AccountId)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_FORBIDDEN",
                "Không được kết thúc phiên của tài khoản khác.");
        }

        if (session.EndedAtUtc is not null ||
            session.Status == UsageSessionStatuses.Ended)
        {
            return SessionResult<UsageSessionSummary>.Failure(
                "SESSION_ALREADY_ENDED",
                "Phiên sử dụng đã kết thúc.");
        }

        await AcquireLockAsync(
            db,
            $"NetCafeManager:UsageSession:Account:{session.AccountId:N}",
            cancellationToken);
        await AcquireLockAsync(
            db,
            $"NetCafeManager:UsageSession:Computer:{session.ComputerId}",
            cancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset startedAt = new(session.StartedAtUtc, TimeSpan.Zero);
        double elapsedMinutes = Math.Max(0d, (now - startedAt).TotalMinutes);
        decimal billedMinutes = Math.Max(1m, (decimal)Math.Ceiling(elapsedMinutes));
        decimal calculatedCost = decimal.Round(
            session.HourlyRateApplied * billedMinutes / 60m,
            2,
            MidpointRounding.AwayFromZero);
        decimal chargedCost = Math.Min(session.Account.Balance, calculatedCost);

        session.Account.Balance -= chargedCost;
        session.UsageCost = calculatedCost;
        session.EndedAtUtc = now.UtcDateTime;
        session.Status = UsageSessionStatuses.Ended;
        session.EndReason = chargedCost < calculatedCost
            ? "HetTien"
            : NormalizeReason(reason);
        session.Computer.Status = ComputerStatuses.Available;
        session.Computer.LastHeartbeatAtUtc = now.UtcDateTime;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SessionResult<UsageSessionSummary>.Success(
            ToSummary(session, session.Computer, session.Account),
            "Đã kết thúc phiên và cập nhật số dư.");
    }

    public async Task<UsageSessionSummary?> GetActiveAsync(
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = new NetCafeDbContext(_options);
        UsageSession? session = await db.UsageSessions
            .AsNoTracking()
            .Include(row => row.Account)
            .Include(row => row.Computer)
            .SingleOrDefaultAsync(
                row => row.AccountId == actor.Id &&
                       row.EndedAtUtc == null &&
                       row.Status != UsageSessionStatuses.Ended,
                cancellationToken);
        return session is null
            ? null
            : ToSummary(session, session.Computer, session.Account);
    }

    private static Task<int> AcquireLockAsync(
        NetCafeDbContext db,
        string resource,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            IF @LockResult < 0
                THROW 51002, N'Không thể khóa phiên sử dụng.', 1;
            """, cancellationToken);

    private static string NormalizeReason(string? reason)
    {
        string normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return "DangXuat";
        }

        return normalized.Length <= MaxEndReasonLength
            ? normalized
            : normalized[..MaxEndReasonLength];
    }

    private static UsageSessionSummary ToSummary(
        UsageSession session,
        Computer computer,
        Account account) =>
        new(
            session.Id,
            computer.Id,
            computer.Name,
            account.Id,
            account.Username,
            new DateTimeOffset(session.StartedAtUtc, TimeSpan.Zero),
            session.EndedAtUtc is null
                ? null
                : new DateTimeOffset(session.EndedAtUtc.Value, TimeSpan.Zero),
            session.HourlyRateApplied,
            session.UsageCost,
            account.Balance,
            session.Status,
            session.EndReason);
}
