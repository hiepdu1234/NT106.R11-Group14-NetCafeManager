namespace NetCafeManager.Shared.Networking;

public static class NetworkMessageTypes
{
    public const string MachineHandshakeBegin = "MACHINE_HANDSHAKE_BEGIN";
    public const string MachineHandshakeComplete = "MACHINE_HANDSHAKE_COMPLETE";

    public const string AuthLogin = "AUTH_LOGIN";
    public const string AuthLogout = "AUTH_LOGOUT";
    public const string AuthForgotPassword = "AUTH_FORGOT_PASSWORD";
    public const string AuthVerifyOtp = "AUTH_VERIFY_OTP";
    public const string AuthResetPassword = "AUTH_RESET_PASSWORD";

    public const string SessionStart = "SESSION_START";
    public const string SessionGetActive = "SESSION_GET_ACTIVE";
    public const string SessionEnd = "SESSION_END";

    public const string CommandResult = "COMMAND_RESULT";
    public const string Error = "ERROR";
}
