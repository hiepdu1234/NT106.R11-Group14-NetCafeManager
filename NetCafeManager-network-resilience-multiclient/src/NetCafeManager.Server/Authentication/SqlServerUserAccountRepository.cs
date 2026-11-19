
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Authentication;

/// <summary>
/// Repository đọc tài khoản và nâng cấp password hash bằng SQL Server.
/// </summary>
public sealed class SqlServerUserAccountRepository : IUserAccountRepository
{
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
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedUsername = username?.Trim() ?? string.Empty;

        if (normalizedUsername.Length == 0)
        {
            return null;
        }

        await using var db = new NetCafeDbContext(_options);

        Account? account = await db.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Username == normalizedUsername,
                cancellationToken);

        if (account is null)
        {
            return null;
        }

        // Chỉ chấp nhận đúng tên Role đã định nghĩa.
        UserRole? role = account.Role switch
        {
            nameof(UserRole.Administrator) => UserRole.Administrator,
            nameof(UserRole.Client) => UserRole.Client,
            _ => null
        };

        // Không tự động cấp quyền mặc định nếu Role không hợp lệ.
        if (role is null)
        {
            return null;
        }

        return new UserAccount
        {
            Id = account.Id,
            Username = account.Username,
            Role = role.Value,

            PasswordHash = new PasswordHash(
                account.PasswordAlgorithm,
                account.PasswordIterations,
                account.PasswordSaltBase64,
                account.PasswordHashBase64),

            // Phải giữ đầy đủ trạng thái và số dư tài khoản.
            Balance = account.Balance,
            IsActive = account.IsActive
        };
    }

    public async Task<bool> TryUpgradePasswordHashAsync(
        Guid accountId,
        string oldHash,
        PasswordHash newPasswordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldHash);
        ArgumentNullException.ThrowIfNull(newPasswordHash);

        cancellationToken.ThrowIfCancellationRequested();

        await using var db = new NetCafeDbContext(_options);

        // Conditional UPDATE:
        // Chỉ cập nhật khi hash hiện tại vẫn bằng oldHash.
        int rowsAffected = await db.Accounts
            .Where(account =>
                account.Id == accountId &&
                EF.Functions.Collate(
                    account.PasswordHashBase64,
                    "Latin1_General_100_BIN2") == oldHash)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        account => account.PasswordAlgorithm,
                        newPasswordHash.Algorithm)
                    .SetProperty(
                        account => account.PasswordIterations,
                        newPasswordHash.Iterations)
                    .SetProperty(
                        account => account.PasswordSaltBase64,
                        newPasswordHash.SaltBase64)
                    .SetProperty(
                        account => account.PasswordHashBase64,
                        newPasswordHash.HashBase64),
                cancellationToken);

        // Chỉ thành công khi đúng một tài khoản được cập nhật.
        return rowsAffected == 1;
    }
}
