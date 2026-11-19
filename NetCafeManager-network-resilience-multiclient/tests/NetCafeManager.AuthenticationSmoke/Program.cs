using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Security;

string connectionString = Environment.GetEnvironmentVariable("NETCAFE_AUTH_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException("NETCAFE_AUTH_SMOKE_CONNECTION must be configured.");

if (!string.Equals(
    new SqlConnectionStringBuilder(connectionString).InitialCatalog,
    "NetCafeManager_CI",
    StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("The smoke test requires the NetCafeManager_CI test database.");
}

var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
var hasher = new Pbkdf2PasswordHasher();
var legacyHasher = new Pbkdf2PasswordHasher(
    Pbkdf2PasswordHasher.MinimumAcceptedIterations);
string originalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
string newClientPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
string suffix = Guid.NewGuid().ToString("N");
var admin = NewAccount("admin", UserRole.Administrator, true, 0m);
var client = NewAccount("client", UserRole.Client, true, 25000m);
var inactive = NewAccount("inactive", UserRole.Client, false, 0m);
var legacy = NewAccount(
    "legacy",
    UserRole.Client,
    true,
    10_000m,
    legacyHasher);

// Tài khoản dùng để kiểm thử thay đổi mật khẩu đồng thời.
var rehashRace = NewAccount(
    "rehash-race",
    UserRole.Client,
    true,
    0m,
    legacyHasher);

var accounts = new[]
{
    admin,
    client,
    inactive,
    legacy,
    rehashRace
};
var clock = new TestClock();
var sender = new CapturingCodeSender();
byte[] otpHashSecret = RandomNumberGenerator.GetBytes(32);

await using var cleanupDb = new NetCafeDbContext(options);
cleanupDb.Accounts.AddRange(accounts);
await cleanupDb.SaveChangesAsync();

try
{
    AuthenticationServices services = AuthenticationSetup.CreateServicesForSqlServer(
        connectionString,
        sender,
        otpHashSecret,
        clock);
    AuthenticationService auth = services.Authentication;
    PasswordResetService passwordReset = services.PasswordReset;

    LoginResponse adminLogin = await auth.LoginAsync(new LoginRequest(admin.Username, originalPassword));
    Require(
        adminLogin.IsSuccess &&
        adminLogin.User is { Role: UserRole.Administrator } adminUser &&
        adminUser.Id == admin.Id,
        "Admin login and role mapping");
    string adminToken = adminLogin.SessionToken
        ?? throw new InvalidOperationException("Missing admin session.");
    Require(
        auth.TryValidateSession(adminToken, out UserSession? adminSession) &&
        adminSession?.Role == UserRole.Administrator,
        "Admin session validation");

    LoginResponse clientLogin = await auth.LoginAsync(
        new LoginRequest($" {client.Username} ", originalPassword));
    Require(
        clientLogin.IsSuccess &&
        clientLogin.User is { Role: UserRole.Client } clientUser &&
        clientUser.Id == client.Id &&
        clientUser.Balance == 25000m,
        "Client login, role and balance mapping");
    string clientToken = clientLogin.SessionToken
        ?? throw new InvalidOperationException("Missing client session.");

    LoginResponse legacyLogin = await auth.LoginAsync(
        new LoginRequest(legacy.Username, originalPassword));
    Require(legacyLogin.IsSuccess, "Legacy PBKDF2 account can log in");
    await using (var verificationDb = new NetCafeDbContext(options))
    {
        int upgradedIterations = await verificationDb.Accounts
            .Where(account => account.Id == legacy.Id)
            .Select(account => account.PasswordIterations)
            .SingleAsync();
        Require(
            upgradedIterations == Pbkdf2PasswordHasher.DefaultIterations,
            "Legacy PBKDF2 hash is upgraded after login");
    }

    await RequireDenied(new LoginRequest(client.Username, originalPassword + "-wrong"), "Wrong password");
    await RequireDenied(new LoginRequest(inactive.Username, originalPassword), "Inactive account");
    await RequireDenied(new LoginRequest($"missing-{suffix}", originalPassword), "Unknown account");
    await RequireDenied(new LoginRequest(new string('x', 51), originalPassword), "Username length limit");

    int sentBeforeUnknownRequest = sender.Count;
    PasswordResetResponse unknownRequest = await passwordReset.RequestAsync(
        new RequestPasswordResetRequest($"missing-{suffix}", UserRole.Client));
    Require(
        unknownRequest.IsSuccess && sender.Count == sentBeforeUnknownRequest,
        "Unknown account receives generic response without sending email");

    PasswordResetResponse requested = await passwordReset.RequestAsync(
        new RequestPasswordResetRequest(client.Email, UserRole.Client));
    Require(requested.IsSuccess, "Client OTP request is accepted");
    string clientOtp = sender.GetLatest(client.Email);

    PasswordResetResponse wrongOtp = await passwordReset.VerifyOtpAsync(
        new VerifyPasswordResetOtpRequest(client.Username, UserRole.Client, WrongOtp(clientOtp)));
    Require(!wrongOtp.IsSuccess && wrongOtp.ErrorCode == "AUTH_INVALID_OTP", "Wrong OTP is rejected");

    PasswordResetResponse verified = await passwordReset.VerifyOtpAsync(
        new VerifyPasswordResetOtpRequest(client.Username, UserRole.Client, clientOtp));
    Require(verified.IsSuccess, "Correct OTP is verified");

    // Kiểm tra từ chối mật khẩu 65 ký tự khi reset.
    PasswordResetResponse rejectedLongPassword =
        await passwordReset.CompleteAsync(
            new CompletePasswordResetRequest(
                client.Username,
                UserRole.Client,
                clientOtp,
                new string('x', 65)));

    Require(
        !rejectedLongPassword.IsSuccess &&
        rejectedLongPassword.ErrorCode == "AUTH_INVALID_NEW_PASSWORD",
        "Password reset must reject a 65-character password.");

    PasswordResetResponse completed = await passwordReset.CompleteAsync(
        new CompletePasswordResetRequest(
            client.Username,
            UserRole.Client,
            clientOtp,
            newClientPassword));
    Require(completed.IsSuccess, "Password reset completes");
    Require(!auth.TryValidateSession(clientToken, out _), "Password reset revokes old sessions");

    await RequireDenied(
        new LoginRequest(client.Username, originalPassword),
        "Old password is rejected after reset");
    LoginResponse newPasswordLogin = await auth.LoginAsync(
        new LoginRequest(client.Username, newClientPassword));
    Require(newPasswordLogin.IsSuccess, "New password can be used");

    PasswordResetResponse reusedOtp = await passwordReset.CompleteAsync(
        new CompletePasswordResetRequest(
            client.Username,
            UserRole.Client,
            clientOtp,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(24))));
    Require(!reusedOtp.IsSuccess, "Used OTP cannot be reused");

    await passwordReset.RequestAsync(
        new RequestPasswordResetRequest(admin.Username, UserRole.Administrator));
    string blockedOtp = sender.GetLatest(admin.Email);
    for (int attempt = 1; attempt <= 5; attempt++)
    {
        PasswordResetResponse failedAttempt = await passwordReset.VerifyOtpAsync(
            new VerifyPasswordResetOtpRequest(
                admin.Username,
                UserRole.Administrator,
                WrongOtp(blockedOtp)));
        Require(!failedAttempt.IsSuccess, $"Wrong OTP attempt {attempt} is rejected");
    }

    PasswordResetResponse blockedCode = await passwordReset.VerifyOtpAsync(
        new VerifyPasswordResetOtpRequest(admin.Username, UserRole.Administrator, blockedOtp));
    Require(!blockedCode.IsSuccess, "OTP is blocked after five failed attempts");

    await passwordReset.RequestAsync(
        new RequestPasswordResetRequest(admin.Email, UserRole.Administrator));
    string expiringOtp = sender.GetLatest(admin.Email);
    clock.Now += TimeSpan.FromMinutes(6);
    PasswordResetResponse expiredCode = await passwordReset.VerifyOtpAsync(
        new VerifyPasswordResetOtpRequest(admin.Email, UserRole.Administrator, expiringOtp));
    Require(!expiredCode.IsSuccess, "OTP expires after five minutes");

    Require(auth.Logout(adminToken) && !auth.TryValidateSession(adminToken, out _), "Logout revokes session");

    async Task RequireDenied(LoginRequest request, string scenario)
    {
        LoginResponse result = await auth.LoginAsync(request);
        Require(
            !result.IsSuccess &&
            result.SessionToken is null &&
            result.User is null &&
            result.ErrorCode == "AUTH_INVALID_CREDENTIALS",
            scenario);
    }
    await RunInMemoryLegacyHashUpgradeTestAsync();

    await RunSqlConcurrentPasswordChangeRehashTestAsync(rehashRace);
}
finally
{
    CryptographicOperations.ZeroMemory(otpHashSecret);
    cleanupDb.Accounts.RemoveRange(accounts);
    await cleanupDb.SaveChangesAsync();
}

Console.WriteLine("SQL Server authentication and OTP smoke test passed.");

Account NewAccount(
    string prefix,
    UserRole role,
    bool isActive,
    decimal balance,
    Pbkdf2PasswordHasher? passwordHasher = null)
{
    string username = $"{prefix}-{suffix}";
    PasswordHash hash = (passwordHasher ?? hasher).HashPassword(originalPassword);
    return new Account
    {
        Id = Guid.NewGuid(),
        Username = username,
        Email = $"{username}@example.invalid",
        Role = role.ToString(),
        Balance = balance,
        IsActive = isActive,
        PasswordAlgorithm = hash.Algorithm,
        PasswordIterations = hash.Iterations,
        PasswordSaltBase64 = hash.SaltBase64,
        PasswordHashBase64 = hash.HashBase64
    };
}
async Task RunInMemoryLegacyHashUpgradeTestAsync()
{
    const string password = "Legacy-Password-2026!";

    var legacyHasher = new Pbkdf2PasswordHasher(
        Pbkdf2PasswordHasher.MinimumAcceptedIterations);

    var currentHasher = new Pbkdf2PasswordHasher(
        Pbkdf2PasswordHasher.DefaultIterations);

    PasswordHash oldHash = legacyHasher.HashPassword(password);

    var account = new UserAccount
    {
        Id = Guid.NewGuid(),
        Username = $"memory-legacy-{Guid.NewGuid():N}",
        PasswordHash = oldHash,
        Role = UserRole.Client,
        Balance = 1000m,
        IsActive = true
    };

    var repository = new InMemoryUserAccountRepository(
        new[] { account });

    var authentication = new AuthenticationService(
        repository,
        currentHasher,
        new SessionManager());

    LoginResponse login = await authentication.LoginAsync(
        new LoginRequest(account.Username, password));

    Require(
        login.IsSuccess,
        "In-memory account can log in using legacy password hash");

    UserAccount? upgraded = await repository.FindByUsernameAsync(
        account.Username);

    Require(
        upgraded is not null,
        "In-memory account still exists after login");

    Require(
        upgraded!.PasswordHash.Iterations ==
            Pbkdf2PasswordHasher.DefaultIterations,
        "In-memory legacy hash is upgraded to current iterations");

    Require(
        upgraded.PasswordHash.HashBase64 != oldHash.HashBase64,
        "In-memory password hash is replaced");

    Require(
        currentHasher.VerifyPassword(password, upgraded.PasswordHash),
        "Password remains valid after hash upgrade");

    Console.WriteLine(
        "PASS: In-memory legacy password hash upgrade");
}

async Task RunSqlConcurrentPasswordChangeRehashTestAsync(Account target)
{
    // Hash của mật khẩu mới do một luồng khác vừa thay đổi.
    PasswordHash replacementHash =
        hasher.HashPassword(newClientPassword);

    int passwordChangeCount = 0;

    var racingRepository = new BeforeRehashPasswordChangeRepository(
        new SqlServerUserAccountRepository(connectionString),
        async cancellationToken =>
        {
            // Mô phỏng luồng B thay đổi mật khẩu
            // sau khi luồng A đã đọc hash cũ.

            await using var changeDb = new NetCafeDbContext(options);

            Account row = await changeDb.Accounts.SingleAsync(
                account => account.Id == target.Id,
                cancellationToken);

            row.PasswordAlgorithm = replacementHash.Algorithm;
            row.PasswordIterations = replacementHash.Iterations;
            row.PasswordSaltBase64 = replacementHash.SaltBase64;
            row.PasswordHashBase64 = replacementHash.HashBase64;

            await changeDb.SaveChangesAsync(cancellationToken);

            Interlocked.Increment(ref passwordChangeCount);
        });

    var racingAuthentication = new AuthenticationService(
        racingRepository,
        hasher,
        new SessionManager(),
        clock);

    // Luồng A đăng nhập bằng mật khẩu cũ.
    LoginResponse result = await racingAuthentication.LoginAsync(
        new LoginRequest(target.Username, originalPassword));

    Require(
        passwordChangeCount == 1,
        "Concurrent password change must execute exactly once");

    Require(
        !result.IsSuccess &&
        result.SessionToken is null &&
        result.User is null &&
        result.ErrorCode == "AUTH_INVALID_CREDENTIALS",
        "Old password must be rejected after concurrent password change");

    // Đọc lại dữ liệu thực tế từ SQL Server.
    await using var verificationDb = new NetCafeDbContext(options);

    Account stored = await verificationDb.Accounts
        .AsNoTracking()
        .SingleAsync(account => account.Id == target.Id);

    var storedHash = new PasswordHash(
        stored.PasswordAlgorithm,
        stored.PasswordIterations,
        stored.PasswordSaltBase64,
        stored.PasswordHashBase64);

    Require(
        stored.PasswordAlgorithm == replacementHash.Algorithm &&
        stored.PasswordIterations == replacementHash.Iterations &&
        stored.PasswordSaltBase64 == replacementHash.SaltBase64 &&
        stored.PasswordHashBase64 == replacementHash.HashBase64,
        "Concurrent password change must not be overwritten by stale rehash");

    Require(
        hasher.VerifyPassword(newClientPassword, storedHash),
        "New password must remain valid");

    Require(
        !hasher.VerifyPassword(originalPassword, storedHash),
        "Old password must no longer be valid");

    Console.WriteLine(
        "PASS: SQL conditional rehash prevents concurrent password overwrite");
}

static string WrongOtp(string otp) => otp == "000000" ? "000001" : "000000";

static void Require(bool condition, string scenario)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Authentication smoke test failed: {scenario}.");
    }
}

sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

sealed class CapturingCodeSender : IPasswordResetCodeSender
{
    private readonly ConcurrentDictionary<string, string> _latestCodes =
        new(StringComparer.OrdinalIgnoreCase);
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public Task SendAsync(
        string email,
        string otp,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _latestCodes[email] = otp;
        Interlocked.Increment(ref _count);
        return Task.CompletedTask;
    }

    public string GetLatest(string email) =>
        _latestCodes.TryGetValue(email, out string? otp)
            ? otp
            : throw new InvalidOperationException($"No OTP was captured for {email}.");
}


sealed class BeforeRehashPasswordChangeRepository
    : IUserAccountRepository
{
    private readonly IUserAccountRepository _inner;
    private readonly Func<CancellationToken, Task> _beforeUpgrade;

    private int _executed;

    public BeforeRehashPasswordChangeRepository(
        IUserAccountRepository inner,
        Func<CancellationToken, Task> beforeUpgrade)
    {
        _inner = inner;
        _beforeUpgrade = beforeUpgrade;
    }

    public Task<UserAccount?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        return _inner.FindByUsernameAsync(
            username,
            cancellationToken);
    }

    public async Task<bool> TryUpgradePasswordHashAsync(
        Guid accountId,
        string oldHash,
        PasswordHash newPasswordHash,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _executed, 1) == 0)
        {
            await _beforeUpgrade(cancellationToken);
        }

        return await _inner.TryUpgradePasswordHashAsync(
            accountId,
            oldHash,
            newPasswordHash,
            cancellationToken);
    }
}

