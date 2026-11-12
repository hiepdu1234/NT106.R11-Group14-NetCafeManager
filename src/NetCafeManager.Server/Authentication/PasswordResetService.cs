using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Security;

namespace NetCafeManager.Server.Authentication;

public sealed class PasswordResetService
{
    private const int OtpDigits = 6;
    private const int MaximumFailedAttempts = 5;
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 128;
    private const int MaximumIdentifierLength = 255;

    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(1);

    private const string InvalidOtpCode = "AUTH_INVALID_OTP";
    private const string InvalidOtpMessage = "Mã xác nhận không hợp lệ hoặc đã hết hạn.";
    private const string InvalidPasswordCode = "AUTH_INVALID_NEW_PASSWORD";
    private const string InvalidPasswordMessage = "Mật khẩu mới phải có từ 8 đến 128 ký tự.";

    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly IPasswordResetCodeSender _codeSender;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SessionManager _sessions;
    private readonly byte[] _otpHashSecret;
    private readonly TimeProvider _timeProvider;

    public PasswordResetService(
        string connectionString,
        IPasswordResetCodeSender codeSender,
        IPasswordHasher passwordHasher,
        SessionManager sessions,
        byte[] otpHashSecret,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _codeSender = codeSender ?? throw new ArgumentNullException(nameof(codeSender));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        ArgumentNullException.ThrowIfNull(otpHashSecret);

        if (otpHashSecret.Length < 32)
        {
            throw new ArgumentException(
                "OTP hash secret must contain at least 32 random bytes.",
                nameof(otpHashSecret));
        }

        _otpHashSecret = (byte[])otpHashSecret.Clone();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    public async Task<PasswordResetResponse> RequestAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string identifier = NormalizeIdentifier(request.Identifier);
        if (!IsValidIdentifier(identifier) || !IsValidRole(request.Role))
        {
            return PasswordResetResponse.RequestAccepted();
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        Account? account;
        PasswordResetOtp? createdOtp = null;
        string? plainOtp = null;

        await using (var db = new NetCafeDbContext(_options))
        await using (var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken))
        {
            account = await FindAccountAsync(db, identifier, request.Role, cancellationToken);
            if (account is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return PasswordResetResponse.RequestAccepted();
            }

            PasswordResetOtp? latest = await db.PasswordResetOtps
                .Where(otp => otp.AccountId == account.Id && !otp.IsUsed)
                .OrderByDescending(otp => otp.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is not null &&
                latest.ExpiresAtUtc > now.UtcDateTime &&
                latest.CreatedAtUtc > now.Subtract(RequestCooldown).UtcDateTime)
            {
                await transaction.CommitAsync(cancellationToken);
                return PasswordResetResponse.RequestAccepted();
            }

            List<PasswordResetOtp> activeOtps = await db.PasswordResetOtps
                .Where(otp => otp.AccountId == account.Id && !otp.IsUsed)
                .ToListAsync(cancellationToken);
            foreach (PasswordResetOtp activeOtp in activeOtps)
            {
                activeOtp.IsUsed = true;
            }

            plainOtp = RandomNumberGenerator
                .GetInt32(0, 1_000_000)
                .ToString($"D{OtpDigits}", CultureInfo.InvariantCulture);
            createdOtp = new PasswordResetOtp
            {
                AccountId = account.Id,
                OtpHash = HashOtp(account.Id, plainOtp),
                ExpiresAtUtc = now.Add(OtpLifetime).UtcDateTime,
                IsUsed = false,
                FailedAttemptCount = 0,
                CreatedAtUtc = now.UtcDateTime
            };

            db.PasswordResetOtps.Add(createdOtp);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        try
        {
            await _codeSender.SendAsync(
                account.Email,
                plainOtp,
                new DateTimeOffset(createdOtp.ExpiresAtUtc, TimeSpan.Zero),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Không để một mã chưa gửi được tiếp tục có hiệu lực. Phản hồi vẫn
            // giống trường hợp không có tài khoản để tránh lộ thông tin.
            await InvalidateOtpAfterDeliveryFailureAsync(createdOtp.Id, cancellationToken);
        }

        return PasswordResetResponse.RequestAccepted();
    }

    public async Task<PasswordResetResponse> VerifyOtpAsync(
        VerifyPasswordResetOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string identifier = NormalizeIdentifier(request.Identifier);
        if (!IsValidIdentifier(identifier) ||
            !IsValidRole(request.Role) ||
            !IsValidOtpFormat(request.Otp))
        {
            return InvalidOtp();
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        Account? account = await FindAccountAsync(db, identifier, request.Role, cancellationToken);
        if (account is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return InvalidOtp();
        }

        OtpValidation validation = await ValidateOtpAsync(
            db,
            account.Id,
            request.Otp,
            now,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return validation.IsValid
            ? PasswordResetResponse.OtpVerified()
            : InvalidOtp();
    }

    public async Task<PasswordResetResponse> CompleteAsync(
        CompletePasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsValidNewPassword(request.NewPassword))
        {
            return PasswordResetResponse.Failure(InvalidPasswordCode, InvalidPasswordMessage);
        }

        string identifier = NormalizeIdentifier(request.Identifier);
        if (!IsValidIdentifier(identifier) ||
            !IsValidRole(request.Role) ||
            !IsValidOtpFormat(request.Otp))
        {
            return InvalidOtp();
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        Guid accountId;

        await using (var db = new NetCafeDbContext(_options))
        await using (var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken))
        {
            Account? account = await FindAccountAsync(db, identifier, request.Role, cancellationToken);
            if (account is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return InvalidOtp();
            }

            OtpValidation validation = await ValidateOtpAsync(
                db,
                account.Id,
                request.Otp,
                now,
                cancellationToken);
            if (!validation.IsValid || validation.Otp is null)
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return InvalidOtp();
            }

            PasswordHash passwordHash = _passwordHasher.HashPassword(request.NewPassword);
            account.PasswordAlgorithm = passwordHash.Algorithm;
            account.PasswordIterations = passwordHash.Iterations;
            account.PasswordSaltBase64 = passwordHash.SaltBase64;
            account.PasswordHashBase64 = passwordHash.HashBase64;
            validation.Otp.IsUsed = true;

            List<PasswordResetOtp> otherActiveOtps = await db.PasswordResetOtps
                .Where(otp => otp.AccountId == account.Id && !otp.IsUsed && otp.Id != validation.Otp.Id)
                .ToListAsync(cancellationToken);
            foreach (PasswordResetOtp otherOtp in otherActiveOtps)
            {
                otherOtp.IsUsed = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            accountId = account.Id;
        }

        _sessions.RevokeAllForUser(accountId);
        return PasswordResetResponse.Completed();
    }

    private async Task<OtpValidation> ValidateOtpAsync(
        NetCafeDbContext db,
        Guid accountId,
        string plainOtp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        PasswordResetOtp? otp = await db.PasswordResetOtps
            .Where(candidate => candidate.AccountId == accountId && !candidate.IsUsed)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (otp is null || otp.FailedAttemptCount >= MaximumFailedAttempts)
        {
            return OtpValidation.Invalid;
        }

        if (otp.ExpiresAtUtc <= now.UtcDateTime)
        {
            otp.IsUsed = true;
            return OtpValidation.Invalid;
        }

        byte[] suppliedHash = HashOtp(accountId, plainOtp);
        bool matches = CryptographicOperations.FixedTimeEquals(suppliedHash, otp.OtpHash);
        CryptographicOperations.ZeroMemory(suppliedHash);

        if (matches)
        {
            return new OtpValidation(true, otp);
        }

        otp.FailedAttemptCount++;
        if (otp.FailedAttemptCount >= MaximumFailedAttempts)
        {
            otp.IsUsed = true;
        }

        return OtpValidation.Invalid;
    }

    private static Task<Account?> FindAccountAsync(
        NetCafeDbContext db,
        string identifier,
        UserRole role,
        CancellationToken cancellationToken) =>
        db.Accounts
            .Where(account =>
                account.IsActive &&
                account.Role == role.ToString() &&
                (account.Username == identifier || account.Email == identifier))
            .OrderBy(account => account.Username == identifier ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);

    private byte[] HashOtp(Guid accountId, string otp)
    {
        byte[] otpBytes = Encoding.UTF8.GetBytes(otp);
        byte[] input = new byte[16 + otpBytes.Length];
        accountId.TryWriteBytes(input);
        otpBytes.CopyTo(input, 16);

        using var hmac = new HMACSHA256(_otpHashSecret);
        byte[] hash = hmac.ComputeHash(input);
        CryptographicOperations.ZeroMemory(otpBytes);
        CryptographicOperations.ZeroMemory(input);
        return hash;
    }

    private async Task InvalidateOtpAfterDeliveryFailureAsync(
        long otpId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = new NetCafeDbContext(_options);
            PasswordResetOtp? otp = await db.PasswordResetOtps
                .SingleOrDefaultAsync(candidate => candidate.Id == otpId, cancellationToken);
            if (otp is not null)
            {
                otp.IsUsed = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Không làm lộ trạng thái tài khoản qua phản hồi khôi phục mật khẩu.
        }
    }

    private static string NormalizeIdentifier(string? identifier) => identifier?.Trim() ?? string.Empty;

    private static bool IsValidIdentifier(string identifier) =>
        identifier.Length is > 0 and <= MaximumIdentifierLength;

    private static bool IsValidRole(UserRole role) =>
        role is UserRole.Administrator or UserRole.Client;

    private static bool IsValidOtpFormat(string? otp) =>
        otp is { Length: OtpDigits } && otp.All(char.IsAsciiDigit);

    private static bool IsValidNewPassword(string? password) =>
        password is not null &&
        password.Length is >= MinimumPasswordLength and <= MaximumPasswordLength &&
        !string.IsNullOrWhiteSpace(password);

    private static PasswordResetResponse InvalidOtp() =>
        PasswordResetResponse.Failure(InvalidOtpCode, InvalidOtpMessage);

    private sealed record OtpValidation(bool IsValid, PasswordResetOtp? Otp)
    {
        public static OtpValidation Invalid { get; } = new(false, null);
    }
}
