namespace NetCafeManager.Shared.Authentication;

public sealed record LoginResponse(
    bool IsSuccess,
    string? SessionToken,
    DateTimeOffset? ExpiresAt,
    AuthenticatedUser? User,
    string? ErrorCode,
    string Message)
{
    public static LoginResponse Success(
        string sessionToken,
        DateTimeOffset expiresAt,
        AuthenticatedUser user) =>
        new(true, sessionToken, expiresAt, user, null, "Đăng nhập thành công.");

    public static LoginResponse Failure(string errorCode, string message) =>
        new(false, null, null, null, errorCode, message);
}
