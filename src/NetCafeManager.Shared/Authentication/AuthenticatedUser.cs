namespace NetCafeManager.Shared.Authentication;

public sealed record AuthenticatedUser(
    Guid Id,
    string Username,
    UserRole Role,
    decimal Balance);
