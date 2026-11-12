namespace NetCafeManager.Shared.Billing;

public sealed record HourPackageSummary(
    int Id,
    string Name,
    int Hours,
    decimal Price,
    bool IsActive);

public sealed record SaveHourPackageRequest(
    int? PackageId,
    string? Name,
    int Hours,
    decimal Price,
    bool IsActive = true);

public sealed record TopUpRequest(
    Guid CustomerId,
    int HourPackageId,
    string? Note);

public sealed record TopUpReceipt(
    long TransactionId,
    Guid CustomerId,
    string PackageName,
    int HoursAdded,
    decimal Amount,
    decimal NewBalance,
    DateTimeOffset CreatedAt);

public sealed record TopUpHistoryItem(
    long TransactionId,
    string PackageName,
    int HoursAdded,
    decimal Amount,
    string PerformedBy,
    DateTimeOffset CreatedAt,
    string? Note);

public sealed record BalanceWarningState(
    bool ShouldWarn,
    TimeSpan EstimatedRemaining,
    string Message);

public sealed record BillingResult<T>(
    bool IsSuccess,
    string? ErrorCode,
    string Message,
    T? Value)
{
    public static BillingResult<T> Success(T value, string message) =>
        new(true, null, message, value);

    public static BillingResult<T> Failure(string errorCode, string message) =>
        new(false, errorCode, message, default);
}
