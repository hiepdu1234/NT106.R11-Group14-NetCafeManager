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

        string? note = NormalizeNote(request.Note);
        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

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

        customer.Balance = checked(customer.Balance + package.Price);
        var topUp = new TopUpTransaction
        {
            AccountId = customer.Id,
            HourPackageId = package.Id,
            Amount = package.Price,
            HoursAdded = package.Hours,
            PerformedByAccountId = administrator.Id,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Note = note
        };
        db.TopUpTransactions.Add(topUp);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var receipt = new TopUpReceipt(
            topUp.Id,
            customer.Id,
            package.Name,
            package.Hours,
            package.Price,
            customer.Balance,
            new DateTimeOffset(topUp.CreatedAtUtc, TimeSpan.Zero));
        return BillingResult<TopUpReceipt>.Success(receipt, "Nạp gói giờ thành công.");
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
