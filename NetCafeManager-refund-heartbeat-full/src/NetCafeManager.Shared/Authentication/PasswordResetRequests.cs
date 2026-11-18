namespace NetCafeManager.Shared.Authentication;

public sealed record RequestPasswordResetRequest(
    string Identifier,
    UserRole Role);

public sealed record VerifyPasswordResetOtpRequest(
    string Identifier,
    UserRole Role,
    string Otp);

public sealed record CompletePasswordResetRequest(
    string Identifier,
    UserRole Role,
    string Otp,
    string NewPassword);
