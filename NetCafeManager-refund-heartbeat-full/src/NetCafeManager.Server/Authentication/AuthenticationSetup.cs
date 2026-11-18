using NetCafeManager.Infrastructure.Security;

namespace NetCafeManager.Server.Authentication;

public sealed record AuthenticationServices(
    AuthenticationService Authentication,
    PasswordResetService PasswordReset);

public static class AuthenticationSetup
{
    // Giữ API cũ cho các nơi chỉ cần đăng nhập/đăng xuất.
    public static AuthenticationService CreateForSqlServer(
        string connectionString,
        TimeProvider? timeProvider = null,
        int passwordHashIterations = Pbkdf2PasswordHasher.DefaultIterations)
    {
        var sessions = new SessionManager();
        return new AuthenticationService(
            new SqlServerUserAccountRepository(connectionString),
            new Pbkdf2PasswordHasher(passwordHashIterations),
            sessions,
            timeProvider);
    }

    // Server chỉ tạo bộ dịch vụ này một lần để đăng nhập và đặt lại mật khẩu
    // dùng chung SessionManager. Nhờ đó đổi mật khẩu sẽ thu hồi mọi phiên cũ.
    public static AuthenticationServices CreateServicesForSqlServer(
        string connectionString,
        IPasswordResetCodeSender codeSender,
        byte[] otpHashSecret,
        TimeProvider? timeProvider = null,
        int passwordHashIterations = Pbkdf2PasswordHasher.DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(codeSender);
        ArgumentNullException.ThrowIfNull(otpHashSecret);

        var sessions = new SessionManager();
        var passwordHasher = new Pbkdf2PasswordHasher(passwordHashIterations);
        var authentication = new AuthenticationService(
            new SqlServerUserAccountRepository(connectionString),
            passwordHasher,
            sessions,
            timeProvider);
        var passwordReset = new PasswordResetService(
            connectionString,
            codeSender,
            passwordHasher,
            sessions,
            otpHashSecret,
            timeProvider);

        return new AuthenticationServices(authentication, passwordReset);
    }
}
