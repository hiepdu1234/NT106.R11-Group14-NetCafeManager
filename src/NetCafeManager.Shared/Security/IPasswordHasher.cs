namespace NetCafeManager.Shared.Security;

public interface IPasswordHasher
{
    PasswordHash HashPassword(string password);

    bool VerifyPassword(string password, PasswordHash storedHash);
}
