using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public static class Fake
{
    public static BtDevice Audio(string name, ulong address, bool connected = false) =>
        new(name, address, connected, 0x240418);   // major class 0x04 = Audio/Video (headphones)

    public static BtDevice Phone(string name, ulong address, bool connected = false) =>
        new(name, address, connected, 0x5A020C);   // major class 0x02 = Phone
}

public sealed class FakeBluetoothApi : IBluetoothApi
{
    public List<BtDevice> Devices { get; } = new();
    public Dictionary<Guid, uint> ServiceResults { get; } = new();
    public List<(ulong Address, Guid Service, bool Enable)> Calls { get; } = new();
    public bool ApplyStateChange { get; set; } = true;

    public BtDevice? GetDevice(ulong address) => Devices.FirstOrDefault(d => d.Address == address);

    public uint SetServiceState(ulong address, Guid service, bool enable)
    {
        Calls.Add((address, service, enable));
        var code = ServiceResults.TryGetValue(service, out var c) ? c : 0u;
        if (code == 0 && ApplyStateChange)
        {
            var i = Devices.FindIndex(d => d.Address == address);
            if (i >= 0) Devices[i] = Devices[i] with { Connected = enable };
        }
        return code;
    }

    public IReadOnlyList<BtDevice> EnumeratePaired() => Devices.ToList();
}
