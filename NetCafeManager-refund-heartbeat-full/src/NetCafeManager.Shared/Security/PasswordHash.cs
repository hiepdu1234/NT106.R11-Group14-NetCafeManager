namespace NetCafeManager.Shared.Security;

public sealed record PasswordHash(
    string Algorithm,
    int Iterations,
    string SaltBase64,
    string HashBase64);
