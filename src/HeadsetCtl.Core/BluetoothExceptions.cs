namespace HeadsetCtl.Core;

public class BluetoothException : Exception
{
    public BluetoothException(string message) : base(message) { }
    public BluetoothException(string message, Exception inner) : base(message, inner) { }
}

public sealed class DeviceNotFoundException(string message) : BluetoothException(message);

public sealed class AmbiguousDeviceException(string message) : BluetoothException(message);
