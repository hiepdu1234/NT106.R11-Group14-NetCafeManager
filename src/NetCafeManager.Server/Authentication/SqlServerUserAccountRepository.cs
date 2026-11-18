using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Authentication;

public sealed class SqlServerUserAccountRepository : IUserAccountRepository
{
    private const int MaxUsernameLength = 50;
    private readonly DbContextOptions<NetCafeDbContext> _options;

    public SqlServerUserAccountRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    public async Task<UserAccount?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        cancellationToken.ThrowIfCancellationRequested();

        string lookup = username.Trim();
        if (lookup.Length is 0 or > MaxUsernameLength)
        {
            return null;
        }

        await using var db = new NetCafeDbContext(_options);
        Account? row = await db.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Username == lookup, cancellationToken);

        if (row is null)
        {
            return null;
        }

        UserRole? role = row.Role switch
        {
            nameof(UserRole.Administrator) => UserRole.Administrator,
            nameof(UserRole.Client) => UserRole.Client,
            _ => null
        };

        if (role is null)
        {
            return null;
        }

        return new UserAccount
        {
            Id = row.Id,
            Username = row.Username,
            Role = role.Value,
            IsActive = row.IsActive,
            Balance = row.Balance,
            PasswordHash = new PasswordHash(
                row.PasswordAlgorithm,
                row.PasswordIterations,
                row.PasswordSaltBase64,
                row.PasswordHashBase64)
        };
    }

    public async Task<bool> TryUpgradePasswordHashAsync(
        Guid accountId,
        string oldHash,
        PasswordHash passwordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(oldHash);
        cancellationToken.ThrowIfCancellationRequested();

        await using var db = new NetCafeDbContext(_options);

        // Cốt lõi của Optimistic Concurrency: Chỉ tìm Account nếu Hash trong DB 
        // vẫn khớp đúng với oldHash mà ta truyền vào.
        Account? row = await db.Accounts.SingleOrDefaultAsync(
            account => account.Id == accountId && account.PasswordHashBase64 == oldHash,
            cancellationToken);

        if (row is null)
        {
            // Nếu null, nghĩa là mật khẩu vừa bị ai đó đổi từ nơi khác
            return false;
        }

        row.PasswordAlgorithm = passwordHash.Algorithm;
        row.PasswordIterations = passwordHash.Iterations;
        row.PasswordSaltBase64 = passwordHash.SaltBase64;
        row.PasswordHashBase64 = passwordHash.HashBase64;

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

}
