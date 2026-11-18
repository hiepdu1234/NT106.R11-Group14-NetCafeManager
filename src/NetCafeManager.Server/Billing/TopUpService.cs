using System.Data;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Billing;

namespace NetCafeManager.Server.Billing;

public sealed class TopUpService
{
    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly TimeProvider _timeProvider;
    private const decimal MaxMoney = 9_999_999_999.99m;

    public TopUpService(string connectionString, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BillingResult<TopUpReceipt>> TopUpAsync(
        AuthenticatedUser actor,
        TopUpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_FORBIDDEN",
                "Chỉ quản trị viên được cộng tiền vào tài khoản khách hàng.");
        }

        if (request.CommandId == Guid.Empty || request.CustomerId == Guid.Empty || request.HourPackageId <= 0)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_INVALID_REQUEST",
                "Mã yêu cầu, tài khoản khách hàng hoặc gói giờ không hợp lệ.");
        }

        string? note;
        try
        {
            note = NormalizeNote(request.Note);
        }
        catch (ArgumentException)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_INVALID_NOTE",
                "Ghi chú không được vượt quá 500 ký tự.");
        }

        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await AcquireLockAsync(
            db,
            $"NetCafeManager:TopUp:Command:{request.CommandId:N}",
            cancellationToken);
        await AcquireLockAsync(
            db,
            $"NetCafeManager:TopUp:Account:{request.CustomerId:N}",
            cancellationToken);

        TopUpTransaction? previous = await db.TopUpTransactions
            .Include(row => row.HourPackage)
            .SingleOrDefaultAsync(row => row.CommandId == request.CommandId, cancellationToken);
        if (previous is not null)
        {
            if (previous.AccountId != request.CustomerId ||
                previous.PerformedByAccountId != actor.Id ||
                previous.HourPackageId != request.HourPackageId ||
                previous.Note != note)
            {
                return BillingResult<TopUpReceipt>.Failure(
                    "TOPUP_COMMAND_CONFLICT",
                    "Mã yêu cầu đã được sử dụng cho một giao dịch khác.");
            }

            return BillingResult<TopUpReceipt>.Success(
                ToReceipt(previous),
                "Yêu cầu đã được xử lý trước đó; trả lại kết quả cũ.");
        }

        Account? customer = await db.Accounts.SingleOrDefaultAsync(
            account => account.Id == request.CustomerId &&
                       account.Role == nameof(UserRole.Client),
            cancellationToken);
        if (customer is null)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_CUSTOMER_NOT_FOUND",
                "Không tìm thấy tài khoản khách hàng.");
        }

        if (!customer.IsActive)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_CUSTOMER_INACTIVE",
                "Không thể nạp cho tài khoản đang bị khóa.");
        }

        HourPackage? package = await db.HourPackages.SingleOrDefaultAsync(
            row => row.Id == request.HourPackageId,
            cancellationToken);
        if (package is null || !package.IsActive)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_PACKAGE_UNAVAILABLE",
                "Gói giờ không tồn tại hoặc đã ngừng áp dụng.");
        }

        if (package.Hours <= 0 || package.Price <= 0m || package.Price > MaxMoney)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_INVALID_PACKAGE",
                "Gói giờ phải có số giờ và giá tiền lớn hơn 0, nằm trong giới hạn cho phép.");
        }

        Account? administrator = await db.Accounts.SingleOrDefaultAsync(
            account => account.Id == actor.Id &&
                       account.Role == nameof(UserRole.Administrator) &&
                       account.IsActive,
            cancellationToken);
        if (administrator is null)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_ADMIN_NOT_FOUND",
                "Tài khoản quản trị viên không còn hợp lệ.");
        }

        decimal newBalance;
        try
        {
            newBalance = checked(customer.Balance + package.Price);
        }
        catch (OverflowException)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_BALANCE_OVERFLOW",
                "Số dư sau khi nạp vượt giới hạn cho phép.");
        }

        if (newBalance > MaxMoney)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_BALANCE_OVERFLOW",
                "Số dư sau khi nạp vượt giới hạn cho phép.");
        }

        customer.Balance = newBalance;
        var topUp = new TopUpTransaction
        {
            CommandId = request.CommandId,
            BalanceAfter = newBalance,
            AccountId = customer.Id,
            HourPackageId = package.Id,
            HourPackage = package,
            Amount = package.Price,
            HoursAdded = package.Hours,
            PerformedByAccountId = administrator.Id,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Note = note
        };
        db.TopUpTransactions.Add(topUp);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return BillingResult<TopUpReceipt>.Failure(
                "TOPUP_SAVE_FAILED",
                "Không thể ghi nhận giao dịch nạp tiền. Hãy kiểm tra mã yêu cầu và thử lại.");
        }

        return BillingResult<TopUpReceipt>.Success(
            ToReceipt(topUp),
            "Nạp gói giờ thành công.");
    }

    public async Task<IReadOnlyList<TopUpHistoryItem>> GetHistoryAsync(
        AuthenticatedUser actor,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        bool canRead = AdministratorGuard.IsAdministrator(actor) || actor.Id == customerId;
        if (!canRead)
        {
            throw new UnauthorizedAccessException("Không được xem lịch sử nạp của tài khoản khác.");
        }

        await using var db = new NetCafeDbContext(_options);
        return await db.TopUpTransactions
            .AsNoTracking()
            .Where(row => row.AccountId == customerId)
            .OrderByDescending(row => row.CreatedAtUtc)
            .Select(row => new TopUpHistoryItem(
                row.Id,
                row.HourPackage.Name,
                row.HoursAdded,
                row.Amount,
                row.PerformedByAccount.Username,
                new DateTimeOffset(row.CreatedAtUtc, TimeSpan.Zero),
                row.Note))
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
                THROW 51003, N'Không thể khóa giao dịch nạp tiền.', 1;
            """, cancellationToken);

    private static TopUpReceipt ToReceipt(TopUpTransaction transaction) => new(
        transaction.Id,
        transaction.AccountId,
        transaction.HourPackage.Name,
        transaction.HoursAdded,
        transaction.Amount,
        transaction.BalanceAfter,
        new DateTimeOffset(transaction.CreatedAtUtc, TimeSpan.Zero));

    private static string? NormalizeNote(string? note)
    {
        string normalized = note?.Trim() ?? string.Empty;
        if (normalized.Length > 500)
        {
            throw new ArgumentException("Ghi chú không được vượt quá 500 ký tự.", nameof(note));
        }

        return normalized.Length == 0 ? null : normalized;
    }
}
