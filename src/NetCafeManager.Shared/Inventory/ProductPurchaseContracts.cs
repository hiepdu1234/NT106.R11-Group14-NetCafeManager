namespace NetCafeManager.Shared.Inventory;

public sealed record PurchaseProductRequest(int ProductId, int Quantity);

public sealed record ProductPurchaseReceipt(
    long DetailId,
    int ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal TotalAmount,
    int RemainingStock,
    decimal RemainingBalance,
    long UsageSessionId,
    DateTimeOffset PurchasedAtUtc);
