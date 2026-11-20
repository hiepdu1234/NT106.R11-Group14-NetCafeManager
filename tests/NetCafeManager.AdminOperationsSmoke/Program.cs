using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Server.Billing;
using NetCafeManager.Server.Customers;
using NetCafeManager.Server.Inventory;
using NetCafeManager.Server.Invoices;
using NetCafeManager.Server.Networking;
using NetCafeManager.Shared.Administration;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Billing;
using NetCafeManager.Shared.Customers;
using NetCafeManager.Shared.Inventory;
using NetCafeManager.Shared.Invoices;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

string connectionString = Environment.GetEnvironmentVariable(
    "NETCAFE_ADMIN_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException(
        "Thiếu NETCAFE_ADMIN_SMOKE_CONNECTION.");
var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
var hasher = new Pbkdf2PasswordHasher();
var passwordHash = hasher.HashPassword("MatKhau!123");
Guid adminId = Guid.NewGuid();
Guid existingCustomerId = Guid.NewGuid();
long endedSessionId;
string suffix = Guid.NewGuid().ToString("N")[..8];

await using (var db = new NetCafeDbContext(options))
{
    db.Accounts.AddRange(
        NewAccount(
            adminId,
            $"admin-{suffix}",
            $"admin-{suffix}@example.test",
            nameof(UserRole.Administrator),
            0m),
        NewAccount(
            existingCustomerId,
            $"client-{suffix}",
            $"client-{suffix}@example.test",
            nameof(UserRole.Client),
            25_000m));
    var computer = new Computer
    {
        Name = $"ADMIN-SMOKE-{suffix}",
        Status = ComputerStatuses.Available
    };
    db.Computers.Add(computer);
    await db.SaveChangesAsync();

    var session = new UsageSession
    {
        ComputerId = computer.Id,
        AccountId = existingCustomerId,
        StartedAtUtc = DateTime.UtcNow.AddHours(-1),
        EndedAtUtc = DateTime.UtcNow,
        HourlyRateApplied = 10_000m,
        UsageCost = 10_000m,
        Status = UsageSessionStatuses.Ended,
        EndReason = "Smoke"
    };
    db.UsageSessions.Add(session);
    await db.SaveChangesAsync();
    endedSessionId = session.Id;
}

byte[] otpSecret = RandomNumberGenerator.GetBytes(32);
try
{
    AuthenticationServices authentication =
        AuthenticationSetup.CreateServicesForSqlServer(
            connectionString,
            new NoOpCodeSender(),
            otpSecret);
    LoginResponse login = await authentication.Authentication.LoginAsync(
        new LoginRequest($"admin-{suffix}", "MatKhau!123"));
    Require(
        login.IsSuccess && login.User is not null && login.SessionToken is not null,
        "Tài khoản quản trị smoke phải đăng nhập được.");

    var handler = new AdminOperationsTcpHandler(
        new CustomerManagementService(connectionString),
        new HourPackageService(connectionString),
        new TopUpService(connectionString),
        new ProductInventoryService(connectionString),
        new InvoiceService(connectionString),
        new BalanceRefundService(connectionString),
        authentication.Authentication);
    var context = new TcpConnectionContext
    {
        User = login.User,
        SessionToken = login.SessionToken
    };

    NetworkMessage forbidden = (await handler.HandleAsync(
        Message(NetworkMessageTypes.AdminCustomerList, new { }),
        new TcpConnectionContext()))!;
    Require(
        forbidden.Type == NetworkMessageTypes.Error,
        "Kết nối chưa đăng nhập không được gọi lệnh quản trị.");

    IReadOnlyList<CustomerSummary> customers = Deserialize<IReadOnlyList<CustomerSummary>>(
        (await handler.HandleAsync(
            Message(NetworkMessageTypes.AdminCustomerList, new { }),
            context))!);
    Require(
        customers.Any(customer => customer.Id == existingCustomerId),
        "TCP quản trị phải tải được danh sách khách hàng.");

    CustomerResult<CustomerSummary> created = Deserialize<CustomerResult<CustomerSummary>>(
        (await handler.HandleAsync(
            Message(
                NetworkMessageTypes.AdminCustomerCreate,
                new CreateCustomerRequest(
                    $"new-{suffix}",
                    $"new-{suffix}@example.test",
                    "Khách hàng TCP smoke",
                    "MatKhau!123")),
            context))!);
    Require(created.IsSuccess && created.Value is not null,
        "TCP quản trị phải tạo được khách hàng.");
    Guid createdCustomerId = created.Value!.Id;

    BillingResult<HourPackageSummary> savedPackage =
        Deserialize<BillingResult<HourPackageSummary>>(
            (await handler.HandleAsync(
                Message(
                    NetworkMessageTypes.AdminHourPackageSave,
                    new SaveHourPackageRequest(
                        null,
                        $"Gói TCP {suffix}",
                        5,
                        50_000m)),
                context))!);
    Require(savedPackage.IsSuccess && savedPackage.Value is not null,
        "TCP quản trị phải lưu được gói giờ.");

    BillingResult<TopUpReceipt> topUp = Deserialize<BillingResult<TopUpReceipt>>(
        (await handler.HandleAsync(
            Message(
                NetworkMessageTypes.AdminTopUp,
                new TopUpRequest(
                    createdCustomerId,
                    savedPackage.Value!.Id,
                    "TCP smoke",
                    Guid.NewGuid())),
            context))!);
    Require(topUp.IsSuccess && topUp.Value?.NewBalance == 50_000m,
        "TCP quản trị phải nạp đúng giá trị gói giờ.");

    InventoryResult<ServiceItemSummary> savedItem =
        Deserialize<InventoryResult<ServiceItemSummary>>(
            (await handler.HandleAsync(
                Message(
                    NetworkMessageTypes.AdminInventorySave,
                    new SaveServiceItemRequest(
                        null,
                        $"Nước TCP {suffix}",
                        ServiceItemTypes.Product,
                        "Chai",
                        12_000m,
                        5,
                        20)),
                context))!);
    Require(savedItem.IsSuccess && savedItem.Value is not null,
        "TCP quản trị phải tạo được sản phẩm.");

    InventoryResult<ServiceItemSummary> adjusted =
        Deserialize<InventoryResult<ServiceItemSummary>>(
            (await handler.HandleAsync(
                Message(
                    NetworkMessageTypes.AdminInventoryAdjust,
                    new AdjustStockRequest(savedItem.Value!.Id, 3)),
                context))!);
    Require(adjusted.Value?.StockQuantity == 8,
        "TCP quản trị phải điều chỉnh đúng tồn kho.");

    InvoiceResult<InvoiceSummary> invoice =
        Deserialize<InvoiceResult<InvoiceSummary>>(
            (await handler.HandleAsync(
                Message(
                    NetworkMessageTypes.AdminInvoiceCreate,
                    new CreateInvoiceRequest(endedSessionId, 0m)),
                context))!);
    Require(invoice.IsSuccess && invoice.Value is not null,
        "TCP quản trị phải lập được hóa đơn.");

    InvoiceResult<InvoiceSummary> paid =
        Deserialize<InvoiceResult<InvoiceSummary>>(
            (await handler.HandleAsync(
                Message(
                    NetworkMessageTypes.AdminInvoiceSetStatus,
                    new InvoiceStatusRequest(
                        invoice.Value!.Id,
                        InvoiceStatuses.Paid)),
                context))!);
    Require(paid.Value?.Status == InvoiceStatuses.Paid,
        "TCP quản trị phải xác nhận được hóa đơn.");

    BillingResult<BalanceRefundReceipt> refund =
        Deserialize<BillingResult<BalanceRefundReceipt>>(
            (await handler.HandleAsync(
                Message(
                    NetworkMessageTypes.AdminRefundCreate,
                    new BalanceRefundRequest(
                        createdCustomerId,
                        RefundMethods.Cash,
                        "Đóng tài khoản TCP smoke",
                        Guid.NewGuid())),
                context))!);
    Require(
        refund.IsSuccess && refund.Value?.Amount == 50_000m &&
        refund.Value.AccountClosed,
        "TCP quản trị phải hoàn tiền và đóng tài khoản.");

    IReadOnlyList<BalanceRefundHistoryItem> refundHistory =
        Deserialize<IReadOnlyList<BalanceRefundHistoryItem>>(
            (await handler.HandleAsync(
                Message(
                    NetworkMessageTypes.AdminRefundHistory,
                    new CustomerIdRequest(createdCustomerId)),
                context))!);
    Require(refundHistory.Count == 1,
        "TCP quản trị phải trả lịch sử hoàn tiền.");
}
finally
{
    CryptographicOperations.ZeroMemory(otpSecret);
}

Console.WriteLine("Admin operations TCP smoke test passed.");

NetworkMessage Message(string type, object value) => new(
    Guid.NewGuid().ToString("N"),
    type,
    "ADMIN-SMOKE",
    null,
    JsonSerializer.SerializeToElement(value));

T Deserialize<T>(NetworkMessage response) =>
    response.Data.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidOperationException(
        $"Không đọc được phản hồi {typeof(T).Name}.");

Account NewAccount(
    Guid id,
    string username,
    string email,
    string role,
    decimal balance) => new()
{
    Id = id,
    Username = username,
    Email = email,
    FullName = username,
    Role = role,
    Balance = balance,
    IsActive = true,
    PasswordAlgorithm = passwordHash.Algorithm,
    PasswordIterations = passwordHash.Iterations,
    PasswordSaltBase64 = passwordHash.SaltBase64,
    PasswordHashBase64 = passwordHash.HashBase64
};

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class NoOpCodeSender : IPasswordResetCodeSender
{
    public Task SendAsync(
        string email,
        string otp,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
