
using System.Collections.Concurrent;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Authentication;

public sealed class InMemoryUserAccountRepository : IUserAccountRepository
{
    private readonly ConcurrentDictionary<string, UserAccount> _accounts =
        new(StringComparer.OrdinalIgnoreCase);

    public InMemoryUserAccountRepository(
        IEnumerable<UserAccount>? initialAccounts = null)
    {
        if (initialAccounts is null)
            return;

        foreach (UserAccount account in initialAccounts)
        {
            TryAdd(account);
        }
    }

    public bool TryAdd(UserAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return _accounts.TryAdd(
            NormalizeUsername(account.Username),
            account);
    }

    public Task<UserAccount?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(
            NormalizeUsername(username),
            out UserAccount? account))
        {
            return Task.FromResult<UserAccount?>(null);
        }

        lock (account)
        {
            // Trả snapshot để tránh đọc PasswordHash
            // đồng thời với thao tác nâng cấp hash.
            var snapshot = new UserAccount
            {
                Id = account.Id,
                Username = account.Username,
                Role = account.Role,
                PasswordHash = account.PasswordHash,
                Balance = account.Balance,
                IsActive = account.IsActive
            };

            return Task.FromResult<UserAccount?>(snapshot);
        }
    }

    public Task<bool> TryUpgradePasswordHashAsync(
        Guid accountId,
        string oldHash,
        PasswordHash newPasswordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldHash);
        ArgumentNullException.ThrowIfNull(newPasswordHash);

        cancellationToken.ThrowIfCancellationRequested();

        UserAccount? account = _accounts.Values.FirstOrDefault(
            candidate => candidate.Id == accountId);

        if (account is null)
            return Task.FromResult(false);

        lock (account)
        {
            // Chỉ nâng cấp nếu hash hiện tại vẫn là hash cũ.
            if (!string.Equals(
                account.PasswordHash.HashBase64,
                oldHash,
                StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            account.PasswordHash = newPasswordHash;

            return Task.FromResult(true);
        }
    }

    private static string NormalizeUsername(string username) =>
        username.Trim();
}
