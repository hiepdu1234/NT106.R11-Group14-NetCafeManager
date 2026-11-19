namespace NetCafeManager.Shared.Billing;

public static class RefundMethods
{
    public const string Cash = "TienMat";
    public const string BankTransfer = "ChuyenKhoan";

    public static bool IsSupported(string method) =>
        method is Cash or BankTransfer;
}

public sealed record BalanceRefundRequest(
    Guid CustomerId,
    string? Method,
    string? Reason,
    Guid CommandId);

public sealed record BalanceRefundReceipt(
    long TransactionId,
    Guid CustomerId,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string Method,
    string Reason,
    DateTimeOffset CreatedAt,
    bool AccountClosed);

public sealed record BalanceRefundHistoryItem(
    long TransactionId,
    decimal Amount,
    string Method,
    string Reason,
    string PerformedBy,
    DateTimeOffset CreatedAt);

