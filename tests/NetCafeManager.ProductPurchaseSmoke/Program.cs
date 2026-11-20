using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Inventory;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Inventory;

string connectionString =
    Environment.GetEnvironmentVariable("NETCAFE_PURCHASE_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException(
        "Thieu NETCAFE_PURCHASE_SMOKE_CONNECTION.");

var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;

var hasher = new Pbkdf2PasswordHasher();

Guid clientId = Guid.NewGuid();
Guid otherClientId = Guid.NewGuid();
Guid adminId = Guid.NewGuid();

string suffix = Guid.NewGuid().ToString("N")[..10];

int computerId;
int successProductId;
int balanceProductId;
int stockProductId;
int concurrentProductId;
int idempotentProductId;
int rollbackProductId;

long activeSessionId;
long endedSessionId;

// ============================================
// 1. TAO DU LIEU KIEM THU
// ============================================

await using (var db = new NetCafeDbContext(options))
{
    var hash = hasher.HashPassword("Smoke!12345");

    db.Accounts.AddRange(
        NewAccount(adminId, $"admin-{suffix}",
            UserRole.Administrator, hash, 0m),

        NewAccount(clientId, $"client-{suffix}",
            UserRole.Client, hash, 100_000m),

        NewAccount(otherClientId, $"other-{suffix}",
            UserRole.Client, hash, 100_000m)
    );

    var computer = new Computer
    {
        Name = $"PURCHASE-{suffix}",
        Status = "DangSuDung"
    };

    db.Computers.Add(computer);
    await db.SaveChangesAsync();

    computerId = computer.Id;

    var activeSession = NewSession(
        computerId, clientId, false);

    var endedSession = NewSession(
        computerId, clientId, true);

    db.UsageSessions.AddRange(
        activeSession,
        endedSession
    );

    await db.SaveChangesAsync();

    activeSessionId = activeSession.Id;
    endedSessionId = endedSession.Id;

    var successProduct = NewProduct(
        $"Success-{suffix}", 10_000m, 5);

    var balanceProduct = NewProduct(
        $"Balance-{suffix}", 200_000m, 4);

    var stockProduct = NewProduct(
        $"Stock-{suffix}", 10_000m, 1);

    var concurrentProduct = NewProduct(
        $"Concurrent-{suffix}", 10_000m, 1);

    var idempotentProduct = NewProduct(
        $"Idempotent-{suffix}", 5_000m, 5);

    var rollbackProduct = NewProduct(
        $"Rollback-{suffix}", 10_000m, 3);

    db.ServiceItems.AddRange(
        successProduct,
        balanceProduct,
        stockProduct,
        concurrentProduct,
        idempotentProduct,
        rollbackProduct
    );

    await db.SaveChangesAsync();

    successProductId = successProduct.Id;
    balanceProductId = balanceProduct.Id;
    stockProductId = stockProduct.Id;
    concurrentProductId = concurrentProduct.Id;
    idempotentProductId = idempotentProduct.Id;
    rollbackProductId = rollbackProduct.Id;
}

// ============================================
// 2. KHOI TAO SERVICE
// ============================================

var client = new AuthenticatedUser(
    clientId, $"client-{suffix}",
    UserRole.Client, 100_000m);

var otherClient = new AuthenticatedUser(
    otherClientId, $"other-{suffix}",
    UserRole.Client, 100_000m);

var admin = new AuthenticatedUser(
    adminId, $"admin-{suffix}",
    UserRole.Administrator, 0m);

var service = new ProductPurchaseService(connectionString);

// ============================================
// TEST 1: MUA THANH CONG
// ============================================

Guid successCommandId = Guid.NewGuid();
var success = await service.PurchaseAsync(
    client,
    activeSessionId,
    new PurchaseProductRequest(successProductId, 2, successCommandId)
);

Assert(success.IsSuccess, "Mua hang phai thanh cong.");

Assert(success.Value!.TotalAmount == 20_000m,
    "Tong tien khong chinh xac.");

Assert(success.Value.RemainingBalance == 80_000m,
    "So du sau mua khong chinh xac.");

Assert(success.Value.RemainingStock == 3,
    "Ton kho sau mua khong chinh xac.");

// Kiem tra ServiceUsage
await using (var db = new NetCafeDbContext(options))
{
    var detail = await db.ServiceUsages
        .SingleOrDefaultAsync(x => x.Id == success.Value.DetailId);

    Assert(detail != null,
        "Khong tao ServiceUsage.");

    Assert(detail!.UsageSessionId == activeSessionId,
        "Sai phien su dung.");

    Assert(detail.CommandId == successCommandId,
        "Khong luu CommandId cua giao dich mua hang.");

    Assert(detail.ServiceItemId == successProductId,
        "Sai san pham.");

    Assert(detail.Quantity == 2,
        "Sai so luong.");

    Assert(detail.UnitPriceApplied == 10_000m,
        "Sai don gia.");

    Assert(detail.TotalAmount == 20_000m,
        "Sai thanh tien ServiceUsage.");
}

Console.WriteLine("[PASS] Mua hang thanh cong.");

// ============================================
// TEST 2: KHONG DU TIEN
// ============================================

await AssertRejectedWithoutChanges(
    service,
    options,
    client,
    activeSessionId,
    balanceProductId,
    1,
    "PURCHASE_INSUFFICIENT_BALANCE",
    80_000m,
    4
);

Console.WriteLine("[PASS] Khong du tien.");

// ============================================
// TEST 3: KHONG DU HANG
// ============================================

await AssertRejectedWithoutChanges(
    service,
    options,
    client,
    activeSessionId,
    stockProductId,
    2,
    "PURCHASE_INSUFFICIENT_STOCK",
    80_000m,
    1
);

Console.WriteLine("[PASS] Khong du hang.");

// ============================================
// TEST 4: PHIEN DA KET THUC
// ============================================

var ended = await service.PurchaseAsync(
    client,
    endedSessionId,
    new PurchaseProductRequest(successProductId, 1, Guid.NewGuid())
);

Assert(
    !ended.IsSuccess &&
    ended.ErrorCode == "PURCHASE_SESSION_REQUIRED",
    "Khong duoc mua bang phien da ket thuc."
);

Console.WriteLine("[PASS] Tu choi phien da ket thuc.");

// ============================================
// TEST 5: PHIEN KHONG THUOC KHACH HANG
// ============================================

var wrongOwner = await service.PurchaseAsync(
    otherClient,
    activeSessionId,
    new PurchaseProductRequest(successProductId, 1, Guid.NewGuid())
);

Assert(
    !wrongOwner.IsSuccess &&
    wrongOwner.ErrorCode == "PURCHASE_SESSION_REQUIRED",
    "Khong duoc mua bang phien cua khach hang khac."
);

Console.WriteLine("[PASS] Tu choi phien khong thuoc khach hang.");

// ============================================
// TEST 6: ADMIN KHONG DUOC MUA
// ============================================

var forbidden = await service.PurchaseAsync(
    admin,
    activeSessionId,
    new PurchaseProductRequest(successProductId, 1, Guid.NewGuid())
);

Assert(
    !forbidden.IsSuccess &&
    forbidden.ErrorCode == "PURCHASE_FORBIDDEN",
    "Admin khong duoc goi luong mua hang client."
);

Console.WriteLine("[PASS] Kiem tra quyen mua hang.");

// ============================================
// TEST 7: HAI YEU CAU DONG THOI
// ============================================

// Chi con 1 san pham trong kho.
// Gui 2 yeu cau mua cung luc.

var results = await Task.WhenAll(
    service.PurchaseAsync(
        client,
        activeSessionId,
        new PurchaseProductRequest(concurrentProductId, 1, Guid.NewGuid())
    ),

    service.PurchaseAsync(
        client,
        activeSessionId,
        new PurchaseProductRequest(concurrentProductId, 1, Guid.NewGuid())
    )
);

Assert(
    results.Count(x => x.IsSuccess) == 1,
    "Chi mot yeu cau duoc phep mua thanh cong."
);

await using (var db = new NetCafeDbContext(options))
{
    var stock = await db.ServiceItems
        .Where(x => x.Id == concurrentProductId)
        .Select(x => x.StockQuantity)
        .SingleAsync();

    var balance = await db.Accounts
        .Where(x => x.Id == clientId)
        .Select(x => x.Balance)
        .SingleAsync();

    Assert(stock == 0,
        "Ton kho khong duoc am.");

    Assert(balance == 70_000m,
        "So du bi tru sai khi mua dong thoi.");
}

Console.WriteLine("[PASS] Kiem tra mua hang dong thoi.");

// ============================================
// TEST 8: GUI TRUNG COMMANDID KHONG TRU HAI LAN
// ============================================

Guid duplicateCommandId = Guid.NewGuid();
var duplicateRequest = new PurchaseProductRequest(
    idempotentProductId,
    2,
    duplicateCommandId);

InventoryResult<ProductPurchaseReceipt>[] duplicateResults = await Task.WhenAll(
    service.PurchaseAsync(client, activeSessionId, duplicateRequest),
    service.PurchaseAsync(client, activeSessionId, duplicateRequest));

Assert(
    duplicateResults.All(result => result.IsSuccess && result.Value is not null),
    "Hai lan gui cung CommandId phai tra ket qua thanh cong cu.");
Assert(
    duplicateResults.Select(result => result.Value!.DetailId).Distinct().Count() == 1,
    "Gui trung CommandId phai tra cung mot chi tiet dich vu.");

var retry = await service.PurchaseAsync(
    client,
    activeSessionId,
    duplicateRequest);
Assert(
    retry.IsSuccess &&
    retry.Value!.DetailId == duplicateResults[0].Value!.DetailId &&
    retry.Value.RemainingBalance == 60_000m &&
    retry.Value.RemainingStock == 3,
    "Retry phai tra dung snapshot cua giao dich da xu ly.");

var conflictingRetry = await service.PurchaseAsync(
    client,
    activeSessionId,
    duplicateRequest with { Quantity = 1 });
Assert(
    !conflictingRetry.IsSuccess &&
    conflictingRetry.ErrorCode == "PURCHASE_COMMAND_CONFLICT",
    "Khong duoc tai su dung CommandId voi noi dung khac.");

await using (var db = new NetCafeDbContext(options))
{
    int detailCount = await db.ServiceUsages.CountAsync(
        row => row.CommandId == duplicateCommandId);
    int stock = await db.ServiceItems
        .Where(row => row.Id == idempotentProductId)
        .Select(row => row.StockQuantity!.Value)
        .SingleAsync();
    decimal balance = await db.Accounts
        .Where(row => row.Id == clientId)
        .Select(row => row.Balance)
        .SingleAsync();

    Assert(detailCount == 1,
        "Gui trung CommandId chi duoc tao mot chi tiet dich vu.");
    Assert(stock == 3,
        "Gui trung CommandId khong duoc tru ton kho lan hai.");
    Assert(balance == 60_000m,
        "Gui trung CommandId khong duoc tru so du lan hai.");
}

Console.WriteLine("[PASS] Chong gui trung CommandId khi mua hang.");

// ============================================
// TEST 9: ROLLBACK KHI TRANSACTION LOI
// ============================================

// Tao trigger tam thoi de co tinh gay loi
// sau khi ServiceUsage duoc INSERT.

string triggerName =
    $"TR_Smoke_FailPurchase_{suffix}";

await using (var db = new NetCafeDbContext(options))
{
    string sql = $"""
        CREATE OR ALTER TRIGGER dbo.[{triggerName}]
        ON dbo.CHI_TIET_DICH_VU
        AFTER INSERT
        AS
        BEGIN
            IF EXISTS
            (
                SELECT 1
                FROM inserted
                WHERE MaDV = {rollbackProductId}
            )
            BEGIN
                THROW 51091,
                    N'Smoke test forced transaction failure.',
                    1;
            END;
        END;
        """;

    await db.Database.ExecuteSqlRawAsync(sql);
}

bool transactionFailed = false;

try
{
    await service.PurchaseAsync(
        client,
        activeSessionId,
        new PurchaseProductRequest(rollbackProductId, 1, Guid.NewGuid())
    );
}
catch
{
    transactionFailed = true;
}
finally
{
    await using var db = new NetCafeDbContext(options);

    await db.Database.ExecuteSqlRawAsync(
        $"DROP TRIGGER IF EXISTS dbo.[{triggerName}];"
    );
}

Assert(transactionFailed,
    "Phai phat sinh loi khi kiem tra rollback.");

await using (var db = new NetCafeDbContext(options))
{
    var stock = await db.ServiceItems
        .Where(x => x.Id == rollbackProductId)
        .Select(x => x.StockQuantity)
        .SingleAsync();

    var balance = await db.Accounts
        .Where(x => x.Id == clientId)
        .Select(x => x.Balance)
        .SingleAsync();

    var detailCount = await db.ServiceUsages.CountAsync(
        x => x.UsageSessionId == activeSessionId
          && x.ServiceItemId == rollbackProductId
    );

    Assert(stock == 3,
        "Rollback khong khoi phuc ton kho.");

    Assert(balance == 60_000m,
        "Rollback khong khoi phuc so du.");

    Assert(detailCount == 0,
        "Rollback van con ServiceUsage.");
}

Console.WriteLine("[PASS] Kiem tra rollback transaction.");

Console.WriteLine("----------------------------------");
Console.WriteLine("ALL PRODUCT PURCHASE TESTS PASSED");
Console.WriteLine("----------------------------------");

// ============================================
// CAC HAM HO TRO
// ============================================

static Account NewAccount(
    Guid id,
    string username,
    UserRole role,
    NetCafeManager.Shared.Security.PasswordHash hash,
    decimal balance)
{
    return new Account
    {
        Id = id,
        Username = username,
        Email = $"{username}@example.test",
        FullName = username,
        Role = role.ToString(),
        IsActive = true,
        Balance = balance,
        PasswordAlgorithm = hash.Algorithm,
        PasswordIterations = hash.Iterations,
        PasswordSaltBase64 = hash.SaltBase64,
        PasswordHashBase64 = hash.HashBase64
    };
}

static UsageSession NewSession(
    int computerId,
    Guid accountId,
    bool ended)
{
    return new UsageSession
    {
        ComputerId = computerId,
        AccountId = accountId,
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        EndedAtUtc = ended ? DateTime.UtcNow : null,
        HourlyRateApplied = 5000m,
        UsageCost = 0m,
        Status = ended ? "KetThuc" : "DangSuDung",
        EndReason = ended ? "SmokeTest" : null
    };
}

static ServiceItem NewProduct(
    string name,
    decimal price,
    int stock)
{
    return new ServiceItem
    {
        Name = name,
        Type = ServiceItemTypes.Product,
        Unit = "Cai",
        UnitPrice = price,
        StockQuantity = stock,
        ReferenceStockQuantity = Math.Max(stock, 10)
    };
}

static async Task AssertRejectedWithoutChanges(
    ProductPurchaseService service,
    DbContextOptions<NetCafeDbContext> options,
    AuthenticatedUser actor,
    long sessionId,
    int productId,
    int quantity,
    string expectedError,
    decimal expectedBalance,
    int expectedStock)
{
    var result = await service.PurchaseAsync(
        actor,
        sessionId,
        new PurchaseProductRequest(productId, quantity, Guid.NewGuid())
    );

    Assert(!result.IsSuccess, "Giao dich phai bi tu choi.");

    Assert(result.ErrorCode == expectedError,
        "Ma loi khong chinh xac.");

    await using var db = new NetCafeDbContext(options);

    var balance = await db.Accounts
        .Where(x => x.Id == actor.Id)
        .Select(x => x.Balance)
        .SingleAsync();

    var stock = await db.ServiceItems
        .Where(x => x.Id == productId)
        .Select(x => x.StockQuantity)
        .SingleAsync();

    Assert(balance == expectedBalance,
        "So du bi thay doi khi giao dich that bai.");

    Assert(stock == expectedStock,
        "Ton kho bi thay doi khi giao dich that bai.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
