using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Sessions;

string connectionString = RequireEnvironment("NETCAFE_DB_CONNECTION");
string clientUsername = ReadEnvironment(
    "NETCAFE_DEMO_CLIENT_USERNAME",
    "demo-client");
string clientEmail = RequireEnvironment("NETCAFE_DEMO_CLIENT_EMAIL");
string clientPassword = RequirePassword(
    "NETCAFE_DEMO_CLIENT_PASSWORD");
string adminUsername = ReadEnvironment(
    "NETCAFE_DEMO_ADMIN_USERNAME",
    "demo-admin");
string adminEmail = RequireEnvironment("NETCAFE_DEMO_ADMIN_EMAIL");
string adminPassword = RequirePassword(
    "NETCAFE_DEMO_ADMIN_PASSWORD");
bool replaceExisting = string.Equals(
    Environment.GetEnvironmentVariable("NETCAFE_DEMO_REPLACE_EXISTING"),
    "true",
    StringComparison.OrdinalIgnoreCase);

ValidateIdentifier(clientUsername, "NETCAFE_DEMO_CLIENT_USERNAME", 50);
ValidateIdentifier(adminUsername, "NETCAFE_DEMO_ADMIN_USERNAME", 50);
ValidateIdentifier(clientEmail, "NETCAFE_DEMO_CLIENT_EMAIL", 255);
ValidateIdentifier(adminEmail, "NETCAFE_DEMO_ADMIN_EMAIL", 255);
if (string.Equals(clientUsername, adminUsername, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(clientEmail, adminEmail, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Tài khoản Client và Administrator phải có username/email khác nhau.");
}

var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
await using var db = new NetCafeDbContext(options);
if (!await db.Database.CanConnectAsync())
{
    throw new InvalidOperationException(
        "Không kết nối được SQL Server. Kiểm tra NETCAFE_DB_CONNECTION.");
}

try
{
    _ = await db.Accounts.AsNoTracking().Take(1).ToListAsync();
}
catch (Exception exception)
{
    throw new InvalidOperationException(
        "Database chưa có schema. Hãy chạy database/migrations/001_initial_schema.sql trước.",
        exception);
}

var hasher = new Pbkdf2PasswordHasher();
await UpsertAccountAsync(
    clientUsername,
    clientEmail,
    clientPassword,
    UserRole.Client,
    50_000m);
await UpsertAccountAsync(
    adminUsername,
    adminEmail,
    adminPassword,
    UserRole.Administrator,
    0m);
await EnsureDemoComputerAsync("MAY-01");
await db.SaveChangesAsync();

Console.WriteLine("Đã chuẩn bị dữ liệu demo:");
Console.WriteLine($"- Client: {clientUsername} ({clientEmail}), số dư 50.000 đồng");
Console.WriteLine($"- Administrator: {adminUsername} ({adminEmail})");
Console.WriteLine("- Máy trạm: MAY-01");
Console.WriteLine("Mật khẩu không được in ra. Xem file .demo-secrets.ps1 trên máy của bạn.");

async Task UpsertAccountAsync(
    string username,
    string email,
    string password,
    UserRole role,
    decimal balance)
{
    Account? byUsername = await db.Accounts.SingleOrDefaultAsync(
        account => account.Username == username);
    Account? byEmail = await db.Accounts.SingleOrDefaultAsync(
        account => account.Email == email);
    if (byUsername is not null &&
        byEmail is not null &&
        byUsername.Id != byEmail.Id)
    {
        throw new InvalidOperationException(
            $"Username {username} và email {email} đang thuộc hai tài khoản khác nhau.");
    }

    Account? account = byUsername ?? byEmail;
    if (account is not null && !replaceExisting)
    {
        throw new InvalidOperationException(
            $"Tài khoản {username} đã tồn tại. Chỉ đặt " +
            "NETCAFE_DEMO_REPLACE_EXISTING=true nếu chắc chắn muốn thay dữ liệu demo.");
    }

    var hash = hasher.HashPassword(password);
    if (account is null)
    {
        account = new Account
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            FullName = role == UserRole.Client
                ? "Khách hàng demo"
                : "Quản trị viên demo",
            Role = role.ToString(),
            Balance = balance,
            IsActive = true,
            PasswordAlgorithm = hash.Algorithm,
            PasswordIterations = hash.Iterations,
            PasswordSaltBase64 = hash.SaltBase64,
            PasswordHashBase64 = hash.HashBase64
        };
        db.Accounts.Add(account);
        return;
    }

    account.Username = username;
    account.Email = email;
    account.FullName = role == UserRole.Client
        ? "Khách hàng demo"
        : "Quản trị viên demo";
    account.Role = role.ToString();
    account.Balance = balance;
    account.IsActive = true;
    account.PasswordAlgorithm = hash.Algorithm;
    account.PasswordIterations = hash.Iterations;
    account.PasswordSaltBase64 = hash.SaltBase64;
    account.PasswordHashBase64 = hash.HashBase64;
}

async Task EnsureDemoComputerAsync(string machineName)
{
    Computer? computer = await db.Computers.SingleOrDefaultAsync(
        candidate => candidate.Name == machineName);
    if (computer is null)
    {
        db.Computers.Add(new Computer
        {
            Name = machineName,
            IpAddress = "127.0.0.1",
            Status = ComputerStatuses.Available
        });
        return;
    }

    bool hasActiveSession = await db.UsageSessions.AnyAsync(
        session => session.ComputerId == computer.Id &&
                   session.EndedAtUtc == null &&
                   session.Status != UsageSessionStatuses.Ended);
    if (hasActiveSession)
    {
        throw new InvalidOperationException(
            $"Máy {machineName} đang có phiên hoạt động; không được đặt lại trạng thái demo.");
    }

    computer.Status = ComputerStatuses.Available;
    computer.IpAddress = "127.0.0.1";
    computer.MaintenanceReason = null;
}

static string RequirePassword(string variableName)
{
    string password = RequireEnvironment(variableName);
    if (password.Length is < 8 or > 128 || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            $"{variableName} phải có từ 8 đến 128 ký tự.");
    }

    return password;
}

static void ValidateIdentifier(
    string value,
    string variableName,
    int maximumLength)
{
    if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
    {
        throw new InvalidOperationException(
            $"{variableName} phải có từ 1 đến {maximumLength} ký tự.");
    }
}

static string ReadEnvironment(string variableName, string fallback) =>
    Environment.GetEnvironmentVariable(variableName)?.Trim() is { Length: > 0 } value
        ? value
        : fallback;

static string RequireEnvironment(string variableName) =>
    Environment.GetEnvironmentVariable(variableName)?.Trim() is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException(
            $"Thiếu biến môi trường {variableName}.");
