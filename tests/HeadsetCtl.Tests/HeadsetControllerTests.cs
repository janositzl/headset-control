using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class HeadsetControllerTests
{
    private const ulong Addr = 0x00A41C06640C;

    private static (HeadsetController ctl, FakeBluetoothApi api) Make(bool connected = false)
    {
        var api = new FakeBluetoothApi();
        api.Devices.Add(Fake.Audio("WH-CH720N", Addr, connected));
        return (new HeadsetController(api, TimeSpan.Zero, TimeSpan.Zero), api);
    }

    [Fact]
    public async Task Connect_EnablesAllThreeServices()
    {
        var (ctl, api) = Make();
        Assert.True(await ctl.SetConnectedAsync(Addr, true));
        Assert.Equal(HeadsetController.AudioServices, api.Calls.Select(c => c.Service));
        Assert.All(api.Calls, c => Assert.True(c.Enable));
    }

    [Fact]
    public async Task Disconnect_DisablesAllThreeServices()
    {
        var (ctl, api) = Make(connected: true);
        Assert.False(await ctl.SetConnectedAsync(Addr, false));
        Assert.Equal(3, api.Calls.Count);
        Assert.All(api.Calls, c => Assert.False(c.Enable));
    }

    [Fact]
    public void Services_AreTheExpectedGuids()
    {
        Assert.Equal(Guid.Parse("0000110B-0000-1000-8000-00805F9B34FB"), HeadsetController.A2dpSink);
        Assert.Equal(Guid.Parse("0000111E-0000-1000-8000-00805F9B34FB"), HeadsetController.Handsfree);
        Assert.Equal(Guid.Parse("00001108-0000-1000-8000-00805F9B34FB"), HeadsetController.Headset);
    }

    [Fact]
    public async Task Error87_UnsupportedService_IsIgnored()
    {
        var (ctl, api) = Make();
        api.ServiceResults[HeadsetController.Handsfree] = 87;
        api.ServiceResults[HeadsetController.Headset] = 87;
        Assert.True(await ctl.SetConnectedAsync(Addr, true));
    }

    [Fact]
    public async Task OtherError_WhenStateNotReached_Throws()
    {
        var (ctl, api) = Make();
        foreach (var g in HeadsetController.AudioServices) api.ServiceResults[g] = 5;
        var ex = await Assert.ThrowsAsync<BluetoothException>(() => ctl.SetConnectedAsync(Addr, true));
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public async Task OtherError_ButStateReached_DoesNotThrow()
    {
        var (ctl, api) = Make();
        api.ServiceResults[HeadsetController.Handsfree] = 5;
        api.ServiceResults[HeadsetController.Headset] = 5;
        Assert.True(await ctl.SetConnectedAsync(Addr, true));   // A2DP succeeded and flipped the state
    }

    [Fact]
    public async Task NoError_ButStateNotReached_ReturnsActualState()
    {
        var (ctl, api) = Make();
        api.ApplyStateChange = false;
        Assert.False(await ctl.SetConnectedAsync(Addr, true));
    }

    private static readonly Guid AvrcpController = Guid.Parse("0000110E-0000-1000-8000-00805F9B34FB");
    private static readonly Guid Spp = Guid.Parse("00001101-0000-1000-8000-00805F9B34FB");

    private static (HeadsetController ctl, FakeBluetoothApi api) MakeAvrcpOnly(bool connected)
    {
        // Like the Baseus Bowie MA10: no A2DP/Handsfree/Headset services, link carried by AVRCP.
        var (ctl, api) = Make(connected);
        api.SupportedServices = new HashSet<Guid> { AvrcpController };
        api.InstalledServices.AddRange(new[] { Spp, AvrcpController });
        return (ctl, api);
    }

    [Fact]
    public async Task Disconnect_WhenAudioServicesNotFound_FallsBackToInstalledServices()
    {
        var (ctl, api) = MakeAvrcpOnly(connected: true);
        Assert.False(await ctl.SetConnectedAsync(Addr, false));
        Assert.Contains(api.Calls, c => c.Service == AvrcpController && !c.Enable);
    }

    [Fact]
    public async Task Connect_WhenAudioServicesNotFound_FallsBackToInstalledServices()
    {
        var (ctl, api) = MakeAvrcpOnly(connected: false);
        Assert.True(await ctl.SetConnectedAsync(Addr, true));
        Assert.Contains(api.Calls, c => c.Service == AvrcpController && c.Enable);
    }

    [Fact]
    public async Task WhenAudioServicesWork_InstalledServicesAreLeftAlone()
    {
        var (ctl, api) = Make(connected: true);
        api.InstalledServices.Add(Spp);
        Assert.False(await ctl.SetConnectedAsync(Addr, false));
        Assert.DoesNotContain(api.Calls, c => c.Service == Spp);
    }

    [Fact]
    public async Task WhenAudioServicesAccepted_NeverTouchesOtherServices_EvenIfStateDoesNotChange()
    {
        // A device with working audio services (the Sony) must only ever see the audio path.
        var (ctl, api) = Make(connected: true);
        api.InstalledServices.AddRange(new[] { Spp, AvrcpController });
        api.ApplyStateChange = false;   // audio calls return 0 but the link has not (yet) dropped
        await ctl.SetConnectedAsync(Addr, false);
        Assert.All(api.Calls, c => Assert.Contains(c.Service, HeadsetController.AudioServices));
    }

    [Fact]
    public async Task Fallback_OnlyUsesAvrcp_NotSerialPortOrCustomServices()
    {
        var (ctl, api) = MakeAvrcpOnly(connected: true);
        api.InstalledServices.Add(Guid.NewGuid());   // a vendor-specific service
        await ctl.SetConnectedAsync(Addr, false);
        Assert.DoesNotContain(api.Calls, c => c.Service == Spp);
        Assert.All(api.Calls, c => Assert.True(
            HeadsetController.AudioServices.Contains(c.Service) || HeadsetController.AvrcpServices.Contains(c.Service)));
    }

    [Fact]
    public async Task Fallback_AlsoFailing_ThrowsWithErrorCode()
    {
        var (ctl, api) = Make(connected: true);
        api.SupportedServices = new HashSet<Guid>();     // everything returns 1168
        api.InstalledServices.Add(AvrcpController);
        var ex = await Assert.ThrowsAsync<BluetoothException>(() => ctl.SetConnectedAsync(Addr, false));
        Assert.Contains("1168", ex.Message);
    }

    [Fact]
    public async Task Toggle_WhenConnected_Disconnects()
    {
        var (ctl, _) = Make(connected: true);
        Assert.False(await ctl.ToggleAsync(Addr));
    }

    [Fact]
    public async Task Toggle_WhenDisconnected_Connects()
    {
        var (ctl, _) = Make(connected: false);
        Assert.True(await ctl.ToggleAsync(Addr));
    }

    [Fact]
    public void IsConnected_UnknownDevice_Throws()
    {
        var (ctl, _) = Make();
        Assert.Throws<DeviceNotFoundException>(() => ctl.IsConnected(0x1));
    }
}
