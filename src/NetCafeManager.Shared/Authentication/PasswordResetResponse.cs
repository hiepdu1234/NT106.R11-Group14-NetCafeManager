namespace NetCafeManager.Shared.Authentication;

public sealed record PasswordResetResponse(
    bool IsSuccess,
    string? ErrorCode,
    string Message)
{
    public static PasswordResetResponse RequestAccepted() =>
        new(
            true,
            null,
            "Nếu thông tin khớp với tài khoản, mã xác nhận sẽ được gửi đến email đã đăng ký.");

    public static PasswordResetResponse OtpVerified() =>
        new(true, null, "Mã xác nhận hợp lệ.");

    public static PasswordResetResponse Completed() =>
        new(true, null, "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại.");

    public static PasswordResetResponse Failure(string errorCode, string message) =>
        new(false, errorCode, message);
}
