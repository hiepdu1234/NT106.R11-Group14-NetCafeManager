using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Administration;
using NetCafeManager.Server.Machines;
using NetCafeManager.Shared.Administration;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

string connectionString = Environment.GetEnvironmentVariable(
    "NETCAFE_ADMIN_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException(
        "Thiếu NETCAFE_ADMIN_SMOKE_CONNECTION.");
var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;

Guid adminId = Guid.NewGuid();
Guid clientId = Guid.NewGuid();
string suffix = Guid.NewGuid().ToString("N")[..8];
string machineName = $"REMOTE-{suffix}";
int computerId;
var actor = new AuthenticatedUser(
    adminId,
    $"remote-admin-{suffix}",
    UserRole.Administrator,
    0m);

await using (var db = new NetCafeDbContext(options))
{
    db.Accounts.Add(new Account
    {
        Id = adminId,
        Username = actor.Username,
        Email = $"{actor.Username}@example.test",
        FullName = "Remote command smoke",
        Role = nameof(UserRole.Administrator),
        Balance = 0m,
        IsActive = true,
        PasswordAlgorithm = "PBKDF2-SHA256",
        PasswordIterations = 100_000,
        PasswordSaltBase64 = Convert.ToBase64String(new byte[32]),
        PasswordHashBase64 = Convert.ToBase64String(new byte[32])
    });
    db.Accounts.Add(new Account
    {
        Id = clientId,
        Username = $"remote-client-{suffix}",
        Email = $"remote-client-{suffix}@example.test",
        FullName = "Remote command smoke client",
        Role = nameof(UserRole.Client),
        Balance = 100_000m,
        IsActive = true,
        PasswordAlgorithm = "PBKDF2-SHA256",
        PasswordIterations = 100_000,
        PasswordSaltBase64 = Convert.ToBase64String(new byte[32]),
        PasswordHashBase64 = Convert.ToBase64String(new byte[32])
    });
    var computer = new Computer
    {
        Name = machineName,
        Status = ComputerStatuses.Available
    };
    db.Computers.Add(computer);
    await db.SaveChangesAsync();
    computerId = computer.Id;
}

try
{
    var commands = new RemoteCommandService(connectionString);
    AdminResult<RemoteCommandSummary> queued = await commands.QueueAsync(
        actor,
        new QueueRemoteCommandRequest(
            computerId,
            RemoteCommandTypes.LockComputer));
    Require(queued.IsSuccess && queued.Value is not null,
        "Phải xếp được lệnh khóa máy.");

    var machine = new VerifiedMachine(
        computerId,
        machineName,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(10));
    PendingRemoteCommand? pending = await commands.TakePendingAsync(machine);
    Require(pending?.CommandId == queued.Value!.CommandId,
        "Heartbeat phải nhận đúng lệnh đang chờ.");

    SessionResult<RemoteCommandSummary> completed =
        await commands.CompleteAsync(
            machine,
            new RemoteCommandResultRequest(
                pending!.CommandId,
                true,
                null));
    Require(
        completed.IsSuccess &&
        completed.Value?.Status == RemoteCommandStatuses.Succeeded,
        "Server phải ghi nhận ACK thành công.");

    await using (var db = new NetCafeDbContext(options))
    {
        Computer computer = await db.Computers.SingleAsync(
            row => row.Id == computerId);
        Require(
            computer.Status == ComputerStatuses.Locked,
            "Lệnh khóa thành công phải chuyển trạng thái máy sang Khoa.");
    }

    byte[] machineKey = Enumerable.Range(1, 32)
        .Select(value => (byte)value)
        .ToArray();
    var handshake = new MachineHandshakeService(
        connectionString,
        new DictionaryMachineKeyProvider(
            new Dictionary<string, byte[]>
            {
                [machineName] = machineKey
            }));
    SessionResult<MachineHandshakeChallenge> challenge =
        await handshake.BeginAsync(machineName);
    Require(challenge.IsSuccess && challenge.Value is not null,
        "A locked machine must be allowed to start a control handshake.");
    string proof = MachineHandshakeProof.ComputeBase64(
        machineKey,
        challenge.Value!);
    SessionResult<VerifiedMachine> verified = await handshake.CompleteAsync(
        challenge.Value!.ChallengeId,
        proof,
        "127.0.0.1");
    Require(verified.IsSuccess && verified.Value is not null,
        "A locked machine must reconnect to the control channel.");

    var heartbeat = new MachineHeartbeatService(connectionString);
    SessionResult<MachineHeartbeatAck> lockedHeartbeat =
        await heartbeat.RecordAsync(verified.Value!);
    Require(
        lockedHeartbeat.IsSuccess &&
        lockedHeartbeat.Value?.ComputerStatus == ComputerStatuses.Locked,
        "A locked machine must keep sending heartbeats.");

    var machines = new MachineAdministrationService(connectionString);
    AdminResult<MachineAdminSummary> unlocked =
        await machines.SetStatusAsync(
            actor,
            new SetMachineStatusRequest(
                computerId,
                ComputerStatuses.Available,
                "Smoke unlock"));
    Require(unlocked.IsSuccess, "Quản trị viên phải mở khóa lại được máy.");
    SessionResult<MachineHeartbeatAck> unlockedHeartbeat =
        await heartbeat.RecordAsync(verified.Value!);
    Require(
        unlockedHeartbeat.IsSuccess &&
        unlockedHeartbeat.Value?.ComputerStatus == ComputerStatuses.Available,
        "The next heartbeat must deliver the unlocked state.");
    IReadOnlyList<MachineStatusHistorySummary> history =
        await machines.ListHistoryAsync(actor, computerId);
    Require(history.Count >= 2,
        "Phải có lịch sử khóa và mở khóa máy.");

    var audit = new AdministrativeAuditService(connectionString);
    bool recorded = await audit.TryRecordAsync(
        actor,
        NetworkMessageTypes.AdminRemoteCommandQueue,
        "Computer",
        computerId.ToString(),
        true,
        "Remote command smoke passed.");
    Require(recorded, "Phải ghi được nhật ký quản trị.");
    IReadOnlyList<AdministrativeAuditSummary> auditRows =
        await audit.ListRecentAsync(actor);
    Require(auditRows.Any(row => row.TargetId == computerId.ToString()),
        "Phải đọc lại được nhật ký quản trị.");
    await using (var db = new NetCafeDbContext(options))
    {
        db.UsageSessions.Add(new UsageSession
        {
            ComputerId = computerId,
            AccountId = clientId,
            StartedAtUtc = DateTime.UtcNow,
            HourlyRateApplied = 10_000m,
            UsageCost = 0m,
            Status = UsageSessionStatuses.InUse
        });
        Computer computer = await db.Computers.SingleAsync(
            row => row.Id == computerId);
        computer.Status = ComputerStatuses.InUse;
        await db.SaveChangesAsync();
    }

    AdminResult<MachineAdminSummary> activeSessionChange =
        await machines.SetStatusAsync(
            actor,
            new SetMachineStatusRequest(
                computerId,
                ComputerStatuses.Locked,
                "Must be rejected while a paid session is active"));
    Require(
        !activeSessionChange.IsSuccess &&
        activeSessionChange.ErrorCode == "MACHINE_HAS_ACTIVE_SESSION",
        "An administrator must not overwrite an active usage session state.");
}
finally
{
    await using var db = new NetCafeDbContext(options);
    db.RemoteCommands.RemoveRange(
        db.RemoteCommands.Where(row => row.ComputerId == computerId));
    db.MachineStatusHistories.RemoveRange(
        db.MachineStatusHistories.Where(row => row.ComputerId == computerId));
    db.AdministrativeAudits.RemoveRange(
        db.AdministrativeAudits.Where(row => row.AdministratorAccountId == adminId));
    db.UsageSessions.RemoveRange(
        db.UsageSessions.Where(row => row.ComputerId == computerId));
    Computer? computer = await db.Computers.FindAsync(computerId);
    if (computer is not null) db.Computers.Remove(computer);
    Account? account = await db.Accounts.FindAsync(adminId);
    if (account is not null) db.Accounts.Remove(account);
    Account? client = await db.Accounts.FindAsync(clientId);
    if (client is not null) db.Accounts.Remove(client);
    await db.SaveChangesAsync();
}

Console.WriteLine("Remote machine control smoke test passed.");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
