using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Infrastructure.Security;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Server.Machines;
using NetCafeManager.Server.Networking;
using NetCafeManager.Server.Sessions;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

string connectionString = Environment.GetEnvironmentVariable(
    "NETCAFE_NETWORK_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException(
        "Thiếu NETCAFE_NETWORK_SMOKE_CONNECTION.");
var options = new DbContextOptionsBuilder<NetCafeDbContext>()
    .UseSqlServer(connectionString)
    .Options;
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var clock = new ManualTimeProvider(
    new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero));
var sender = new CapturingCodeSender();
byte[] otpSecret = RandomNumberGenerator.GetBytes(32);
byte[] machineKey = RandomNumberGenerator.GetBytes(32);
string suffix = Guid.NewGuid().ToString("N")[..8];
string machineName = $"NET-SMOKE-{suffix}";
string username = $"network-{suffix}";
string password = "MatKhau!123";
string newPassword = "MatKhau!456";
var hasher = new Pbkdf2PasswordHasher();
var hash = hasher.HashPassword(password);
var account = new Account
{
    Id = Guid.NewGuid(),
    Username = username,
    Email = $"{username}@example.test",
    FullName = username,
    Role = nameof(UserRole.Client),
    IsActive = true,
    Balance = 30_000m,
    PasswordAlgorithm = hash.Algorithm,
    PasswordIterations = hash.Iterations,
    PasswordSaltBase64 = hash.SaltBase64,
    PasswordHashBase64 = hash.HashBase64
};
var computer = new Computer
{
    Name = machineName,
    Status = ComputerStatuses.Available
};

await using (var db = new NetCafeDbContext(options))
{
    db.Accounts.Add(account);
    db.Computers.Add(computer);
    await db.SaveChangesAsync();
}

try
{
    AuthenticationServices services =
        AuthenticationSetup.CreateServicesForSqlServer(
            connectionString,
            sender,
            otpSecret,
            clock);
    var machineService = new MachineHandshakeService(
        connectionString,
        new DictionaryMachineKeyProvider(
            new Dictionary<string, byte[]> { [machineName] = machineKey }),
        clock);
    var machineHandler = new MachineTcpHandler(machineService);
    var authHandler = new AuthTcpHandler(services.Authentication);
    var sessionHandler = new SessionTcpHandler(
        new UsageSessionService(connectionString, 12_000m, clock),
        services.Authentication);
    var otpHandler = new PasswordResetTcpHandler(services.PasswordReset);
    var context = new TcpConnectionContext();

    NetworkMessage rejectedLogin = (await authHandler.HandleAsync(
        Message(
            NetworkMessageTypes.AuthLogin,
            new LoginRequest(username, password)),
        context))!;
    Require(
        rejectedLogin.Type == "ERROR" &&
        rejectedLogin.Data.TryGetProperty("code", out JsonElement errorCode) &&
        errorCode.GetString() == "AUTH_MACHINE_REQUIRED",
        "Server phải từ chối đăng nhập trước khi xác thực thiết bị.");

    NetworkMessage beginResponse = (await machineHandler.HandleAsync(
        Message(
            NetworkMessageTypes.MachineHandshakeBegin,
            new MachineHandshakeBeginRequest(machineName)),
        context,
        "127.0.0.1"))!;
    SessionResult<MachineHandshakeChallenge> challenge =
        Deserialize<SessionResult<MachineHandshakeChallenge>>(beginResponse);
    Require(challenge.IsSuccess && challenge.Value is not null,
        "TCP phải cấp challenge cho máy hợp lệ.");

    string proof = MachineHandshakeProof.ComputeBase64(
        machineKey,
        challenge.Value!);
    NetworkMessage completeResponse = (await machineHandler.HandleAsync(
        Message(
            NetworkMessageTypes.MachineHandshakeComplete,
            new MachineHandshakeCompleteRequest(
                challenge.Value!.ChallengeId,
                proof)),
        context,
        "127.0.0.1"))!;
    SessionResult<VerifiedMachine> verified =
        Deserialize<SessionResult<VerifiedMachine>>(completeResponse);
    Require(verified.IsSuccess && context.VerifiedMachine is not null,
        "TCP phải lưu máy đã xác thực trong ngữ cảnh kết nối.");

    NetworkMessage loginResponse = (await authHandler.HandleAsync(
        Message(
            NetworkMessageTypes.AuthLogin,
            new LoginRequest(username, password)),
        context))!;
    LoginResponse login = Deserialize<LoginResponse>(loginResponse);
    Require(login.IsSuccess && context.User?.Id == account.Id,
        "Đăng nhập TCP phải lưu người dùng vào ngữ cảnh kết nối.");

    NetworkMessage startResponse = (await sessionHandler.HandleAsync(
        Message(NetworkMessageTypes.SessionStart, new { }),
        context))!;
    SessionResult<UsageSessionSummary> started =
        Deserialize<SessionResult<UsageSessionSummary>>(startResponse);
    Require(started.IsSuccess && context.ActiveUsageSession is not null,
        "Đăng nhập hợp lệ trên máy đã handshake phải mở được phiên.");

    clock.Advance(TimeSpan.FromMinutes(30));
    NetworkMessage endResponse = (await sessionHandler.HandleAsync(
        Message(
            NetworkMessageTypes.SessionEnd,
            new EndUsageSessionRequest(
                started.Value!.SessionId,
                "DangXuat")),
        context))!;
    SessionResult<UsageSessionSummary> ended =
        Deserialize<SessionResult<UsageSessionSummary>>(endResponse);
    Require(
        ended.IsSuccess &&
        ended.Value?.UsageCost == 6_000m &&
        context.ActiveUsageSession is null,
        "Kết thúc phiên qua TCP phải tính tiền đúng và xóa trạng thái phiên.");

    NetworkMessage otpRequestResponse = (await otpHandler.HandleAsync(
        Message(
            NetworkMessageTypes.AuthForgotPassword,
            new RequestPasswordResetRequest(username, UserRole.Client)),
        context))!;
    Require(Deserialize<PasswordResetResponse>(otpRequestResponse).IsSuccess,
        "TCP phải nhận yêu cầu gửi OTP.");
    string otp = sender.GetLatest(account.Email);

    NetworkMessage verifyResponse = (await otpHandler.HandleAsync(
        Message(
            NetworkMessageTypes.AuthVerifyOtp,
            new VerifyPasswordResetOtpRequest(
                username,
                UserRole.Client,
                otp)),
        context))!;
    Require(Deserialize<PasswordResetResponse>(verifyResponse).IsSuccess,
        "TCP phải xác minh được OTP đúng.");

    NetworkMessage resetResponse = (await otpHandler.HandleAsync(
        Message(
            NetworkMessageTypes.AuthResetPassword,
            new CompletePasswordResetRequest(
                username,
                UserRole.Client,
                otp,
                newPassword)),
        context))!;
    Require(Deserialize<PasswordResetResponse>(resetResponse).IsSuccess,
        "TCP phải đặt lại được mật khẩu.");

    LoginResponse loginWithNewPassword = await services.Authentication.LoginAsync(
        new LoginRequest(username, newPassword));
    Require(loginWithNewPassword.IsSuccess,
        "Mật khẩu mới phải đăng nhập được sau luồng OTP qua TCP.");
}
finally
{
    CryptographicOperations.ZeroMemory(otpSecret);
    CryptographicOperations.ZeroMemory(machineKey);
}

Console.WriteLine("TCP session and OTP integration smoke test passed.");

NetworkMessage Message(string type, object data) => new(
    Guid.NewGuid().ToString("N"),
    type,
    machineName,
    null,
    JsonSerializer.SerializeToElement(data, jsonOptions));

T Deserialize<T>(NetworkMessage response) =>
    response.Data.Deserialize<T>(jsonOptions)
    ?? throw new InvalidOperationException(
        $"Không đọc được phản hồi {typeof(T).Name}.");

static void Require(bool condition, string message)
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

sealed class CapturingCodeSender : IPasswordResetCodeSender
{
    private readonly ConcurrentDictionary<string, string> _codes =
        new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(
        string email,
        string otp,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _codes[email] = otp;
        return Task.CompletedTask;
    }

    public string GetLatest(string email) =>
        _codes.TryGetValue(email, out string? otp)
            ? otp
            : throw new InvalidOperationException(
                "Không thu được OTP trong smoke test.");
}
