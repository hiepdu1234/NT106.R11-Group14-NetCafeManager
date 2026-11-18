using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Authentication;

public sealed class UserAccount
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public required PasswordHash PasswordHash { get; set; }

    public required UserRole Role { get; init; }

    public decimal Balance { get; set; }

    public bool IsActive { get; set; } = true;
}
