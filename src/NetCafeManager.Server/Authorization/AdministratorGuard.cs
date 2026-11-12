using NetCafeManager.Shared.Authentication;

namespace NetCafeManager.Server.Authorization;

public static class AdministratorGuard
{
    public static bool IsAdministrator(AuthenticatedUser? actor) =>
        actor is not null && actor.Role == UserRole.Administrator;
}
