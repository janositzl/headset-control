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
        uint firstError = 0;

        // The service calls block for a second or two, so keep them off the caller's (UI) thread.
        var audio = await Task.Run(() => Apply(address, AudioServices, connect), ct);
        firstError = audio.FirstError;

        // Devices without A2DP/Handsfree/Headset (e.g. AVRCP-only earphones) answer "not found" to all of
        // those; skip the wait and go straight to the services Windows has installed for the device.
        var state = audio.AnySucceeded && await WaitForStateAsync(address, connect, ct);
        if (!state)
        {
            var extra = await Task.Run(() =>
                Apply(address, _api.GetInstalledServices(address).Where(s => !AudioServices.Contains(s)).ToList(), connect), ct);
            if (firstError == 0) firstError = extra.FirstError;

            if (!await WaitForStateAsync(address, connect, ct) && firstError != 0)
                throw new BluetoothException(
                    $"Could not {(connect ? "connect" : "disconnect")} {MacAddress.Format(address)} (Windows error {firstError}).");
        }

        return IsConnected(address);
    }

    private (uint FirstError, bool AnySucceeded) Apply(ulong address, IReadOnlyList<Guid> services, bool connect)
    {
        uint error = 0;
        bool any = false;
        foreach (var service in services)
        {
            var r = _api.SetServiceState(address, service, connect);
            if (r == 0) any = true;
            else if (r != ErrorInvalidParameter && error == 0) error = r;
        }
        return (error, any);
    }

    private async Task<bool> WaitForStateAsync(ulong address, bool target, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _settleTimeout;
        while (true)
        {
            if (IsConnected(address) == target) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(_pollInterval, ct);
        }
    }

    public async Task<bool> ToggleAsync(ulong address, CancellationToken ct = default) =>
        await SetConnectedAsync(address, !IsConnected(address), ct);
}
