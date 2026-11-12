using System.Security.Cryptography;
using System.Text;

namespace NetCafeManager.Shared.Sessions;

public static class MachineHandshakeProof
{
    public static string ComputeBase64(
        ReadOnlySpan<byte> machineKey,
        MachineHandshakeChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (machineKey.Length < 32)
        {
            throw new ArgumentException(
                "Khóa máy trạm phải có ít nhất 32 byte.",
                nameof(machineKey));
        }

        byte[] payload = CreatePayload(challenge);
        byte[] proof = HMACSHA256.HashData(machineKey, payload);
        return Convert.ToBase64String(proof);
    }

    public static bool VerifyBase64(
        ReadOnlySpan<byte> machineKey,
        MachineHandshakeChallenge challenge,
        string? suppliedProofBase64)
    {
        if (string.IsNullOrWhiteSpace(suppliedProofBase64))
        {
            return false;
        }

        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(suppliedProofBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] expected = Convert.FromBase64String(
            ComputeBase64(machineKey, challenge));
        return supplied.Length == expected.Length &&
               CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static byte[] CreatePayload(MachineHandshakeChallenge challenge) =>
        Encoding.UTF8.GetBytes(
            $"{challenge.ChallengeId:N}.{challenge.MachineName}.{challenge.NonceBase64}");
}
