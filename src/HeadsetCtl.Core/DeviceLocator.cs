namespace HeadsetCtl.Core;

public sealed class DeviceLocator(IBluetoothApi api)
{
    public const string DefaultDeviceName = "WH-CH720N";

    public IReadOnlyList<BtDevice> ListPaired(bool includeAll = false) =>
        api.EnumeratePaired()
            .Where(d => includeAll || d.IsAudio)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public BtDevice Resolve(string nameOrMac)
    {
        var all = api.EnumeratePaired();

        if (MacAddress.TryParse(nameOrMac, out var address))
        {
            return all.FirstOrDefault(d => d.Address == address)
                ?? throw new DeviceNotFoundException($"No paired device with address {MacAddress.Format(address)}.");
        }

        var matches = all
            .Where(d => string.Equals(d.Name, nameOrMac, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => throw new DeviceNotFoundException($"No paired device named '{nameOrMac}'."),
            1 => matches[0],
            _ => throw new AmbiguousDeviceException(
                $"Several paired devices are named '{nameOrMac}'; use a MAC address instead ({string.Join(", ", matches.Select(m => m.Mac))})."),
        };
    }

    public BtDevice ResolveSelected(string? deviceMac) =>
        Resolve(string.IsNullOrWhiteSpace(deviceMac) ? DefaultDeviceName : deviceMac);
}
