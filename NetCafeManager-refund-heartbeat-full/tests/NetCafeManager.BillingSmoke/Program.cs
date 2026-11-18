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

int zeroPricePackageId;
await using (var db = new NetCafeDbContext(options))
{
    var zeroPricePackage = new HourPackage
    {
        Name = $"Goi mien phi smoke {Guid.NewGuid():N}",
        Hours = 1,
        Price = 0m,
        IsActive = true
    };
    db.HourPackages.Add(zeroPricePackage);
    await db.SaveChangesAsync();
    zeroPricePackageId = zeroPricePackage.Id;
}

BillingResult<TopUpReceipt> zeroPriceResult = await topUps.TopUpAsync(
    administrator,
    new TopUpRequest(customerId, zeroPricePackageId, "Zero price", Guid.NewGuid()));
Assert(!zeroPriceResult.IsSuccess && zeroPriceResult.ErrorCode == "TOPUP_INVALID_PACKAGE",
    "Không được nạp gói có giá bằng 0.");

var topUpRequest = new TopUpRequest(customerId, selected.Id, "Smoke test", Guid.NewGuid());
BillingResult<TopUpReceipt> receipt = await topUps.TopUpAsync(
    administrator,
    topUpRequest);
if (!receipt.IsSuccess || receipt.Value is null)
{
    throw new InvalidOperationException("Nạp gói giờ phải thành công.");
}

TopUpReceipt topUpReceipt = receipt.Value;
Assert(topUpReceipt.Amount == selected.Price && topUpReceipt.NewBalance == selected.Price,
    "Số dư phải tăng đúng bằng giá gói.");

BillingResult<TopUpReceipt> repeatedTopUp = await topUps.TopUpAsync(administrator, topUpRequest);
Assert(repeatedTopUp.IsSuccess && repeatedTopUp.Value?.TransactionId == topUpReceipt.TransactionId,
    "Gửi lại cùng CommandId phải trả về giao dịch cũ, không cộng tiền lần hai.");

BillingResult<TopUpReceipt> commandConflict = await topUps.TopUpAsync(
    administrator,
    new TopUpRequest(customerId, selected.Id, "Different note", topUpRequest.CommandId));
Assert(!commandConflict.IsSuccess && commandConflict.ErrorCode == "TOPUP_COMMAND_CONFLICT",
    "Reusing a CommandId with different data must be rejected.");

var concurrentRequest = new TopUpRequest(
    customerId,
    selected.Id,
    "Concurrent retry",
    Guid.NewGuid());
Task<BillingResult<TopUpReceipt>> concurrentFirst = topUps.TopUpAsync(
    administrator,
    concurrentRequest);
Task<BillingResult<TopUpReceipt>> concurrentSecond = topUps.TopUpAsync(
    administrator,
    concurrentRequest);
BillingResult<TopUpReceipt>[] concurrentResults = await Task.WhenAll(
    concurrentFirst,
    concurrentSecond);
Assert(concurrentResults.All(result => result.IsSuccess && result.Value is not null),
    "Concurrent retries must both return a successful result.");
Assert(concurrentResults[0].Value!.TransactionId ==
       concurrentResults[1].Value!.TransactionId,
    "Concurrent retries must return the same transaction.");
Assert(concurrentResults[0].Value!.NewBalance == selected.Price * 2,
    "Concurrent retries must add the balance only once.");

IReadOnlyList<TopUpHistoryItem> history = await topUps.GetHistoryAsync(client, customerId);
Assert(history.Count == 2 &&
       history.Any(item => item.TransactionId == topUpReceipt.TransactionId) &&
       history.Any(item => item.TransactionId == concurrentResults[0].Value!.TransactionId),
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
