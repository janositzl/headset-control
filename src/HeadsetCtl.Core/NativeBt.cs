using System.Runtime.InteropServices;

namespace HeadsetCtl.Core;

public sealed class NativeBt : IBluetoothApi
{
    private const uint ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public int fConnected;
        public int fRemembered;
        public int fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public int dwSize;
        public int fReturnAuthenticated;
        public int fReturnRemembered;
        public int fReturnUnknown;
        public int fReturnConnected;
        public int fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO info, ref Guid service, uint flags);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS search, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);

    [DllImport("bthprops.cpl")]
    private static extern uint BluetoothEnumerateInstalledServices(
        IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO info, ref uint count, [Out] Guid[] services);

    public IReadOnlyList<Guid> GetInstalledServices(ulong address)
    {
        var info = NewInfo(address);
        if (BluetoothGetDeviceInfo(IntPtr.Zero, ref info) != 0) return [];
        var buffer = new Guid[64];
        uint count = (uint)buffer.Length;
        if (BluetoothEnumerateInstalledServices(IntPtr.Zero, ref info, ref count, buffer) != 0) return [];
        return buffer.Take((int)Math.Min(count, (uint)buffer.Length)).ToList();
    }

    private static BLUETOOTH_DEVICE_INFO NewInfo(ulong address = 0) => new()
    {
        dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
        Address = address,
    };

    private static BtDevice ToDevice(in BLUETOOTH_DEVICE_INFO info) =>
        new(info.szName ?? "", info.Address, info.fConnected != 0, info.ulClassofDevice);

    public BtDevice? GetDevice(ulong address)
    {
        var info = NewInfo(address);
        var r = BluetoothGetDeviceInfo(IntPtr.Zero, ref info);
        if (r == 0) return ToDevice(info);
        if (r == ErrorNotFound) return null;
        throw new BluetoothException($"BluetoothGetDeviceInfo failed (Windows error {r}).");
    }

    public uint SetServiceState(ulong address, Guid service, bool enable)
    {
        var info = NewInfo(address);
        var r = BluetoothGetDeviceInfo(IntPtr.Zero, ref info);
        if (r == ErrorNotFound)
            throw new DeviceNotFoundException($"Device {MacAddress.Format(address)} is not known to Windows.");
        if (r != 0)
            throw new BluetoothException($"BluetoothGetDeviceInfo failed (Windows error {r}).");
        return BluetoothSetServiceState(IntPtr.Zero, ref info, ref service, enable ? 1u : 0u);
    }

    public IReadOnlyList<BtDevice> EnumeratePaired()
    {
        var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1,
            fReturnRemembered = 1,
            fReturnConnected = 1,
            fReturnUnknown = 0,
            fIssueInquiry = 0,        // no radio scan: only what Windows already knows
            cTimeoutMultiplier = 0,
            hRadio = IntPtr.Zero,
        };
        var info = NewInfo();
        var devices = new List<BtDevice>();

        var handle = BluetoothFindFirstDevice(ref search, ref info);
        if (handle == IntPtr.Zero) return devices;   // no radio or no devices
        try
        {
            do { devices.Add(ToDevice(info)); }
            while (BluetoothFindNextDevice(handle, ref info));
        }
        finally { BluetoothFindDeviceClose(handle); }
        return devices;
    }
}
