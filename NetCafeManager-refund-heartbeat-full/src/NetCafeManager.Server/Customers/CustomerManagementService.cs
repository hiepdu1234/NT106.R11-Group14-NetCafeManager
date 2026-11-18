using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Customers;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Customers;

public sealed class CustomerManagementService
{
    private const int MinUsernameLength = 3;
    private const int MaxUsernameLength = 50;
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;
    private const int MaxFullNameLength = 100;

    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly IPasswordHasher _passwordHasher;

    public CustomerManagementService(
        string connectionString,
        IPasswordHasher? passwordHasher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _passwordHasher = passwordHasher ?? new Pbkdf2PasswordHasher();
    }

    public async Task<IReadOnlyList<CustomerSummary>> ListAsync(
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        EnsureAdministrator(actor);

        await using var db = new NetCafeDbContext(_options);
        return await db.Accounts
            .AsNoTracking()
            .Where(account => account.Role == nameof(UserRole.Client))
            .OrderBy(account => account.Username)
            .Select(account => new CustomerSummary(
                account.Id,
                account.Username,
                account.Email,
                account.FullName,
                account.Balance,
                account.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomerResult<CustomerSummary>> FindAsync(
        AuthenticatedUser actor,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_FORBIDDEN",
                "Chỉ quản trị viên được xem thông tin khách hàng.");
        }

        await using var db = new NetCafeDbContext(_options);
        Account? account = await db.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == customerId && row.Role == nameof(UserRole.Client),
                cancellationToken);

        return account is null
            ? CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_NOT_FOUND",
                "Không tìm thấy tài khoản khách hàng.")
            : CustomerResult<CustomerSummary>.Success(
                ToSummary(account),
                "Đã tải thông tin khách hàng.");
    }

    public async Task<CustomerResult<CustomerSummary>> CreateAsync(
        AuthenticatedUser actor,
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_FORBIDDEN",
                "Chỉ quản trị viên được tạo tài khoản khách hàng.");
        }

        string username = request.Username?.Trim() ?? string.Empty;
        string email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        string? fullName = NormalizeOptional(request.FullName);
        string password = request.Password ?? string.Empty;

        string? validationError = Validate(username, email, fullName, password);
        if (validationError is not null)
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_INVALID_INPUT",
                validationError);
        }

        await using var db = new NetCafeDbContext(_options);
        bool duplicate = await db.Accounts.AnyAsync(
            account => account.Username == username || account.Email == email,
            cancellationToken);
        if (duplicate)
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_DUPLICATE",
                "Tên đăng nhập hoặc email đã được sử dụng.");
        }

        PasswordHash hash = _passwordHasher.HashPassword(password);
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordAlgorithm = hash.Algorithm,
            PasswordIterations = hash.Iterations,
            PasswordSaltBase64 = hash.SaltBase64,
            PasswordHashBase64 = hash.HashBase64,
            Email = email,
            FullName = fullName,
            Role = nameof(UserRole.Client),
            Balance = 0m,
            IsActive = true
        };

        db.Accounts.Add(account);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_DUPLICATE",
                "Tên đăng nhập hoặc email đã được sử dụng.");
        }

        return CustomerResult<CustomerSummary>.Success(
            ToSummary(account),
            "Đã tạo tài khoản khách hàng.");
    }

    public async Task<CustomerResult<CustomerSummary>> UpdateAsync(
        AuthenticatedUser actor,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_FORBIDDEN",
                "Chỉ quản trị viên được cập nhật tài khoản khách hàng.");
        }

        string email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        string? fullName = NormalizeOptional(request.FullName);
        if (!IsValidEmail(email) || fullName?.Length > MaxFullNameLength)
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_INVALID_INPUT",
                "Email hoặc họ tên không hợp lệ.");
        }

        await using var db = new NetCafeDbContext(_options);
        Account? account = await db.Accounts.SingleOrDefaultAsync(
            row => row.Id == request.CustomerId && row.Role == nameof(UserRole.Client),
            cancellationToken);
        if (account is null)
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_NOT_FOUND",
                "Không tìm thấy tài khoản khách hàng.");
        }

        bool duplicateEmail = await db.Accounts.AnyAsync(
            row => row.Id != account.Id && row.Email == email,
            cancellationToken);
        if (duplicateEmail)
        {
            return CustomerResult<CustomerSummary>.Failure(
                "CUSTOMER_DUPLICATE_EMAIL",
                "Email đã được sử dụng bởi tài khoản khác.");
        }

        account.Email = email;
        account.FullName = fullName;
        account.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        return CustomerResult<CustomerSummary>.Success(
            ToSummary(account),
            "Đã cập nhật tài khoản khách hàng.");
    }

    private static void EnsureAdministrator(AuthenticatedUser actor)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            throw new UnauthorizedAccessException(
                "Chỉ quản trị viên được truy cập danh sách khách hàng.");
        }
    }

    private static string? Validate(
        string username,
        string email,
        string? fullName,
        string password)
    {
        if (username.Length is < MinUsernameLength or > MaxUsernameLength ||
            username.Any(char.IsWhiteSpace))
        {
            return "Tên đăng nhập phải từ 3 đến 50 ký tự và không chứa khoảng trắng.";
        }

        if (!IsValidEmail(email))
        {
            return "Email không hợp lệ.";
        }

        if (fullName?.Length > MaxFullNameLength)
        {
            return "Họ tên không được vượt quá 100 ký tự.";
        }

        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            return "Mật khẩu phải từ 8 đến 128 ký tự.";
        }

        return null;
    }

    private static bool IsValidEmail(string email)
    {
        if (email.Length is 0 or > 255)
        {
            return false;
        }

        try
        {
            return string.Equals(new MailAddress(email).Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 0 ? null : normalized;
    }

    private static CustomerSummary ToSummary(Account account) =>
        new(
            account.Id,
            account.Username,
            account.Email,
            account.FullName,
            account.Balance,
            account.IsActive);
}
