using NetCafeManager.Shared.Security;
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

    public Task<bool> UpdatePasswordHashAsync(
        Guid accountId,
        PasswordHash passwordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);
        cancellationToken.ThrowIfCancellationRequested();

        UserAccount? account = _accounts.Values.FirstOrDefault(
            candidate => candidate.Id == accountId);
        if (account is null)
        {
            return Task.FromResult(false);
        }

        account.PasswordHash = passwordHash;
        return Task.FromResult(true);
    }

}
