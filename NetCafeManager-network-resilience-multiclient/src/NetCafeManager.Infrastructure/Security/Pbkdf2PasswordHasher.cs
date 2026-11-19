using System.Security.Cryptography;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Infrastructure.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const string AlgorithmName = "PBKDF2-HMAC-SHA256";

    public const int DefaultIterations = 600_000;
    public const int MinimumAcceptedIterations = 100_000;
    public const int MaximumAcceptedIterations = 10_000_000;

    private const int SaltSizeInBytes = 32;
    private const int HashSizeInBytes = 32;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < MinimumAcceptedIterations ||
            iterations > MaximumAcceptedIterations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                $"Số vòng PBKDF2 phải nằm trong khoảng {MinimumAcceptedIterations:N0}-{MaximumAcceptedIterations:N0}.");
        }

        _iterations = iterations;
    }

    public PasswordHash HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeInBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _iterations,
            HashAlgorithmName.SHA256,
            HashSizeInBytes);

        return new PasswordHash(
            AlgorithmName,
            _iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, PasswordHash storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(storedHash);

        if (!string.Equals(storedHash.Algorithm, AlgorithmName, StringComparison.Ordinal) ||
            storedHash.Iterations < MinimumAcceptedIterations ||
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

    public bool NeedsRehash(PasswordHash storedHash)
    {
        ArgumentNullException.ThrowIfNull(storedHash);

        return !string.Equals(
                   storedHash.Algorithm,
                   AlgorithmName,
                   StringComparison.Ordinal) ||
               storedHash.Iterations < _iterations ||
               storedHash.Iterations > MaximumAcceptedIterations;
    }

}
