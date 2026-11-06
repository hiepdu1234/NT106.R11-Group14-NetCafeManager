using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace NetCafeManager.Server.Authentication;

public sealed class SessionManager
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    private readonly ConcurrentDictionary<string, UserSession> _sessions =
        new(StringComparer.Ordinal);

    public UserSession Create(UserAccount account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);

        while (true)
        {
            string token = CreateSecureToken();
            UserSession session = new(
                token,
                account.Id,
                account.Username,
                account.Role,
                now,
                now.Add(DefaultLifetime));

            if (_sessions.TryAdd(token, session))
            {
                return session;
            }
        }
    }

    public bool TryValidate(string token, DateTimeOffset now, out UserSession? session)
    {
        session = null;

        if (string.IsNullOrWhiteSpace(token) ||
            !_sessions.TryGetValue(token, out UserSession? storedSession))
        {
            return false;
        }

        if (storedSession.ExpiresAt <= now)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        session = storedSession;
        return true;
    }

    public bool Revoke(string token) =>
        !string.IsNullOrWhiteSpace(token) && _sessions.TryRemove(token, out _);

    private static string CreateSecureToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
