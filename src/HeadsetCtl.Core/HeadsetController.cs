namespace HeadsetCtl.Core;

public sealed class HeadsetController
{
    private const uint ErrorInvalidParameter = 87;   // service not supported by this device

    public static readonly Guid A2dpSink = Guid.Parse("0000110B-0000-1000-8000-00805F9B34FB");
    public static readonly Guid Handsfree = Guid.Parse("0000111E-0000-1000-8000-00805F9B34FB");
    public static readonly Guid Headset = Guid.Parse("00001108-0000-1000-8000-00805F9B34FB");
    public static readonly Guid[] AudioServices = { A2dpSink, Handsfree, Headset };

    private readonly IBluetoothApi _api;
    private readonly TimeSpan _settleTimeout;
    private readonly TimeSpan _pollInterval;

    public HeadsetController(IBluetoothApi api, TimeSpan? settleTimeout = null, TimeSpan? pollInterval = null)
    {
        _api = api;
        _settleTimeout = settleTimeout ?? TimeSpan.FromSeconds(3);
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);
    }

    public bool IsConnected(ulong address) =>
        (_api.GetDevice(address) ?? throw new DeviceNotFoundException($"Device {MacAddress.Format(address)} is not known to Windows."))
            .Connected;

    public async Task<bool> SetConnectedAsync(ulong address, bool connect, CancellationToken ct = default)
    {
        // The service calls block for a second or two, so keep them off the caller's (UI) thread.
        uint firstError = await Task.Run(() =>
        {
            uint error = 0;
            foreach (var service in AudioServices)
            {
                var r = _api.SetServiceState(address, service, connect);
                if (r != 0 && r != ErrorInvalidParameter && error == 0) error = r;
            }
            return error;
        }, ct);

        var deadline = DateTime.UtcNow + _settleTimeout;
        while (true)
        {
            var state = IsConnected(address);
            if (state == connect) return state;
            if (DateTime.UtcNow >= deadline)
            {
                if (firstError != 0)
                    throw new BluetoothException(
                        $"Could not {(connect ? "connect" : "disconnect")} {MacAddress.Format(address)} (Windows error {firstError}).");
                return state;
            }
            await Task.Delay(_pollInterval, ct);
        }
    }

    public async Task<bool> ToggleAsync(ulong address, CancellationToken ct = default) =>
        await SetConnectedAsync(address, !IsConnected(address), ct);
}
