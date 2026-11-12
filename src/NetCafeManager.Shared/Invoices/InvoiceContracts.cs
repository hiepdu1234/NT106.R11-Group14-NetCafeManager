namespace NetCafeManager.Shared.Invoices;

public static class InvoiceStatuses
{
    public const string Unpaid = "ChuaThanhToan";
    public const string Paid = "DaThanhToan";
    public const string Debt = "CongNo";
}

public sealed record CreateInvoiceRequest(
    long UsageSessionId,
    decimal Discount);

public sealed record InvoiceSummary(
    long Id,
    long UsageSessionId,
    decimal UsageCost,
    decimal ServiceCost,
    decimal Discount,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    string Status);

public sealed record InvoiceResult<T>(
    bool IsSuccess,
    string? ErrorCode,
    string Message,
    T? Value)
{
    public static InvoiceResult<T> Success(T value, string message) =>
        new(true, null, message, value);

    public static InvoiceResult<T> Failure(string errorCode, string message) =>
        new(false, errorCode, message, default);
}
