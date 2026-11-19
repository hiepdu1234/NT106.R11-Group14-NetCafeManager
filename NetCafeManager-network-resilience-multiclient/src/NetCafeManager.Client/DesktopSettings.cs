using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetCafeManager.Client;

internal enum WorkstationMode
{
    Client = 1,
    Administration = 2
}

internal sealed record DesktopSettings(
    string ServerHost,
    int ServerPort,
    WorkstationMode Mode,
    string MachineName,
    string? ProtectedMachineKey)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetCafeManager");

    private static string SettingsPath => Path.Combine(
        SettingsDirectory,
        "desktop-settings.json");

    public static DesktopSettings Default => new(
        "127.0.0.1",
        5050,
        WorkstationMode.Client,
        Environment.MachineName,
        null);

    public static DesktopSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return Default;
            }

            DesktopSettings? settings = JsonSerializer.Deserialize<DesktopSettings>(
                File.ReadAllText(SettingsPath),
                JsonOptions);
            return settings is null || !settings.IsValid()
                ? Default
                : settings;
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or
                                          JsonException)
        {
            return Default;
        }
    }

    public static DesktopSettings Create(
        string serverHost,
        int serverPort,
        WorkstationMode mode,
        string machineName,
        string machineKeyBase64)
    {
        string protectedKey = Protect(machineKeyBase64.Trim());
        return new DesktopSettings(
            serverHost.Trim(),
            serverPort,
            mode,
            machineName.Trim(),
            protectedKey);
    }

    public void Save()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException("Cấu hình thiết bị không hợp lệ.");
        }

        Directory.CreateDirectory(SettingsDirectory);
        string temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(this, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    public string? TryUnprotectMachineKey()
    {
        if (string.IsNullOrWhiteSpace(ProtectedMachineKey))
        {
            return null;
        }

        try
        {
            byte[] protectedBytes = Convert.FromBase64String(ProtectedMachineKey);
            byte[] clearBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(clearBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
        catch (Exception exception) when (exception is FormatException or
                                          CryptographicException)
        {
            return null;
        }
    }

    private bool IsValid() =>
        !string.IsNullOrWhiteSpace(ServerHost) &&
        ServerHost.Length <= 253 &&
        ServerPort is >= 1 and <= 65535 &&
        Enum.IsDefined(Mode) &&
        !string.IsNullOrWhiteSpace(MachineName) &&
        MachineName.Length <= 50;

    private static string Protect(string value)
    {
        byte[] clearBytes = Encoding.UTF8.GetBytes(value);
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(
                clearBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }
}
