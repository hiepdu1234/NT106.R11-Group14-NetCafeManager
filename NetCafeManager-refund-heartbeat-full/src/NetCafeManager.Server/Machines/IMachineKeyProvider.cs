namespace NetCafeManager.Server.Machines;

public interface IMachineKeyProvider
{
    bool TryGetKey(string machineName, out byte[] machineKey);
}
