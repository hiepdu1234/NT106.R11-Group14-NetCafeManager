using System.Security.Cryptography;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Infrastructure.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const string AlgorithmName = "PBKDF2-HMAC-SHA256";

    // 100.000 vòng phù hợp cho bản demo học thuật. Khi triển khai thực tế,
    // cần đo hiệu năng trên máy chủ và tăng số vòng theo chính sách bảo mật.
    public const int DefaultIterations = 100_000;

    private const int SaltSizeInBytes = 32;
    private const int HashSizeInBytes = 32;
    private const int MaximumAcceptedIterations = 10_000_000;

    public PasswordHash HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeInBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSizeInBytes);

        return new PasswordHash(
            AlgorithmName,
            DefaultIterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, PasswordHash storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(storedHash);

        if (!string.Equals(storedHash.Algorithm, AlgorithmName, StringComparison.Ordinal) ||
            storedHash.Iterations <= 0 ||
            storedHash.Iterations > MaximumAcceptedIterations)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(storedHash.SaltBase64);
            byte[] expectedHash = Convert.FromBase64String(storedHash.HashBase64);

            if (salt.Length != SaltSizeInBytes || expectedHash.Length != HashSizeInBytes)
            {
                return false;
            }

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                storedHash.Iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            bool isValid = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            CryptographicOperations.ZeroMemory(actualHash);
            return isValid;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
