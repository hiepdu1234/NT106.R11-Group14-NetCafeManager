using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Inventory;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Inventory;

string connectionString = Environment.GetEnvironmentVariable("NETCAFE_INVENTORY_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException("Thiếu NETCAFE_INVENTORY_SMOKE_CONNECTION.");
var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
var hasher = new Pbkdf2PasswordHasher();
var adminId = Guid.NewGuid();
var clientId = Guid.NewGuid();

await using (var db = new NetCafeDbContext(options))
{
    var hash = hasher.HashPassword("MatKhau!123");
    db.Accounts.AddRange(
        NewAccount(adminId, "admin-inventory", "admin-inventory@example.test", nameof(UserRole.Administrator), hash),
        NewAccount(clientId, "client-inventory", "client-inventory@example.test", nameof(UserRole.Client), hash));
    await db.SaveChangesAsync();
}

var administrator = new AuthenticatedUser(adminId, "admin-inventory", UserRole.Administrator, 0m);
var client = new AuthenticatedUser(clientId, "client-inventory", UserRole.Client, 0m);
var service = new ProductInventoryService(connectionString);
string suffix = Guid.NewGuid().ToString("N")[..8];

InventoryResult<ServiceItemSummary> createdProduct = await service.SaveAsync(
    administrator,
    new SaveServiceItemRequest(
        null,
        $"Nuoc smoke {suffix}",
        ServiceItemTypes.Product,
        "Chai",
        12_000m,
        5,
        100));
Assert(createdProduct.IsSuccess && createdProduct.Value is not null,
    "Quản trị viên phải tạo được sản phẩm.");
Assert(createdProduct.Value!.InventoryState == InventoryStates.Low,
    "Tồn kho dưới 10% mức chuẩn phải được cảnh báo ít.");

int productId = createdProduct.Value.Id;
InventoryResult<ServiceItemSummary> adjusted = await service.AdjustStockAsync(
    administrator,
    new AdjustStockRequest(productId, 10));
Assert(adjusted.IsSuccess && adjusted.Value?.StockQuantity == 15,
    "Nhập kho phải tăng đúng số lượng.");
Assert(adjusted.Value?.InventoryState == InventoryStates.Normal,
    "Tồn kho từ 10% mức chuẩn trở lên phải ở trạng thái nhiều.");

InventoryResult<ServiceItemSummary> invalidExport = await service.AdjustStockAsync(
    administrator,
    new AdjustStockRequest(productId, -100));
Assert(!invalidExport.IsSuccess && invalidExport.ErrorCode == "INVENTORY_INSUFFICIENT_STOCK",
    "Không được xuất vượt tồn kho.");

InventoryResult<ServiceItemSummary> forbidden = await service.SaveAsync(
    client,
    new SaveServiceItemRequest(
        null,
        $"Cam {suffix}",
        ServiceItemTypes.Product,
        "Qua",
        5_000m,
        10,
        20));
Assert(!forbidden.IsSuccess && forbidden.ErrorCode == "INVENTORY_FORBIDDEN",
    "Client không được tạo sản phẩm.");

InventoryResult<ServiceItemSummary> createdService = await service.SaveAsync(
    administrator,
    new SaveServiceItemRequest(
        null,
        $"Ngu qua dem smoke {suffix}",
        ServiceItemTypes.Overnight,
        "Lan",
        50_000m,
        null,
        null));
Assert(createdService.IsSuccess && createdService.Value is not null,
    "Quản trị viên phải tạo được dịch vụ ngủ qua đêm.");

IReadOnlyList<ServiceItemSummary> listed = await service.ListAsync(client);
Assert(listed.Any(item => item.Id == productId),
    "Client phải xem được danh sách sản phẩm từ Server.");

InventoryResult<bool> deletedService = await service.DeleteAsync(
    administrator,
    createdService.Value!.Id);
Assert(deletedService.IsSuccess, "Phải xóa được dịch vụ chưa phát sinh sử dụng.");

Console.WriteLine("Inventory smoke test passed.");

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
