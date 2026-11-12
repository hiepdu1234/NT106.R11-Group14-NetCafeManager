using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Invoices;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Invoices;

string connectionString = Environment.GetEnvironmentVariable("NETCAFE_INVOICE_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException("Thiếu NETCAFE_INVOICE_SMOKE_CONNECTION.");
var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
var hasher = new Pbkdf2PasswordHasher();
var adminId = Guid.NewGuid();
var customerId = Guid.NewGuid();
long sessionId;

await using (var db = new NetCafeDbContext(options))
{
    var hash = hasher.HashPassword("MatKhau!123");
    db.Accounts.AddRange(
        NewAccount(adminId, "admin-invoice", "admin-invoice@example.test", nameof(UserRole.Administrator), hash),
        NewAccount(customerId, "client-invoice", "client-invoice@example.test", nameof(UserRole.Client), hash));
    var computer = new Computer { Name = "MAY-INVOICE", Status = "Trong" };
    db.Computers.Add(computer);
    await db.SaveChangesAsync();

    var session = new UsageSession
    {
        ComputerId = computer.Id,
        AccountId = customerId,
        StartedAtUtc = DateTime.UtcNow.AddHours(-2),
        EndedAtUtc = DateTime.UtcNow,
        HourlyRateApplied = 10_000m,
        UsageCost = 20_000m,
        Status = "KetThuc",
        EndReason = "KhachKetThuc"
    };
    db.UsageSessions.Add(session);
    await db.SaveChangesAsync();

    ServiceItem item = await db.ServiceItems.FirstAsync(row => row.Type == "SanPham");
    db.ServiceUsages.Add(new ServiceUsage
    {
        UsageSessionId = session.Id,
        ServiceItemId = item.Id,
        Quantity = 2,
        UnitPriceApplied = item.UnitPrice
    });
    await db.SaveChangesAsync();
    sessionId = session.Id;
}

var administrator = new AuthenticatedUser(adminId, "admin-invoice", UserRole.Administrator, 0m);
var service = new InvoiceService(connectionString);
InvoiceResult<InvoiceSummary> created = await service.CreateAsync(
    administrator,
    new CreateInvoiceRequest(sessionId, 1_000m));
if (!created.IsSuccess || created.Value is null)
{
    throw new InvalidOperationException("Phải lập được hóa đơn.");
}

InvoiceSummary createdInvoice = created.Value;
Assert(createdInvoice.TotalAmount == createdInvoice.UsageCost + createdInvoice.ServiceCost - 1_000m,
    "Tổng tiền hóa đơn phải đúng.");

InvoiceResult<InvoiceSummary> repeated = await service.CreateAsync(
    administrator,
    new CreateInvoiceRequest(sessionId, 1_000m));
Assert(repeated.IsSuccess && repeated.Value?.Id == createdInvoice.Id,
    "Lập lại phải trả về hóa đơn cũ, không tạo trùng.");

InvoiceResult<InvoiceSummary> paid = await service.SetStatusAsync(
    administrator,
    createdInvoice.Id,
    InvoiceStatuses.Paid);
Assert(paid.IsSuccess && paid.Value?.Status == InvoiceStatuses.Paid,
    "Phải đánh dấu hóa đơn đã thanh toán.");

Console.WriteLine("Invoice smoke test passed.");

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
