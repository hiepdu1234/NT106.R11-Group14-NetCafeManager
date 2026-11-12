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

if (!string.Equals(new SqlConnectionStringBuilder(connectionString).InitialCatalog,
    "NetCafeManager_CI", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("The smoke test requires the NetCafeManager_CI test database.");
}

var options = new DbContextOptionsBuilder<NetCafeDbContext>().UseSqlServer(connectionString).Options;
var hasher = new Pbkdf2PasswordHasher();
string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
string suffix = Guid.NewGuid().ToString("N");
var admin = NewAccount("admin", UserRole.Administrator, true, 0m);
var client = NewAccount("client", UserRole.Client, true, 25000m);
var inactive = NewAccount("inactive", UserRole.Client, false, 0m);
var accounts = new[] { admin, client, inactive };
var clock = new TestClock();

await using var db = new NetCafeDbContext(options);
db.Accounts.AddRange(accounts);
await db.SaveChangesAsync();

try
{
    var auth = AuthenticationSetup.CreateForSqlServer(connectionString, clock);

    LoginResponse adminLogin = await auth.LoginAsync(new LoginRequest(admin.Username, password));
    Require(adminLogin.IsSuccess && adminLogin.User is { Role: UserRole.Administrator } adminUser
        && adminUser.Id == admin.Id, "Admin login and role mapping");
    string adminToken = adminLogin.SessionToken ?? throw new InvalidOperationException("Missing admin session.");
    Require(auth.TryValidateSession(adminToken, out var adminSession)
        && adminSession?.Role == UserRole.Administrator, "Admin session validation");

    LoginResponse clientLogin = await auth.LoginAsync(new LoginRequest($" {client.Username} ", password));
    Require(clientLogin.IsSuccess && clientLogin.User is { Role: UserRole.Client } clientUser
        && clientUser.Id == client.Id && clientUser.Balance == 25000m,
        "Client login, role and balance mapping");
    string clientToken = clientLogin.SessionToken ?? throw new InvalidOperationException("Missing client session.");

    await RequireDenied(new LoginRequest(client.Username, password + "-wrong"), "Wrong password");
    await RequireDenied(new LoginRequest(inactive.Username, password), "Inactive account");
    await RequireDenied(new LoginRequest($"missing-{suffix}", password), "Unknown account");
    await RequireDenied(new LoginRequest(new string('x', 51), password), "Username length limit");

    Require(auth.Logout(adminToken) && !auth.TryValidateSession(adminToken, out _), "Logout revokes session");
    clock.Now += TimeSpan.FromHours(8);
    Require(!auth.TryValidateSession(clientToken, out _), "Expired session is rejected");
    async Task RequireDenied(LoginRequest request, string scenario)
    {
        LoginResponse result = await auth.LoginAsync(request);
        Require(!result.IsSuccess && result.SessionToken is null && result.User is null
            && result.ErrorCode == "AUTH_INVALID_CREDENTIALS", scenario);
    }
}
finally
{
    db.Accounts.RemoveRange(accounts);
    await db.SaveChangesAsync();
}

Console.WriteLine("SQL Server authentication smoke test passed.");

Account NewAccount(string prefix, UserRole role, bool isActive, decimal balance)
{
    string username = $"{prefix}-{suffix}";
    PasswordHash hash = hasher.HashPassword(password);
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
