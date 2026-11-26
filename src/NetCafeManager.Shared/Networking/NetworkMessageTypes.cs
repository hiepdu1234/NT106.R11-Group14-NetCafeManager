namespace NetCafeManager.Shared.Networking;

public static class NetworkMessageTypes
{
    public const string MachineHandshakeBegin = "MACHINE_HANDSHAKE_BEGIN";
    public const string MachineHandshakeComplete = "MACHINE_HANDSHAKE_COMPLETE";
    public const string MachineHeartbeat = "MACHINE_HEARTBEAT";
    public const string MachineRemoteCommandResult = "MACHINE_REMOTE_COMMAND_RESULT";

    public const string AuthLogin = "AUTH_LOGIN";
    public const string AuthLogout = "AUTH_LOGOUT";
    public const string AuthForgotPassword = "AUTH_FORGOT_PASSWORD";
    public const string AuthVerifyOtp = "AUTH_VERIFY_OTP";
    public const string AuthResetPassword = "AUTH_RESET_PASSWORD";

    public const string SessionStart = "SESSION_START";
    public const string SessionGetActive = "SESSION_GET_ACTIVE";
    public const string SessionEnd = "SESSION_END";

    public const string ProductPurchase = "PRODUCT_PURCHASE";
    public const string ProductList = "PRODUCT_LIST";

    public const string AdminCustomerList = "ADMIN_CUSTOMER_LIST";
    public const string AdminCustomerCreate = "ADMIN_CUSTOMER_CREATE";
    public const string AdminCustomerUpdate = "ADMIN_CUSTOMER_UPDATE";
    public const string AdminHourPackageList = "ADMIN_HOUR_PACKAGE_LIST";
    public const string AdminHourPackageSave = "ADMIN_HOUR_PACKAGE_SAVE";
    public const string AdminTopUp = "ADMIN_TOP_UP";
    public const string AdminTopUpHistory = "ADMIN_TOP_UP_HISTORY";
    public const string AdminInventoryList = "ADMIN_INVENTORY_LIST";
    public const string AdminInventorySave = "ADMIN_INVENTORY_SAVE";
    public const string AdminInventoryAdjust = "ADMIN_INVENTORY_ADJUST";
    public const string AdminInventoryDelete = "ADMIN_INVENTORY_DELETE";
    public const string AdminInvoiceList = "ADMIN_INVOICE_LIST";
    public const string AdminInvoiceCreate = "ADMIN_INVOICE_CREATE";
    public const string AdminInvoiceSetStatus = "ADMIN_INVOICE_SET_STATUS";
    public const string AdminRefundCreate = "ADMIN_REFUND_CREATE";
    public const string AdminRefundHistory = "ADMIN_REFUND_HISTORY";
    public const string AdminMachineList = "ADMIN_MACHINE_LIST";
    public const string AdminMachineSetStatus = "ADMIN_MACHINE_SET_STATUS";
    public const string AdminMachineHistoryList = "ADMIN_MACHINE_HISTORY_LIST";
    public const string AdminRemoteCommandQueue = "ADMIN_REMOTE_COMMAND_QUEUE";
    public const string AdminRemoteCommandHistory = "ADMIN_REMOTE_COMMAND_HISTORY";
    public const string AdminAuditList = "ADMIN_AUDIT_LIST";

    public const string CommandResult = "COMMAND_RESULT";
    public const string Error = "ERROR";
}
