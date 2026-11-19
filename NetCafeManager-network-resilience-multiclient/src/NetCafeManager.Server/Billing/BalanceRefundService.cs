using System.Data;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Billing;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Billing;

public sealed class BalanceRefundService
{
    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly TimeProvider _timeProvider;

    public BalanceRefundService(
        string connectionString,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BillingResult<BalanceRefundReceipt>> RefundAllAndCloseAsync(
        AuthenticatedUser actor,
        BalanceRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return Failure(
                "REFUND_FORBIDDEN",
                "Chi quan tri vien duoc hoan tien va dong tai khoan.");
        }

        string method = request.Method?.Trim() ?? string.Empty;
        string reason = request.Reason?.Trim() ?? string.Empty;
        if (request.CommandId == Guid.Empty || request.CustomerId == Guid.Empty)
        {
            return Failure(
                "REFUND_INVALID_REQUEST",
                "Ma yeu cau hoac tai khoan khach hang khong hop le.");
        }

        if (!RefundMethods.IsSupported(method))
        {
            return Failure(
                "REFUND_INVALID_METHOD",
                "Phuong thuc hoan tien phai la TienMat hoac ChuyenKhoan.");
        }

        if (reason.Length is 0 or > 500)
        {
            return Failure(
                "REFUND_INVALID_REASON",
                "Ly do hoan tien la bat buoc va khong duoc vuot qua 500 ky tu.");
        }

        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await AcquireLockAsync(
            db,
            $"NetCafeManager:Refund:Command:{request.CommandId:N}",
            cancellationToken);
        await AcquireLockAsync(
            db,
            $"NetCafeManager:Refund:Account:{request.CustomerId:N}",
            cancellationToken);

        BalanceRefund? previous = await db.BalanceRefunds
            .Include(row => row.Account)
            .SingleOrDefaultAsync(
                row => row.CommandId == request.CommandId,
                cancellationToken);
        if (previous is not null)
        {
            if (previous.AccountId != request.CustomerId ||
                previous.PerformedByAccountId != actor.Id ||
                previous.Method != method ||
                previous.Reason != reason)
            {
                return Failure(
                    "REFUND_COMMAND_CONFLICT",
                    "Ma yeu cau da duoc dung cho mot giao dich khac.");
            }

            return BillingResult<BalanceRefundReceipt>.Success(
                ToReceipt(previous),
                "Yeu cau da duoc xu ly truoc do; tra lai ket qua cu.");
        }

        Account? administrator = await db.Accounts.SingleOrDefaultAsync(
            account => account.Id == actor.Id &&
                       account.Role == nameof(UserRole.Administrator) &&
                       account.IsActive,
            cancellationToken);
        if (administrator is null)
        {
            return Failure(
                "REFUND_ADMIN_NOT_FOUND",
                "Tai khoan quan tri vien khong con hop le.");
        }

        Account? customer = await db.Accounts.SingleOrDefaultAsync(
            account => account.Id == request.CustomerId &&
                       account.Role == nameof(UserRole.Client),
            cancellationToken);
        if (customer is null)
        {
            return Failure(
                "REFUND_CUSTOMER_NOT_FOUND",
                "Khong tim thay tai khoan khach hang.");
        }

        if (!customer.IsActive)
        {
            return Failure(
                "REFUND_CUSTOMER_INACTIVE",
                "Tai khoan khach hang da bi khoa hoac da dong.");
        }

        bool hasActiveSession = await db.UsageSessions.AnyAsync(
            session => session.AccountId == customer.Id &&
                       session.EndedAtUtc == null &&
                       session.Status != UsageSessionStatuses.Ended,
            cancellationToken);
        if (hasActiveSession)
        {
            return Failure(
                "REFUND_ACTIVE_SESSION",
                "Phai ket thuc phien su dung truoc khi hoan tien.");
        }

        if (customer.Balance <= 0m)
        {
            return Failure(
                "REFUND_NO_BALANCE",
                "Tai khoan khong con so du de hoan.");
        }

        decimal balanceBefore = customer.Balance;
        customer.Balance = 0m;
        customer.IsActive = false;

        var refund = new BalanceRefund
        {
            CommandId = request.CommandId,
            AccountId = customer.Id,
            Account = customer,
            Amount = balanceBefore,
            BalanceBefore = balanceBefore,
            BalanceAfter = 0m,
            PerformedByAccountId = administrator.Id,
            PerformedByAccount = administrator,
            Method = method,
            Reason = reason,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };
        db.BalanceRefunds.Add(refund);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure(
                "REFUND_SAVE_FAILED",
                "Khong the ghi nhan giao dich hoan tien. Hay kiem tra va thu lai.");
        }

        return BillingResult<BalanceRefundReceipt>.Success(
            ToReceipt(refund),
            "Da hoan toan bo so du va dong tai khoan khach hang.");
    }

    public async Task<IReadOnlyList<BalanceRefundHistoryItem>> GetHistoryAsync(
        AuthenticatedUser actor,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        bool canRead = AdministratorGuard.IsAdministrator(actor) ||
                       actor.Id == customerId;
        if (!canRead)
        {
            throw new UnauthorizedAccessException(
                "Khong duoc xem lich su hoan tien cua tai khoan khac.");
        }

        await using var db = new NetCafeDbContext(_options);
        return await db.BalanceRefunds
            .AsNoTracking()
            .Where(row => row.AccountId == customerId)
            .OrderByDescending(row => row.CreatedAtUtc)
            .Select(row => new BalanceRefundHistoryItem(
                row.Id,
                row.Amount,
                row.Method,
                row.Reason,
                row.PerformedByAccount.Username,
                new DateTimeOffset(row.CreatedAtUtc, TimeSpan.Zero)))
            .ToListAsync(cancellationToken);
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
                THROW 51004, N'Khong the khoa giao dich hoan tien.', 1;
            """, cancellationToken);

    private static BalanceRefundReceipt ToReceipt(BalanceRefund refund) => new(
        refund.Id,
        refund.AccountId,
        refund.Amount,
        refund.BalanceBefore,
        refund.BalanceAfter,
        refund.Method,
        refund.Reason,
        new DateTimeOffset(refund.CreatedAtUtc, TimeSpan.Zero),
        AccountClosed: true);

    private static BillingResult<BalanceRefundReceipt> Failure(
        string code,
        string message) =>
        BillingResult<BalanceRefundReceipt>.Failure(code, message);
}
