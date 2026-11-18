namespace NetCafeManager.Shared.Customers;

public sealed record CreateCustomerRequest(
    string? Username,
    string? Email,
    string? FullName,
    string? Password);

public sealed record UpdateCustomerRequest(
    Guid CustomerId,
    string? Email,
    string? FullName,
    bool IsActive);

public sealed record CustomerSummary(
    Guid Id,
    string Username,
    string Email,
    string? FullName,
    decimal Balance,
    bool IsActive);

public sealed record CustomerResult<T>(
    bool IsSuccess,
    string? ErrorCode,
    string Message,
    T? Value)
{
    public static CustomerResult<T> Success(T value, string message) =>
        new(true, null, message, value);

    public static CustomerResult<T> Failure(string errorCode, string message) =>
        new(false, errorCode, message, default);
}
