using NetCafeManager.Shared.Authentication;

namespace NetCafeManager.Server.Authentication;

public sealed record UserSession(
    string Token,
    Guid UserId,
    string Username,
    UserRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
