using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Billing;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Billing;
using NetCafeManager.Shared.Sessions;

string connectionString = Environment.GetEnvironmentVariable(
    "NETCAFE_REFUND_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException(
        "Thieu NETCAFE_REFUND_SMOKE_CONNECTION.");
var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
var hasher = new Pbkdf2PasswordHasher();
var password = hasher.HashPassword("MatKhau!123");
Guid adminId = Guid.NewGuid();
Guid customerId = Guid.NewGuid();
Guid concurrentCustomerId = Guid.NewGuid();

await using (var db = new NetCafeDbContext(options))
{
    db.Accounts.AddRange(
        NewAccount(
            adminId,
            "admin-refund",
            "admin-refund@example.test",
            nameof(UserRole.Administrator),
            0m,
            password),
        NewAccount(
            customerId,
            "client-refund",
            "client-refund@example.test",
            nameof(UserRole.Client),
            50_000m,
            password),
        NewAccount(
            concurrentCustomerId,
            "client-refund-concurrent",
            "client-refund-concurrent@example.test",
            nameof(UserRole.Client),
            75_000m,
            password));
    await db.SaveChangesAsync();
}

var administrator = new AuthenticatedUser(
    adminId,
    "admin-refund",
    UserRole.Administrator,
    0m);
var customerActor = new AuthenticatedUser(
    customerId,
    "client-refund",
    UserRole.Client,
    50_000m);
var refunds = new BalanceRefundService(connectionString);

var request = new BalanceRefundRequest(
    customerId,
    RefundMethods.Cash,
    "Khach hang yeu cau dong tai khoan",
    Guid.NewGuid());
BillingResult<BalanceRefundReceipt> forbidden =
    await refunds.RefundAllAndCloseAsync(customerActor, request);
Require(
    !forbidden.IsSuccess && forbidden.ErrorCode == "REFUND_FORBIDDEN",
    "Khach hang khong duoc tu hoan tien.");

int computerId;
long sessionId;
await using (var db = new NetCafeDbContext(options))
{
    var computer = new Computer
    {
        Name = $"REFUND-SMOKE-{Guid.NewGuid():N}",
        Status = ComputerStatuses.InUse
    };
    db.Computers.Add(computer);
    await db.SaveChangesAsync();
    computerId = computer.Id;

    var session = new UsageSession
    {
        ComputerId = computer.Id,
        AccountId = customerId,
        StartedAtUtc = DateTime.UtcNow,
        HourlyRateApplied = 12_000m,
        UsageCost = 0m,
        Status = UsageSessionStatuses.InUse
    };
    db.UsageSessions.Add(session);
    await db.SaveChangesAsync();
    sessionId = session.Id;
}

BillingResult<BalanceRefundReceipt> activeSessionRejected =
    await refunds.RefundAllAndCloseAsync(administrator, request);
Require(
    !activeSessionRejected.IsSuccess &&
    activeSessionRejected.ErrorCode == "REFUND_ACTIVE_SESSION",
    "Khong duoc hoan tien khi khach hang con phien dang hoat dong.");

await using (var db = new NetCafeDbContext(options))
{
    UsageSession session = await db.UsageSessions.SingleAsync(
        row => row.Id == sessionId);
    session.Status = UsageSessionStatuses.Ended;
    session.EndedAtUtc = DateTime.UtcNow;
    session.EndReason = "DongTaiKhoan";
    Computer computer = await db.Computers.SingleAsync(
        row => row.Id == computerId);
    computer.Status = ComputerStatuses.Available;
    await db.SaveChangesAsync();
}

BillingResult<BalanceRefundReceipt> completed =
    await refunds.RefundAllAndCloseAsync(administrator, request);
Require(completed.IsSuccess && completed.Value is not null,
    "Hoan tien hop le phai thanh cong.");
BalanceRefundReceipt completedReceipt = completed.Value
    ?? throw new InvalidOperationException("Thieu phieu hoan tien.");
Require(
    completedReceipt.Amount == 50_000m &&
    completedReceipt.BalanceBefore == 50_000m &&
    completedReceipt.BalanceAfter == 0m &&
    completedReceipt.AccountClosed,
    "Phieu hoan tien phai ghi dung so du truoc va sau giao dich.");

BillingResult<BalanceRefundReceipt> repeated =
    await refunds.RefundAllAndCloseAsync(administrator, request);
Require(
    repeated.IsSuccess &&
    repeated.Value?.TransactionId == completedReceipt.TransactionId,
    "Gui lai cung CommandId phai tra giao dich cu, khong hoan lan hai.");

BillingResult<BalanceRefundReceipt> conflict =
    await refunds.RefundAllAndCloseAsync(
        administrator,
        request with { Reason = "Ly do khac" });
Require(
    !conflict.IsSuccess && conflict.ErrorCode == "REFUND_COMMAND_CONFLICT",
    "Khong duoc tai su dung CommandId cho noi dung khac.");

var concurrentRequest = new BalanceRefundRequest(
    concurrentCustomerId,
    RefundMethods.BankTransfer,
    "Dong tai khoan va chuyen khoan so du",
    Guid.NewGuid());
BillingResult<BalanceRefundReceipt>[] concurrent = await Task.WhenAll(
    refunds.RefundAllAndCloseAsync(administrator, concurrentRequest),
    refunds.RefundAllAndCloseAsync(administrator, concurrentRequest));
Require(
    concurrent.All(result => result.IsSuccess) &&
    concurrent.Select(result => result.Value!.TransactionId).Distinct().Count() == 1,
    "Hai yeu cau dong thoi cung CommandId chi duoc tao mot giao dich.");

await using (var db = new NetCafeDbContext(options))
{
    Account customer = await db.Accounts.SingleAsync(
        row => row.Id == customerId);
    int refundCount = await db.BalanceRefunds.CountAsync(
        row => row.AccountId == customerId);
    Require(
        customer.Balance == 0m && !customer.IsActive && refundCount == 1,
        "So du, trang thai tai khoan va lich su hoan tien phai cap nhat cung nhau.");
}

IReadOnlyList<BalanceRefundHistoryItem> history =
    await refunds.GetHistoryAsync(administrator, customerId);
Require(
    history.Count == 1 && history[0].Amount == 50_000m,
    "Quan tri vien phai xem duoc lich su hoan tien.");

Console.WriteLine("Balance refund smoke test passed.");

static Account NewAccount(
    Guid id,
    string username,
    string email,
    string role,
    decimal balance,
    NetCafeManager.Shared.Security.PasswordHash password) => new()
{
    Id = id,
    Username = username,
    Email = email,
    FullName = username,
    Role = role,
    Balance = balance,
    IsActive = true,
    PasswordAlgorithm = password.Algorithm,
    PasswordIterations = password.Iterations,
    PasswordSaltBase64 = password.SaltBase64,
    PasswordHashBase64 = password.HashBase64
};

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
