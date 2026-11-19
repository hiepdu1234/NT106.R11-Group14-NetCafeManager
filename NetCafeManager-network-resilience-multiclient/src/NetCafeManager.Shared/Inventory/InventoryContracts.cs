namespace NetCafeManager.Shared.Inventory;

public static class ServiceItemTypes
{
    public const string Product = "SanPham";
    public const string Overnight = "NguQuaDem";
}

public static class InventoryStates
{
    public const string NotApplicable = "KhongApDung";
    public const string Low = "It";
    public const string Normal = "Nhieu";
}

public sealed record ServiceItemSummary(
    int Id,
    string Name,
    string Type,
    string? Unit,
    decimal UnitPrice,
    int? StockQuantity,
    int? ReferenceStockQuantity,
    string InventoryState);

public sealed record SaveServiceItemRequest(
    int? ItemId,
    string? Name,
    string? Type,
    string? Unit,
    decimal UnitPrice,
    int? StockQuantity,
    int? ReferenceStockQuantity);

public sealed record AdjustStockRequest(
    int ItemId,
    int QuantityDelta);

public sealed record InventoryResult<T>(
    bool IsSuccess,
    string? ErrorCode,
    string Message,
    T? Value)
{
    public static InventoryResult<T> Success(T value, string message) =>
        new(true, null, message, value);

    public static InventoryResult<T> Failure(string errorCode, string message) =>
        new(false, errorCode, message, default);
}
