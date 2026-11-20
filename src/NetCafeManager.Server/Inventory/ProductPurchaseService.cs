using System.Data;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Inventory;

namespace NetCafeManager.Server.Inventory;

public sealed class ProductPurchaseService
{
    private const decimal MaxMoney = 9_999_999_999.99m;
    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly TimeProvider _timeProvider;

    public ProductPurchaseService(
        string connectionString,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<InventoryResult<ProductPurchaseReceipt>> PurchaseAsync(
        AuthenticatedUser actor,
        long usageSessionId,
        PurchaseProductRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (actor.Role != UserRole.Client)
            return Fail("PURCHASE_FORBIDDEN", "Chỉ tài khoản khách hàng được mua sản phẩm.");
        if (request.CommandId == Guid.Empty || usageSessionId <= 0 ||
            request.ProductId <= 0 || request.Quantity is < 1 or > 1000)
            return Fail("PURCHASE_INVALID_INPUT", "Sản phẩm, số lượng hoặc phiên sử dụng không hợp lệ.");

        await using var db = new NetCafeDbContext(_options);
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        await AcquireLockAsync(
            db,
            $"NetCafeManager:ProductPurchase:Command:{request.CommandId:N}",
            cancellationToken);

        ServiceUsage? previous = await db.ServiceUsages
            .Include(row => row.UsageSession)
            .Include(row => row.ServiceItem)
            .SingleOrDefaultAsync(
                row => row.CommandId == request.CommandId,
                cancellationToken);
        if (previous is not null)
        {
            if (previous.UsageSession.AccountId != actor.Id ||
                previous.ServiceItemId != request.ProductId ||
                previous.Quantity != request.Quantity)
            {
                return Fail(
                    "PURCHASE_COMMAND_CONFLICT",
                    "Mã yêu cầu đã được sử dụng cho một giao dịch mua khác.");
            }

            return InventoryResult<ProductPurchaseReceipt>.Success(
                ToReceipt(previous),
                "Yêu cầu đã được xử lý trước đó; trả lại kết quả cũ.");
        }

        await AcquireLockAsync(
            db,
            $"NetCafeManager:ProductPurchase:Account:{actor.Id:N}",
            cancellationToken);
        await AcquireLockAsync(
            db,
            $"NetCafeManager:ProductPurchase:Product:{request.ProductId}",
            cancellationToken);

        Account? account = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == actor.Id && x.IsActive && x.Role == UserRole.Client.ToString(), cancellationToken);
        if (account is null)
            return Fail("PURCHASE_ACCOUNT_UNAVAILABLE", "Tài khoản không tồn tại hoặc đã bị khóa.");

        UsageSession? session = await db.UsageSessions.SingleOrDefaultAsync(
            x => x.Id == usageSessionId
              && x.AccountId == actor.Id
              && x.EndedAtUtc == null
              && x.Status == "DangSuDung",
            cancellationToken);
        if (session is null)
            return Fail("PURCHASE_SESSION_REQUIRED", "Không tìm thấy phiên sử dụng đang hoạt động của tài khoản.");

        ServiceItem? item = await db.ServiceItems.SingleOrDefaultAsync(
            x => x.Id == request.ProductId, cancellationToken);
        if (item is null || item.Type != ServiceItemTypes.Product || item.StockQuantity is null)
            return Fail("PURCHASE_PRODUCT_NOT_FOUND", "Sản phẩm không tồn tại hoặc không kinh doanh.");
        if (item.UnitPrice <= 0 || item.UnitPrice > MaxMoney)
            return Fail("PURCHASE_INVALID_PRICE", "Đơn giá sản phẩm không hợp lệ.");
        if (item.StockQuantity < request.Quantity)
            return Fail("PURCHASE_INSUFFICIENT_STOCK", "Số lượng tồn kho không đủ.");

        decimal total;
        try { total = checked(item.UnitPrice * request.Quantity); }
        catch (OverflowException) { return Fail("PURCHASE_AMOUNT_OVERFLOW", "Thành tiền vượt giới hạn."); }
        if (total > MaxMoney || account.Balance < total)
            return Fail("PURCHASE_INSUFFICIENT_BALANCE", "Số dư không đủ để thanh toán.");

        account.Balance -= total;
        item.StockQuantity -= request.Quantity;
        DateTime purchasedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var detail = new ServiceUsage
        {
            CommandId = request.CommandId,
            UsageSessionId = usageSessionId,
            ServiceItemId = item.Id,
            ServiceItem = item,
            Quantity = request.Quantity,
            UnitPriceApplied = item.UnitPrice,
            StockAfter = item.StockQuantity.Value,
            BalanceAfter = account.Balance,
            PurchasedAtUtc = purchasedAtUtc
        };
        db.ServiceUsages.Add(detail);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return InventoryResult<ProductPurchaseReceipt>.Success(
            ToReceipt(detail),
            "Mua sản phẩm thành công.");
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
                @LockTimeout = 10000;
            IF @LockResult < 0
                THROW 51002, N'Không thể khóa giao dịch mua sản phẩm.', 1;
            """, cancellationToken);

    private static ProductPurchaseReceipt ToReceipt(ServiceUsage detail) =>
        new(
            detail.Id,
            detail.ServiceItemId,
            detail.ServiceItem.Name,
            detail.Quantity,
            detail.UnitPriceApplied,
            detail.UnitPriceApplied * detail.Quantity,
            detail.StockAfter,
            detail.BalanceAfter,
            detail.UsageSessionId,
            new DateTimeOffset(detail.PurchasedAtUtc, TimeSpan.Zero));

    private static InventoryResult<ProductPurchaseReceipt> Fail(string code, string message) =>
        InventoryResult<ProductPurchaseReceipt>.Failure(code, message);
}

