using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Authentication;

public sealed class AuthenticationService
{
    private const string InvalidCredentialsCode = "AUTH_INVALID_CREDENTIALS";
    private const string InvalidCredentialsMessage = "Tên đăng nhập hoặc mật khẩu không đúng.";

    private readonly IUserAccountRepository _accounts;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SessionManager _sessions;
    private readonly TimeProvider _timeProvider;

    public AuthenticationService(
        IUserAccountRepository accounts,
        IPasswordHasher passwordHasher,
        SessionManager sessions,
        TimeProvider? timeProvider = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string username = request.Username?.Trim() ?? string.Empty;

        // Giới hạn mật khẩu từ 8 đến 64 ký tự để đảm bảo an toàn và chống DoS
        if (username.Length == 0 ||
            string.IsNullOrEmpty(request.Password) ||
            request.Password.Length < 8 ||
            request.Password.Length > 64)
        {
            return LoginResponse.Failure(InvalidCredentialsCode, InvalidCredentialsMessage);
        }

        UserAccount? account = await _accounts.FindByUsernameAsync(
            username,
            cancellationToken);

        if (account is null ||
            !account.IsActive ||
            !_passwordHasher.VerifyPassword(request.Password, account.PasswordHash))
        {
            return LoginResponse.Failure(InvalidCredentialsCode, InvalidCredentialsMessage);
        }

        if (_passwordHasher.NeedsRehash(account.PasswordHash))
        {
            PasswordHash upgradedHash = _passwordHasher.HashPassword(request.Password);

            // Sử dụng TryUpgradePasswordHashAsync với oldHash để chống Race Condition
            bool updated = await _accounts.TryUpgradePasswordHashAsync(
                account.Id,
                account.PasswordHash.HashBase64,
                upgradedHash,
                cancellationToken);

            if (!updated)
            {
                // Có xung đột: Đọc lại user và xác minh lại mật khẩu mới nhất
                account = await _accounts.FindByUsernameAsync(username, cancellationToken);

                if (account is null ||
                    !account.IsActive ||
                    !_passwordHasher.VerifyPassword(request.Password, account.PasswordHash))
                {
                    return LoginResponse.Failure(InvalidCredentialsCode, InvalidCredentialsMessage);
                }
            }
            else
            {
                account.PasswordHash = upgradedHash;
            }
        }

        UserSession session = _sessions.Create(account, _timeProvider.GetUtcNow());
        AuthenticatedUser user = new(
            account.Id,
            account.Username,
            account.Role,
            account.Balance);

        return LoginResponse.Success(session.Token, session.ExpiresAt, user);
    }

    public bool Logout(string sessionToken) => _sessions.Revoke(sessionToken);

    public bool TryValidateSession(string sessionToken, out UserSession? session) =>
        _sessions.TryValidate(sessionToken, _timeProvider.GetUtcNow(), out session);
}
