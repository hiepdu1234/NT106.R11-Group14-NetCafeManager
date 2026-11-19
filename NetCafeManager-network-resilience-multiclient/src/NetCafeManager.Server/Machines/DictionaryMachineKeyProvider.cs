namespace NetCafeManager.Server.Machines;

public sealed class DictionaryMachineKeyProvider : IMachineKeyProvider
{
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public DictionaryMachineKeyProvider(
        IReadOnlyDictionary<string, byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var validated = new Dictionary<string, byte[]>(
            StringComparer.OrdinalIgnoreCase);
        foreach ((string name, byte[] key) in keys)
        {
            string normalizedName = name?.Trim() ?? string.Empty;
            if (normalizedName.Length == 0 || key is null || key.Length < 32)
            {
                throw new ArgumentException(
                    "Mỗi máy phải có tên và khóa ngẫu nhiên tối thiểu 32 byte.",
                    nameof(keys));
            }

            validated.Add(normalizedName, key.ToArray());
        }

        _keys = validated;
    }

    public bool TryGetKey(string machineName, out byte[] machineKey)
    {
        if (_keys.TryGetValue(machineName.Trim(), out byte[]? stored))
        {
            machineKey = stored.ToArray();
            return true;
        }

        machineKey = [];
        return false;
    }
}
