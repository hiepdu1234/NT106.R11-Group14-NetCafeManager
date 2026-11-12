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
string originalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
string newClientPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
string suffix = Guid.NewGuid().ToString("N");
var admin = NewAccount("admin", UserRole.Administrator, true, 0m);
var client = NewAccount("client", UserRole.Client, true, 25000m);
var inactive = NewAccount("inactive", UserRole.Client, false, 0m);
var accounts = new[] { admin, client, inactive };
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
}
finally
{
    CryptographicOperations.ZeroMemory(otpHashSecret);
    cleanupDb.Accounts.RemoveRange(accounts);
    await cleanupDb.SaveChangesAsync();
}

Console.WriteLine("SQL Server authentication and OTP smoke test passed.");

Account NewAccount(string prefix, UserRole role, bool isActive, decimal balance)
{
    string username = $"{prefix}-{suffix}";
    PasswordHash hash = hasher.HashPassword(originalPassword);
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
