using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Machines;
using NetCafeManager.Server.Sessions;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Sessions;

string connectionString = Environment.GetEnvironmentVariable(
    "NETCAFE_SESSION_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException(
        "Thiếu NETCAFE_SESSION_SMOKE_CONNECTION.");
var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
var hasher = new Pbkdf2PasswordHasher();
var timeProvider = new ManualTimeProvider(
    new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
string suffix = Guid.NewGuid().ToString("N")[..8];
string machine1Name = $"SMOKE-A-{suffix}";
string machine2Name = $"SMOKE-B-{suffix}";
byte[] machine1Key = RandomNumberGenerator.GetBytes(32);
byte[] machine2Key = RandomNumberGenerator.GetBytes(32);
Guid client1Id = Guid.NewGuid();
Guid client2Id = Guid.NewGuid();
Guid emptyClientId = Guid.NewGuid();
int machine1Id;
int machine2Id;

await using (var db = new NetCafeDbContext(options))
{
    var hash = hasher.HashPassword("MatKhau!123");
    db.Accounts.AddRange(
        NewAccount(client1Id, $"session-a-{suffix}", $"session-a-{suffix}@example.test", 30_000m, hash),
        NewAccount(client2Id, $"session-b-{suffix}", $"session-b-{suffix}@example.test", 20_000m, hash),
        NewAccount(emptyClientId, $"session-zero-{suffix}", $"session-zero-{suffix}@example.test", 0m, hash));
    var machineEntity1 = new Computer
    {
        Name = machine1Name,
        Status = ComputerStatuses.Available
    };
    var machineEntity2 = new Computer
    {
        Name = machine2Name,
        Status = ComputerStatuses.Available
    };
    db.Computers.AddRange(machineEntity1, machineEntity2);
    await db.SaveChangesAsync();
    machine1Id = machineEntity1.Id;
    machine2Id = machineEntity2.Id;
}

var keyProvider = new DictionaryMachineKeyProvider(
    new Dictionary<string, byte[]>
    {
        [machine1Name] = machine1Key,
        [machine2Name] = machine2Key
    });
var handshakes = new MachineHandshakeService(
    connectionString,
    keyProvider,
    timeProvider);

SessionResult<MachineHandshakeChallenge> firstChallenge =
    await handshakes.BeginAsync(machine1Name);
Assert(firstChallenge.IsSuccess && firstChallenge.Value is not null,
    "Máy đã cấu hình phải nhận được challenge.");
SessionResult<VerifiedMachine> wrongProof = await handshakes.CompleteAsync(
    firstChallenge.Value!.ChallengeId,
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    "127.0.0.10");
Assert(!wrongProof.IsSuccess,
    "Proof sai phải bị từ chối.");

VerifiedMachine machine1 = await VerifyMachineAsync(
    handshakes,
    machine1Name,
    machine1Key,
    "127.0.0.10");
VerifiedMachine machine2 = await VerifyMachineAsync(
    handshakes,
    machine2Name,
    machine2Key,
    "127.0.0.11");
Assert(machine1.ComputerId == machine1Id && machine2.ComputerId == machine2Id,
    "Handshake phải ánh xạ đúng máy trong SQL Server.");

SessionResult<MachineHandshakeChallenge> replayChallenge =
    await handshakes.BeginAsync(machine1Name);
string replayProof = MachineHandshakeProof.ComputeBase64(
    machine1Key,
    replayChallenge.Value!);
SessionResult<VerifiedMachine> replayFirst = await handshakes.CompleteAsync(
    replayChallenge.Value!.ChallengeId,
    replayProof,
    "127.0.0.10");
SessionResult<VerifiedMachine> replaySecond = await handshakes.CompleteAsync(
    replayChallenge.Value.ChallengeId,
    replayProof,
    "127.0.0.10");
Assert(replayFirst.IsSuccess && !replaySecond.IsSuccess,
    "Challenge chỉ được sử dụng thành công một lần.");

var client1 = new AuthenticatedUser(
    client1Id,
    $"session-a-{suffix}",
    UserRole.Client,
    30_000m);
var client2 = new AuthenticatedUser(
    client2Id,
    $"session-b-{suffix}",
    UserRole.Client,
    20_000m);
var emptyClient = new AuthenticatedUser(
    emptyClientId,
    $"session-zero-{suffix}",
    UserRole.Client,
    0m);
var sessions = new UsageSessionService(
    connectionString,
    12_000m,
    timeProvider);

SessionResult<UsageSessionSummary> started = await sessions.StartAsync(
    client1,
    machine1);
Assert(started.IsSuccess && started.Value is not null,
    "Tài khoản đủ tiền phải bắt đầu phiên được.");

SessionResult<UsageSessionSummary> duplicateAccount = await sessions.StartAsync(
    client1,
    machine2);
Assert(!duplicateAccount.IsSuccess &&
       duplicateAccount.ErrorCode == "SESSION_ACCOUNT_ALREADY_ACTIVE",
    "Một tài khoản không được sử dụng đồng thời hai máy.");

SessionResult<UsageSessionSummary> busyComputer = await sessions.StartAsync(
    client2,
    machine1);
Assert(!busyComputer.IsSuccess &&
       busyComputer.ErrorCode == "SESSION_COMPUTER_BUSY",
    "Một máy không được có hai phiên đồng thời.");

timeProvider.Advance(TimeSpan.FromMinutes(30));
SessionResult<UsageSessionSummary> ended = await sessions.EndAsync(
    client1,
    started.Value!.SessionId,
    "DangXuat");
Assert(ended.IsSuccess && ended.Value is not null,
    "Chủ tài khoản phải kết thúc được phiên của mình.");
Assert(ended.Value!.UsageCost == 6_000m &&
       ended.Value.RemainingBalance == 24_000m,
    "Phiên 30 phút với giá 12.000 đồng/giờ phải trừ 6.000 đồng.");
Assert(await sessions.GetActiveAsync(client1) is null,
    "Phiên đã kết thúc không còn được xem là đang hoạt động.");

SessionResult<UsageSessionSummary> endedAgain = await sessions.EndAsync(
    client1,
    started.Value.SessionId,
    "DangXuat");
Assert(!endedAgain.IsSuccess &&
       endedAgain.ErrorCode == "SESSION_ALREADY_ENDED",
    "Không được tính tiền hai lần cho cùng một phiên.");

SessionResult<UsageSessionSummary> noBalance = await sessions.StartAsync(
    emptyClient,
    machine2);
Assert(!noBalance.IsSuccess &&
       noBalance.ErrorCode == "SESSION_INSUFFICIENT_BALANCE",
    "Tài khoản hết tiền không được bắt đầu phiên.");

timeProvider.Advance(TimeSpan.FromHours(8));
SessionResult<UsageSessionSummary> expiredMachine = await sessions.StartAsync(
    client2,
    machine2);
Assert(!expiredMachine.IsSuccess &&
       expiredMachine.ErrorCode == "SESSION_MACHINE_VERIFICATION_EXPIRED",
    "Kết quả handshake hết hạn phải bị từ chối.");

Console.WriteLine("Machine handshake and usage session smoke test passed.");

static async Task<VerifiedMachine> VerifyMachineAsync(
    MachineHandshakeService handshakes,
    string machineName,
    byte[] key,
    string ipAddress)
{
    SessionResult<MachineHandshakeChallenge> challenge =
        await handshakes.BeginAsync(machineName);
    Assert(challenge.IsSuccess && challenge.Value is not null,
        "Không tạo được challenge cho máy thử nghiệm.");
    string proof = MachineHandshakeProof.ComputeBase64(key, challenge.Value!);
    SessionResult<VerifiedMachine> result = await handshakes.CompleteAsync(
        challenge.Value!.ChallengeId,
        proof,
        ipAddress);
    Assert(result.IsSuccess && result.Value is not null,
        "Máy có proof đúng phải xác thực thành công.");
    return result.Value!;
}

static Account NewAccount(
    Guid id,
    string username,
    string email,
    decimal balance,
    NetCafeManager.Shared.Security.PasswordHash hash) => new()
{
    Id = id,
    Username = username,
    Email = email,
    FullName = username,
    Role = nameof(UserRole.Client),
    IsActive = true,
    Balance = balance,
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

sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
{
    private DateTimeOffset _utcNow = initialTime;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
