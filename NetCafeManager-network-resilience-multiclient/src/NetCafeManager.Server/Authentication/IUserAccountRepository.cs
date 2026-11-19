
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Authentication;

public interface IUserAccountRepository
{
    Task<UserAccount?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<bool> TryUpgradePasswordHashAsync(
        Guid accountId,
        string oldHash,
        PasswordHash newPasswordHash,
        CancellationToken cancellationToken = default);
}
