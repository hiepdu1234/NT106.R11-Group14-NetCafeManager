namespace NetCafeManager.Shared.Sessions;

public sealed record MachineHandshakeBeginRequest(string MachineName);

public sealed record MachineHandshakeCompleteRequest(
    Guid ChallengeId,
    string ProofBase64);

public sealed record EndUsageSessionRequest(
    long SessionId,
    string? Reason);
