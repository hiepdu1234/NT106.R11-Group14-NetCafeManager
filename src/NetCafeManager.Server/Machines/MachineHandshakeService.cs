using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Server.Machines;

public sealed class MachineHandshakeService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(8);

    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly IMachineKeyProvider _keyProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, PendingChallenge> _challenges = [];

    public MachineHandshakeService(
        string connectionString,
        IMachineKeyProvider keyProvider,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    public async Task<SessionResult<MachineHandshakeChallenge>> BeginAsync(
        string? machineName,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = machineName?.Trim() ?? string.Empty;
        if (normalizedName.Length is 0 or > 50 ||
            !_keyProvider.TryGetKey(normalizedName, out _))
        {
            return RejectedChallenge();
        }

        await using var db = new NetCafeDbContext(_options);
        Computer? computer = await db.Computers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Name == normalizedName,
                cancellationToken);
        if (computer is null || IsUnavailable(computer.Status))
        {
            return RejectedChallenge();
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var challenge = new MachineHandshakeChallenge(
            Guid.NewGuid(),
            computer.Name,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            now.Add(ChallengeLifetime));
        _challenges[challenge.ChallengeId] = new PendingChallenge(
            computer.Id,
            challenge);
        RemoveExpired(now);
        return SessionResult<MachineHandshakeChallenge>.Success(
            challenge,
            "Đã tạo thử thách xác thực máy trạm.");
    }

    public async Task<SessionResult<VerifiedMachine>> CompleteAsync(
        Guid challengeId,
        string? proofBase64,
        string? remoteIpAddress,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!_challenges.TryRemove(challengeId, out PendingChallenge? pending) ||
            pending.Challenge.ExpiresAt <= now ||
            !_keyProvider.TryGetKey(pending.Challenge.MachineName, out byte[] key) ||
            !MachineHandshakeProof.VerifyBase64(
                key,
                pending.Challenge,
                proofBase64))
        {
            return RejectedMachine();
        }

        string? normalizedIp = NormalizeIp(remoteIpAddress);
        await using var db = new NetCafeDbContext(_options);
        Computer? computer = await db.Computers.SingleOrDefaultAsync(
            row => row.Id == pending.ComputerId,
            cancellationToken);
        if (computer is null || IsUnavailable(computer.Status))
        {
            return RejectedMachine();
        }

        computer.LastHeartbeatAtUtc = now.UtcDateTime;
        if (normalizedIp is not null)
        {
            computer.IpAddress = normalizedIp;
        }

        if (computer.Status == ComputerStatuses.Disconnected)
        {
            computer.Status = ComputerStatuses.Available;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return RejectedMachine();
        }

        var verified = new VerifiedMachine(
            computer.Id,
            computer.Name,
            now,
            now.Add(VerificationLifetime));
        return SessionResult<VerifiedMachine>.Success(
            verified,
            "Máy trạm đã được xác thực.");
    }

    private static bool IsUnavailable(string status) =>
        status is ComputerStatuses.Locked or
            ComputerStatuses.Maintenance or
            ComputerStatuses.Incident;

    private static string? NormalizeIp(string? remoteIpAddress)
    {
        string normalized = remoteIpAddress?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= 45 ? normalized : null;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach ((Guid id, PendingChallenge challenge) in _challenges)
        {
            if (challenge.Challenge.ExpiresAt <= now)
            {
                _challenges.TryRemove(id, out _);
            }
        }
    }

    private static SessionResult<MachineHandshakeChallenge> RejectedChallenge() =>
        SessionResult<MachineHandshakeChallenge>.Failure(
            "MACHINE_HANDSHAKE_REJECTED",
            "Không thể xác thực máy trạm.");

    private static SessionResult<VerifiedMachine> RejectedMachine() =>
        SessionResult<VerifiedMachine>.Failure(
            "MACHINE_HANDSHAKE_REJECTED",
            "Không thể xác thực máy trạm.");

    private sealed record PendingChallenge(
        int ComputerId,
        MachineHandshakeChallenge Challenge);
}
