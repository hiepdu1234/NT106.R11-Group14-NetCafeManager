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

    public ProductPurchaseService(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
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
        if (usageSessionId <= 0 || request.ProductId <= 0 || request.Quantity is < 1 or > 1000)
            return Fail("PURCHASE_INVALID_INPUT", "Sản phẩm, số lượng hoặc phiên sử dụng không hợp lệ.");

        await using var db = new NetCafeDbContext(_options);
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        string lockResource = $"NetCafeManager:ProductPurchase:{request.ProductId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = {lockResource}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @LockResult < 0
                THROW 51002, N'Không thể khóa sản phẩm để thanh toán.', 1;
            """, cancellationToken);

        Account? account = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == actor.Id && x.IsActive && x.Role == UserRole.Client.ToString(), cancellationToken);
        if (account is null)
            return Fail("PURCHASE_ACCOUNT_UNAVAILABLE", "Tài khoản không tồn tại hoặc đã bị khóa.");

        UsageSession? session = await db.UsageSessions.SingleOrDefaultAsync(
            x => x.Id == usageSessionId && x.AccountId == actor.Id && x.EndedAtUtc == null,
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
        var detail = new ServiceUsage
        {
            UsageSessionId = usageSessionId,
            ServiceItemId = item.Id,
            Quantity = request.Quantity,
            UnitPriceApplied = item.UnitPrice
        };
        db.ServiceUsages.Add(detail);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return InventoryResult<ProductPurchaseReceipt>.Success(
            new ProductPurchaseReceipt(
                detail.Id, item.Id, item.Name, request.Quantity, item.UnitPrice,
                total, item.StockQuantity.Value, account.Balance, usageSessionId,
                DateTimeOffset.UtcNow),
            "Mua sản phẩm thành công.");
    }

    private static InventoryResult<ProductPurchaseReceipt> Fail(string code, string message) =>
        InventoryResult<ProductPurchaseReceipt>.Failure(code, message);
}
