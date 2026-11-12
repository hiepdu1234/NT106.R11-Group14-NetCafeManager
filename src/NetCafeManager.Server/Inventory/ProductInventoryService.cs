using System.Data;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Inventory;

namespace NetCafeManager.Server.Inventory;

public sealed class ProductInventoryService
{
    private const int MaxNameLength = 100;
    private const int MaxUnitLength = 20;

    private readonly DbContextOptions<NetCafeDbContext> _options;

    public ProductInventoryService(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    public async Task<IReadOnlyList<ServiceItemSummary>> ListAsync(
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = new NetCafeDbContext(_options);
        List<ServiceItem> items = await db.ServiceItems
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return items.Select(ToSummary).ToList();
    }

    public async Task<InventoryResult<ServiceItemSummary>> SaveAsync(
        AuthenticatedUser actor,
        SaveServiceItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_FORBIDDEN",
                "Chỉ quản trị viên được tạo hoặc cập nhật sản phẩm, dịch vụ.");
        }

        string name = request.Name?.Trim() ?? string.Empty;
        string type = request.Type?.Trim() ?? string.Empty;
        string? unit = NormalizeOptional(request.Unit);
        string? validationError = Validate(request, name, type, unit);
        if (validationError is not null)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_INVALID_INPUT",
                validationError);
        }

        await using var db = new NetCafeDbContext(_options);
        ServiceItem? item = request.ItemId is null
            ? null
            : await db.ServiceItems.SingleOrDefaultAsync(
                row => row.Id == request.ItemId.Value,
                cancellationToken);
        if (request.ItemId is not null && item is null)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_NOT_FOUND",
                "Không tìm thấy sản phẩm hoặc dịch vụ.");
        }

        int currentItemId = item?.Id ?? 0;
        bool duplicate = await db.ServiceItems.AnyAsync(
            row => row.Name == name && row.Id != currentItemId,
            cancellationToken);
        if (duplicate)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_DUPLICATE_NAME",
                "Tên sản phẩm hoặc dịch vụ đã tồn tại.");
        }

        item ??= new ServiceItem
        {
            Name = name,
            Type = type
        };
        item.Name = name;
        item.Type = type;
        item.Unit = unit;
        item.UnitPrice = request.UnitPrice;
        item.StockQuantity = type == ServiceItemTypes.Product ? request.StockQuantity : null;
        item.ReferenceStockQuantity = type == ServiceItemTypes.Product
            ? request.ReferenceStockQuantity
            : null;

        if (item.Id == 0)
        {
            db.ServiceItems.Add(item);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_SAVE_FAILED",
                "Không thể lưu do tên bị trùng hoặc dữ liệu không hợp lệ.");
        }

        return InventoryResult<ServiceItemSummary>.Success(
            ToSummary(item),
            request.ItemId is null
                ? "Đã tạo sản phẩm hoặc dịch vụ."
                : "Đã cập nhật sản phẩm hoặc dịch vụ.");
    }

    public async Task<InventoryResult<ServiceItemSummary>> AdjustStockAsync(
        AuthenticatedUser actor,
        AdjustStockRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_FORBIDDEN",
                "Chỉ quản trị viên được điều chỉnh tồn kho.");
        }

        if (request.ItemId <= 0 || request.QuantityDelta == 0)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_INVALID_ADJUSTMENT",
                "Mã sản phẩm hoặc số lượng điều chỉnh không hợp lệ.");
        }

        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        string lockResource = $"NetCafeManager:Inventory:{request.ItemId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = {lockResource},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            IF @LockResult < 0
                THROW 51001, N'Không thể khóa sản phẩm để cập nhật tồn kho.', 1;
            """, cancellationToken);

        ServiceItem? item = await db.ServiceItems.SingleOrDefaultAsync(
            row => row.Id == request.ItemId,
            cancellationToken);
        if (item is null)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_NOT_FOUND",
                "Không tìm thấy sản phẩm.");
        }

        if (item.Type != ServiceItemTypes.Product || item.StockQuantity is null)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_NOT_PRODUCT",
                "Chỉ sản phẩm mới có số lượng tồn kho.");
        }

        int newQuantity;
        try
        {
            newQuantity = checked(item.StockQuantity.Value + request.QuantityDelta);
        }
        catch (OverflowException)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_QUANTITY_OVERFLOW",
                "Số lượng tồn kho vượt phạm vi cho phép.");
        }

        if (newQuantity < 0)
        {
            return InventoryResult<ServiceItemSummary>.Failure(
                "INVENTORY_INSUFFICIENT_STOCK",
                "Số lượng xuất vượt quá tồn kho hiện tại.");
        }

        item.StockQuantity = newQuantity;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InventoryResult<ServiceItemSummary>.Success(
            ToSummary(item),
            "Đã cập nhật tồn kho.");
    }

    public async Task<InventoryResult<bool>> DeleteAsync(
        AuthenticatedUser actor,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return InventoryResult<bool>.Failure(
                "INVENTORY_FORBIDDEN",
                "Chỉ quản trị viên được xóa sản phẩm hoặc dịch vụ.");
        }

        await using var db = new NetCafeDbContext(_options);
        ServiceItem? item = await db.ServiceItems.SingleOrDefaultAsync(
            row => row.Id == itemId,
            cancellationToken);
        if (item is null)
        {
            return InventoryResult<bool>.Failure(
                "INVENTORY_NOT_FOUND",
                "Không tìm thấy sản phẩm hoặc dịch vụ.");
        }

        bool hasUsage = await db.ServiceUsages.AnyAsync(
            row => row.ServiceItemId == itemId,
            cancellationToken);
        if (hasUsage)
        {
            return InventoryResult<bool>.Failure(
                "INVENTORY_ITEM_IN_USE",
                "Không thể xóa sản phẩm hoặc dịch vụ đã phát sinh sử dụng.");
        }

        db.ServiceItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return InventoryResult<bool>.Success(true, "Đã xóa sản phẩm hoặc dịch vụ.");
    }

    private static string? Validate(
        SaveServiceItemRequest request,
        string name,
        string type,
        string? unit)
    {
        if (name.Length is 0 or > MaxNameLength)
        {
            return "Tên phải có từ 1 đến 100 ký tự.";
        }

        if (type is not (ServiceItemTypes.Product or ServiceItemTypes.Overnight))
        {
            return "Loại chỉ có thể là sản phẩm hoặc dịch vụ ngủ qua đêm.";
        }

        if (unit?.Length > MaxUnitLength || request.UnitPrice < 0)
        {
            return "Đơn vị tính hoặc đơn giá không hợp lệ.";
        }

        if (type == ServiceItemTypes.Product &&
            (request.StockQuantity is null or < 0 ||
             request.ReferenceStockQuantity is null or <= 0))
        {
            return "Sản phẩm phải có tồn kho không âm và mức tồn chuẩn lớn hơn 0.";
        }

        if (type == ServiceItemTypes.Overnight &&
            (request.StockQuantity is not null || request.ReferenceStockQuantity is not null))
        {
            return "Dịch vụ ngủ qua đêm không sử dụng số lượng tồn kho.";
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 0 ? null : normalized;
    }

    private static ServiceItemSummary ToSummary(ServiceItem item) =>
        new(
            item.Id,
            item.Name,
            item.Type,
            item.Unit,
            item.UnitPrice,
            item.StockQuantity,
            item.ReferenceStockQuantity,
            EvaluateInventoryState(item));

    private static string EvaluateInventoryState(ServiceItem item)
    {
        if (item.Type != ServiceItemTypes.Product ||
            item.StockQuantity is null ||
            item.ReferenceStockQuantity is null)
        {
            return InventoryStates.NotApplicable;
        }

        return (decimal)item.StockQuantity.Value * 10m < item.ReferenceStockQuantity.Value
            ? InventoryStates.Low
            : InventoryStates.Normal;
    }
}
