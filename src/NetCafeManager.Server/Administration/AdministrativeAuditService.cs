using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Administration;
using NetCafeManager.Shared.Authentication;

namespace NetCafeManager.Server.Administration;

public sealed class AdministrativeAuditService
{
    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly TimeProvider _clock;

    public AdministrativeAuditService(
        string connectionString,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<bool> TryRecordAsync(
        AuthenticatedUser actor,
        string action,
        string targetType,
        string? targetId,
        bool succeeded,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor)) return false;

        try
        {
            await using var db = new NetCafeDbContext(_options);
            db.AdministrativeAudits.Add(new AdministrativeAudit
            {
                AdministratorAccountId = actor.Id,
                Action = Limit(action, 80),
                TargetType = Limit(targetType, 50),
                TargetId = string.IsNullOrWhiteSpace(targetId)
                    ? null
                    : Limit(targetId.Trim(), 100),
                Succeeded = succeeded,
                Description = Limit(
                    string.IsNullOrWhiteSpace(description)
                        ? "Administrator command was processed."
                        : description.Trim(),
                    500),
                CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<AdministrativeAuditSummary>> ListRecentAsync(
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            throw new UnauthorizedAccessException("Administrator role is required.");
        }

        await using var db = new NetCafeDbContext(_options);
        return await db.AdministrativeAudits
            .AsNoTracking()
            .OrderByDescending(audit => audit.CreatedAtUtc)
            .Take(300)
            .Select(audit => new AdministrativeAuditSummary(
                audit.Id,
                audit.AdministratorAccount.Username,
                audit.Action,
                audit.TargetType,
                audit.TargetId,
                audit.Succeeded,
                audit.Description,
                new DateTimeOffset(audit.CreatedAtUtc, TimeSpan.Zero)))
            .ToListAsync(cancellationToken);
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

