using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Authentication;

public interface IUserAccountRepository
{
    Task<UserAccount?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<bool> UpdatePasswordHashAsync(
        Guid accountId,
        PasswordHash passwordHash,
        CancellationToken cancellationToken = default);
}
