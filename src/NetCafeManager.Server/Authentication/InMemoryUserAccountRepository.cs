using System.Collections.Concurrent;

namespace NetCafeManager.Server.Authentication;

public sealed class InMemoryUserAccountRepository : IUserAccountRepository
{
    private readonly ConcurrentDictionary<string, UserAccount> _accounts =
        new(StringComparer.OrdinalIgnoreCase);

    public InMemoryUserAccountRepository(IEnumerable<UserAccount>? initialAccounts = null)
    {
        if (initialAccounts is null)
        {
            return;
        }

        foreach (UserAccount account in initialAccounts)
        {
            _accounts[NormalizeUsername(account.Username)] = account;
        }
    }

    public Task<UserAccount?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _accounts.TryGetValue(NormalizeUsername(username), out UserAccount? account);
        return Task.FromResult(account);
    }

    public bool TryAdd(UserAccount account) =>
        _accounts.TryAdd(NormalizeUsername(account.Username), account);

    private static string NormalizeUsername(string username) => username.Trim();
}
