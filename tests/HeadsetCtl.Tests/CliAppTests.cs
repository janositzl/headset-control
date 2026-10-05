using HeadsetCtl.Core;
using HeadsetCtl.Cli;

namespace HeadsetCtl.Tests;

public class CliAppTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "HeadsetCtlCli-" + Guid.NewGuid() + ".json");
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly FakeBluetoothApi _api = new();

    public void Dispose() { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }

    private SettingsStore Store => new(_settingsPath);

    private CliApp App() => new(_api, new HeadsetController(_api, TimeSpan.Zero, TimeSpan.Zero), Store, _out, _err);

    private Task<int> Run(params string[] args) => App().RunAsync(args);

    private void Pair(params BtDevice[] devices) => _api.Devices.AddRange(devices);

    private static readonly BtDevice Headset = Fake.Audio("WH-CH720N", 0x00A41C06640C);
    private static readonly BtDevice Other = Fake.Audio("Other-BT", 0xAABBCC112233, connected: true);
    private static readonly BtDevice Phone = Fake.Phone("Pixel", 0x112233445566);

    [Fact]
    public async Task List_ShowsAudioDevicesWithMacAndState()
    {
        Pair(Headset, Other, Phone);
        Assert.Equal(CliApp.Ok, await Run("list"));
        var text = _out.ToString();
        Assert.Contains("NAME", text);
        Assert.Contains("MAC", text);
        Assert.Contains("00:A4:1C:06:64:0C", text);
        Assert.Contains("AA:BB:CC:11:22:33", text);
        Assert.Contains("Connected", text);
        Assert.Contains("Disconnected", text);
        Assert.DoesNotContain("Pixel", text);
    }

    [Fact]
    public async Task List_All_IncludesNonAudio()
    {
        Pair(Headset, Phone);
        Assert.Equal(CliApp.Ok, await Run("list", "--all"));
        Assert.Contains("Pixel", _out.ToString());
    }

    [Fact]
    public async Task List_NoDevices_PrintsFriendlyMessage()
    {
        Assert.Equal(CliApp.Ok, await Run("list"));
        Assert.Contains("No paired audio devices", _out.ToString());
    }

    [Fact]
    public async Task List_UnnamedDevice_ShowsPlaceholder()
    {
        Pair(Fake.Audio("", 0x1));
        Assert.Equal(CliApp.Ok, await Run("list"));
        Assert.Contains("(unnamed)", _out.ToString());
    }

    [Fact]
    public async Task Select_ByName_SavesMac()
    {
        Pair(Headset, Other);
        Assert.Equal(CliApp.Ok, await Run("select", "other-bt"));
        Assert.Equal("AA:BB:CC:11:22:33", Store.Load().DeviceMac);
        Assert.Contains("Other-BT", _out.ToString());
    }

    [Fact]
    public async Task Select_Unknown_ReturnsNotFound_AndSavesNothing()
    {
        Pair(Headset);
        Assert.Equal(CliApp.NotFound, await Run("select", "Nope"));
        Assert.Null(Store.Load().DeviceMac);
        Assert.NotEmpty(_err.ToString());
    }

    [Fact]
    public async Task Select_WithoutArgument_IsUsageError()
    {
        Assert.Equal(CliApp.Failure, await Run("select"));
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public async Task Status_UsesDefaultDeviceName_AndPrintsMac()
    {
        Pair(Headset, Other);
        Assert.Equal(CliApp.Ok, await Run("status"));
        Assert.Contains("WH-CH720N (00:A4:1C:06:64:0C): Disconnected", _out.ToString());
    }

    [Fact]
    public async Task Status_UsesSelectedDevice()
    {
        Pair(Headset, Other);
        await Run("select", "Other-BT");
        _out.GetStringBuilder().Clear();
        Assert.Equal(CliApp.Ok, await Run("status"));
        Assert.Contains("Other-BT (AA:BB:CC:11:22:33): Connected", _out.ToString());
    }

    [Fact]
    public async Task Status_DeviceOverride_BeatsSelection()
    {
        Pair(Headset, Other);
        await Run("select", "Other-BT");
        _out.GetStringBuilder().Clear();
        Assert.Equal(CliApp.Ok, await Run("status", "--device", "00:A4:1C:06:64:0C"));
        Assert.Contains("WH-CH720N", _out.ToString());
    }

    [Fact]
    public async Task Status_DeviceNotPaired_ReturnsNotFound()
    {
        Assert.Equal(CliApp.NotFound, await Run("status"));
        Assert.Contains("WH-CH720N", _err.ToString());
    }

    [Fact]
    public async Task On_ConnectsDevice()
    {
        Pair(Headset);
        Assert.Equal(CliApp.Ok, await Run("on"));
        Assert.Contains("Connected", _out.ToString());
        Assert.True(_api.Devices[0].Connected);
    }

    [Fact]
    public async Task Off_DisconnectsDevice()
    {
        Pair(Fake.Audio("WH-CH720N", 0x00A41C06640C, connected: true));
        Assert.Equal(CliApp.Ok, await Run("off"));
        Assert.False(_api.Devices[0].Connected);
    }

    [Fact]
    public async Task Toggle_FlipsState()
    {
        Pair(Headset);
        Assert.Equal(CliApp.Ok, await Run("toggle"));
        Assert.True(_api.Devices[0].Connected);
        Assert.Equal(CliApp.Ok, await Run("toggle"));
        Assert.False(_api.Devices[0].Connected);
    }

    [Fact]
    public async Task On_WhenWindowsDoesNotConnect_ReturnsFailure()
    {
        Pair(Headset);
        _api.ApplyStateChange = false;
        Assert.Equal(CliApp.Failure, await Run("on"));
        Assert.NotEmpty(_err.ToString());
    }

    [Fact]
    public async Task DuplicateNames_ReturnFailureMentioningMacs()
    {
        Pair(Headset, Fake.Audio("WH-CH720N", 0xAA));
        Assert.Equal(CliApp.Failure, await Run("status"));
        Assert.Contains("00:00:00:00:00:AA", _err.ToString());
    }

    [Fact]
    public async Task Mode_WithoutValue_ShowsAudioByDefault()
    {
        Pair(Headset);
        Assert.Equal(CliApp.Ok, await Run("mode", "WH-CH720N"));
        Assert.Contains("mode audio", _out.ToString());
    }

    [Fact]
    public async Task Mode_Set_IsSavedAndShown()
    {
        Pair(Other);
        Assert.Equal(CliApp.Ok, await Run("mode", "Other-BT", "AVRCP"));
        Assert.Contains("mode avrcp", _out.ToString());
        Assert.Equal(ConnectionMode.Avrcp, Store.Load().GetMode("AA:BB:CC:11:22:33"));
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("mode", "Other-BT", "bogus")]
    [InlineData("mode", "Other-BT", "avrcp", "extra")]
    public async Task Mode_BadArguments_AreUsageErrors(params string[] args)
    {
        Pair(Other);
        Assert.Equal(CliApp.Failure, await Run(args));
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public async Task Mode_UnknownDevice_ReturnsNotFound()
    {
        Assert.Equal(CliApp.NotFound, await Run("mode", "Nope", "avrcp"));
    }

    [Fact]
    public async Task Off_UsesTheDevicesSavedMode()
    {
        var baseus = Fake.Audio("Baseus", 0x41AA00DBD8D1, connected: true);
        Pair(baseus);
        _api.SupportedServices = new HashSet<Guid> { HeadsetController.AvrcpServices[0] };
        await Run("mode", "Baseus", "avrcp");
        Assert.Equal(CliApp.Ok, await Run("off"
            , "--device", "Baseus"));
        Assert.False(_api.Devices[0].Connected);
        Assert.All(_api.Calls, c => Assert.Contains(c.Service, HeadsetController.AvrcpServices));
    }

    [Fact]
    public async Task NoArguments_PrintsUsage()
    {
        Assert.Equal(CliApp.Failure, await Run());
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public async Task UnknownCommand_PrintsUsage()
    {
        Assert.Equal(CliApp.Failure, await Run("explode"));
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public async Task DeviceOption_WithoutValue_IsUsageError()
    {
        Assert.Equal(CliApp.Failure, await Run("status", "--device"));
        Assert.Contains("Usage", _err.ToString());
    }
}
