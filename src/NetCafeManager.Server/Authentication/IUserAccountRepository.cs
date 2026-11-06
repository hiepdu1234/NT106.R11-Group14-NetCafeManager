namespace NetCafeManager.Server.Authentication;

public interface IUserAccountRepository
{
    Task<UserAccount?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default);
}
