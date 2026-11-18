using System.Text.Json;

namespace NetCafeManager.Server.Machines;

public static class EnvironmentMachineKeyProvider
{
    public const string DefaultVariableName = "NETCAFE_MACHINE_KEYS_JSON";

    public static IMachineKeyProvider Create(
        string variableName = DefaultVariableName)
    {
        string json = Environment.GetEnvironmentVariable(variableName)
            ?? throw new InvalidOperationException(
                $"Thiếu biến môi trường {variableName}.");
        Dictionary<string, string>? encodedKeys =
            JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (encodedKeys is null || encodedKeys.Count == 0)
        {
            throw new InvalidOperationException(
                $"Biến {variableName} không chứa khóa máy hợp lệ.");
        }

        var keys = new Dictionary<string, byte[]>(
            StringComparer.OrdinalIgnoreCase);
        foreach ((string machineName, string encodedKey) in encodedKeys)
        {
            try
            {
                keys.Add(machineName, Convert.FromBase64String(encodedKey));
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    $"Khóa của máy {machineName} không phải Base64 hợp lệ.",
                    exception);
            }
        }

        return new DictionaryMachineKeyProvider(keys);
    }
}
