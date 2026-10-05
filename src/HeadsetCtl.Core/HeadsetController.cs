namespace HeadsetCtl.Core;

/// <summary>Which services are switched to connect/disconnect a device. Chosen per device, never guessed.</summary>
public enum ConnectionMode
{
    /// <summary>A2DP / Handsfree / Headset services. Proven for Sony WH-CH720N. The default.</summary>
    Audio,

    /// <summary>Disconnect via the AVRCP services only (link carried by AVRCP, e.g. Baseus Bowie MA10).</summary>
    Avrcp,
}

public sealed class HeadsetController
{
    private const uint ErrorInvalidParameter = 87;   // service not supported by this device

    public static readonly Guid A2dpSink = Guid.Parse("0000110B-0000-1000-8000-00805F9B34FB");
    public static readonly Guid Handsfree = Guid.Parse("0000111E-0000-1000-8000-00805F9B34FB");
    public static readonly Guid Headset = Guid.Parse("00001108-0000-1000-8000-00805F9B34FB");
    public static readonly Guid[] AudioServices = { A2dpSink, Handsfree, Headset };

    public static readonly Guid[] AvrcpServices =
    {
        Guid.Parse("0000110E-0000-1000-8000-00805F9B34FB"),   // AVRCP controller
        Guid.Parse("0000110C-0000-1000-8000-00805F9B34FB"),   // AVRCP target
    };

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

    /// <summary>Audio mode touches only the audio services. Avrcp mode connects with audio + AVRCP but
    /// disconnects with AVRCP only: disabling audio services on such devices kills the sound without dropping the link.</summary>
    private static IReadOnlyList<Guid> ServicesFor(ConnectionMode mode, bool connect) =>
        mode == ConnectionMode.Avrcp
            ? (connect ? AudioServices.Concat(AvrcpServices).ToList() : AvrcpServices)
            : AudioServices;

    public async Task<bool> SetConnectedAsync(
        ulong address, bool connect, ConnectionMode mode = ConnectionMode.Audio, CancellationToken ct = default)
    {
        // The service calls block for a second or two, so keep them off the caller's (UI) thread.
        var services = ServicesFor(mode, connect);
        uint firstError = await Task.Run(() => Apply(address, services, connect), ct);

        await WaitForStateAsync(address, connect, ct);

        var state = IsConnected(address);
        if (state != connect && firstError != 0)
            throw new BluetoothException(
                $"Could not {(connect ? "connect" : "disconnect")} {MacAddress.Format(address)} (Windows error {firstError}).");
        return state;
    }

    public async Task<bool> ToggleAsync(
        ulong address, ConnectionMode mode = ConnectionMode.Audio, CancellationToken ct = default) =>
        await SetConnectedAsync(address, !IsConnected(address), mode, ct);

    /// <returns>The first error other than 0 / "service not supported", or 0.</returns>
    private uint Apply(ulong address, IReadOnlyList<Guid> services, bool connect)
    {
        uint error = 0;
        foreach (var service in services)
        {
            var r = _api.SetServiceState(address, service, connect);
            if (r != 0 && r != ErrorInvalidParameter && error == 0) error = r;
        }
        return error;
    }

    private async Task WaitForStateAsync(ulong address, bool target, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _settleTimeout;
        while (IsConnected(address) != target && DateTime.UtcNow < deadline)
            await Task.Delay(_pollInterval, ct);
    }
}
