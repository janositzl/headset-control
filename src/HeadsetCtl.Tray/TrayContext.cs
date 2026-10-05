using HeadsetCtl.Core;

namespace HeadsetCtl.Tray;

internal sealed class TrayContext : ApplicationContext
{
    private enum TrayState { Connected, Disconnected, Warning }

    private readonly IBluetoothApi _api = new NativeBt();
    private readonly HeadsetController _controller;
    private readonly DeviceLocator _locator;
    private readonly SettingsStore _store = new(SettingsStore.DefaultPath);
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly Icon _iconOn = IconFactory.Create(Color.SeaGreen);
    private readonly Icon _iconOff = IconFactory.Create(Color.Gray);
    private readonly Icon _iconWarn = IconFactory.Create(Color.DarkOrange);
    private readonly ToolStripMenuItem _deviceMenu = new("Device");
    private readonly ToolStripMenuItem _autostartItem = new("Start with Windows");
    private readonly ToolStripMenuItem _connectItem = new("Connect");
    private readonly ToolStripMenuItem _disconnectItem = new("Disconnect");
    private bool _isBusy;

    /// <summary>True while a connect/disconnect is running; the menu items that could start another are greyed out.</summary>
    private bool Busy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            _connectItem.Enabled = _disconnectItem.Enabled = _deviceMenu.Enabled = !value;
        }
    }

    public TrayContext()
    {
        _controller = new HeadsetController(_api);
        _locator = new DeviceLocator(_api);

        var menu = new ContextMenuStrip();
        _connectItem.Click += async (_, _) => await SetAsync(true);
        _disconnectItem.Click += async (_, _) => await SetAsync(false);
        menu.Items.Add(_connectItem);
        menu.Items.Add(_disconnectItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_deviceMenu);
        _autostartItem.Click += (_, _) => SafeRun(() => Autostart.Set(!Autostart.IsEnabled));
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) =>
        {
            RebuildDeviceMenu();
            _autostartItem.Checked = Autostart.IsEnabled;
        };

        _tray = new NotifyIcon { Icon = _iconOff, Text = "Headset", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += async (_, e) => { if (e.Button == MouseButtons.Left) await ToggleAsync(); };

        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _hotkeyWindow.Pressed += async () => await ToggleAsync();
        RegisterHotkey();

        _ = RefreshAsync();
    }

    private BtDevice ResolveSelected() => _locator.ResolveSelected(_store.Load().DeviceMac);

    private void RegisterHotkey()
    {
        var text = _store.Load().Hotkey;
        if (!HotkeyParser.TryParse(text, out var hotkey))
        {
            Notify($"Invalid hotkey '{text}' in settings.json; using {Settings.DefaultHotkey}.");
            text = Settings.DefaultHotkey;
            HotkeyParser.TryParse(text, out hotkey);
        }
        if (!_hotkeyWindow.Register(hotkey))
            Notify($"Hotkey {text} is already in use by another program.");
    }

    private ConnectionMode ModeFor(BtDevice device) => _store.Load().GetMode(device.Mac);

    private Task ToggleAsync() =>
        PerformAsync(device => _controller.ToggleAsync(device.Address, ModeFor(device)));

    private Task SetAsync(bool connect) =>
        PerformAsync(device => _controller.SetConnectedAsync(device.Address, connect, ModeFor(device)));

    /// <summary>Runs one user-requested action; ignores requests while another is in flight.</summary>
    private async Task PerformAsync(Func<BtDevice, Task<bool>> action)
    {
        if (Busy) return;
        Busy = true;
        SetText("Working...");
        try
        {
            var device = await Task.Run(ResolveSelected);
            var connected = await action(device);
            Show(connected ? TrayState.Connected : TrayState.Disconnected, device);
        }
        catch (Exception e)
        {
            Show(TrayState.Warning, null, e.Message);
            Notify(e.Message);
        }
        finally { Busy = false; }
    }

    /// <summary>Background poll: updates icon only, never raises balloons.</summary>
    private async Task RefreshAsync()
    {
        if (Busy) return;
        try
        {
            var (device, connected) = await Task.Run(() =>
            {
                var d = ResolveSelected();
                return (d, _controller.IsConnected(d.Address));
            });
            if (Busy) return;
            Show(connected ? TrayState.Connected : TrayState.Disconnected, device);
        }
        catch (Exception e)
        {
            if (!Busy) Show(TrayState.Warning, null, e.Message);
        }
    }

    private void Show(TrayState state, BtDevice? device, string? message = null)
    {
        _tray.Icon = state switch
        {
            TrayState.Connected => _iconOn,
            TrayState.Disconnected => _iconOff,
            _ => _iconWarn,
        };
        SetText(state switch
        {
            TrayState.Connected => $"{device!.Name}: Connected",
            TrayState.Disconnected => $"{device!.Name}: Disconnected",
            _ => message ?? "Headset unavailable",
        });
    }

    private void SetText(string text) => _tray.Text = text.Length > 63 ? text[..63] : text;

    private void Notify(string message) => _tray.ShowBalloonTip(4000, "HeadsetCtl", message, ToolTipIcon.Warning);

    private void SafeRun(Action action)
    {
        try { action(); }
        catch (Exception e) { Notify(e.Message); }
    }

    private void RebuildDeviceMenu()
    {
        _deviceMenu.DropDownItems.Clear();

        string? selectedMac = null;
        try { selectedMac = ResolveSelected().Mac; } catch (BluetoothException) { }

        IReadOnlyList<BtDevice> devices;
        try { devices = _locator.ListPaired(); } catch (BluetoothException) { devices = []; }

        if (devices.Count == 0)
        {
            _deviceMenu.DropDownItems.Add(new ToolStripMenuItem("No paired audio devices") { Enabled = false });
            return;
        }

        foreach (var device in devices)
        {
            var mac = device.Mac;
            var name = string.IsNullOrWhiteSpace(device.Name) ? "(unnamed)" : device.Name;
            var item = new ToolStripMenuItem($"{name} ({mac})") { Checked = mac == selectedMac };
            item.Click += async (_, _) =>
            {
                SafeRun(() =>
                {
                    var settings = _store.Load();
                    settings.DeviceMac = mac;
                    _store.Save(settings);
                });
                await RefreshAsync();
            };
            _deviceMenu.DropDownItems.Add(item);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _hotkeyWindow.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _iconOn.Dispose();
            _iconOff.Dispose();
            _iconWarn.Dispose();
        }
        base.Dispose(disposing);
    }
}
