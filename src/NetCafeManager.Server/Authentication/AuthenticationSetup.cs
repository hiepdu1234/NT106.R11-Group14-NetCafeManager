using NetCafeManager.Infrastructure.Security;

namespace NetCafeManager.Server.Authentication;

public static class AuthenticationSetup
{
    // Create once for the Server so all requests use the same session manager.
    public static AuthenticationService CreateForSqlServer(
        string connectionString,
        TimeProvider? timeProvider = null) =>
        new(
            new SqlServerUserAccountRepository(connectionString),
            new Pbkdf2PasswordHasher(),
            new SessionManager(),
            timeProvider);
}
