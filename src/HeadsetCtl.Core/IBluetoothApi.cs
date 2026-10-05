namespace HeadsetCtl.Core;

public interface IBluetoothApi
{
    /// <summary>Returns the device, or null if Windows does not know it.</summary>
    BtDevice? GetDevice(ulong address);

    /// <summary>Win32 error code; 0 = success. enable=true connects the service, false disconnects it.</summary>
    uint SetServiceState(ulong address, Guid service, bool enable);

    /// <summary>All paired (authenticated or remembered) devices. Empty when there is no radio.</summary>
    IReadOnlyList<BtDevice> EnumeratePaired();
}
