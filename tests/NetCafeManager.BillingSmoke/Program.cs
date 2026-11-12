using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Billing;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Billing;

string connectionString = Environment.GetEnvironmentVariable("NETCAFE_BILLING_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException("Thiếu NETCAFE_BILLING_SMOKE_CONNECTION.");
var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
var hasher = new Pbkdf2PasswordHasher();
var adminId = Guid.NewGuid();
var customerId = Guid.NewGuid();

await using (var db = new NetCafeDbContext(options))
{
    var hash = hasher.HashPassword("MatKhau!123");
    db.Accounts.AddRange(
        NewAccount(adminId, "admin-billing", "admin-billing@example.test", nameof(UserRole.Administrator), hash),
        NewAccount(customerId, "client-billing", "client-billing@example.test", nameof(UserRole.Client), hash));
    await db.SaveChangesAsync();
}

var administrator = new AuthenticatedUser(adminId, "admin-billing", UserRole.Administrator, 0m);
var client = new AuthenticatedUser(customerId, "client-billing", UserRole.Client, 0m);
var packages = new HourPackageService(connectionString);
var topUps = new TopUpService(connectionString);
IReadOnlyList<HourPackageSummary> activePackages = await packages.ListActiveAsync();
Assert(activePackages.Count > 0, "Phải có ít nhất một gói giờ đang hoạt động.");
HourPackageSummary selected = activePackages[0];

BillingResult<TopUpReceipt> receipt = await topUps.TopUpAsync(
    administrator,
    new TopUpRequest(customerId, selected.Id, "Smoke test"));
if (!receipt.IsSuccess || receipt.Value is null)
{
    throw new InvalidOperationException("Nạp gói giờ phải thành công.");
}

TopUpReceipt topUpReceipt = receipt.Value;
Assert(topUpReceipt.Amount == selected.Price && topUpReceipt.NewBalance == selected.Price,
    "Số dư phải tăng đúng bằng giá gói.");

IReadOnlyList<TopUpHistoryItem> history = await topUps.GetHistoryAsync(client, customerId);
Assert(history.Count == 1 && history[0].TransactionId == topUpReceipt.TransactionId,
    "Khách hàng phải xem được lịch sử nạp của chính mình.");

BalanceWarningState warning = BalanceWarningEvaluator.Evaluate(2_000m, 10_000m);
Assert(warning.ShouldWarn, "Số dư chỉ đủ 12 phút phải phát cảnh báo.");

Console.WriteLine("Billing smoke test passed.");

static Account NewAccount(
    Guid id,
    string username,
    string email,
    string role,
    NetCafeManager.Shared.Security.PasswordHash hash) => new()
{
    Id = id,
    Username = username,
    Email = email,
    FullName = username,
    Role = role,
    IsActive = true,
    Balance = 0m,
    PasswordAlgorithm = hash.Algorithm,
    PasswordIterations = hash.Iterations,
    PasswordSaltBase64 = hash.SaltBase64,
    PasswordHashBase64 = hash.HashBase64
};

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
