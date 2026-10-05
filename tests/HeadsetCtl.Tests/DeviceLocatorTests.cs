using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class DeviceLocatorTests
{
    private static readonly BtDevice Headset = Fake.Audio("WH-CH720N", 0x00A41C06640C);
    private static readonly BtDevice Other = Fake.Audio("Other-BT", 0xAABBCC112233, connected: true);
    private static readonly BtDevice Phone = Fake.Phone("Pixel", 0x112233445566);

    private static DeviceLocator Locator(params BtDevice[] devices)
    {
        var api = new FakeBluetoothApi();
        api.Devices.AddRange(devices);
        return new DeviceLocator(api);
    }

    [Fact]
    public void BtDevice_IsAudio_UsesMajorDeviceClass()
    {
        Assert.True(Headset.IsAudio);
        Assert.False(Phone.IsAudio);
        Assert.Equal("00:A4:1C:06:64:0C", Headset.Mac);
    }

    [Fact]
    public void ListPaired_ReturnsOnlyAudioDevicesSortedByName()
    {
        var list = Locator(Phone, Headset, Other).ListPaired();
        Assert.Equal(new[] { "Other-BT", "WH-CH720N" }, list.Select(d => d.Name));
    }

    [Fact]
    public void ListPaired_All_IncludesNonAudio() =>
        Assert.Equal(3, Locator(Phone, Headset, Other).ListPaired(includeAll: true).Count);

    [Fact]
    public void ListPaired_NoDevices_ReturnsEmpty() =>
        Assert.Empty(Locator().ListPaired());

    [Fact]
    public void Resolve_ByName_IsCaseInsensitive() =>
        Assert.Equal(Headset, Locator(Headset, Other).Resolve("wh-ch720n"));

    [Fact]
    public void Resolve_ByMac_AcceptsAnyFormat() =>
        Assert.Equal(Headset, Locator(Headset, Other).Resolve("00-a4-1c-06-64-0c"));

    [Fact]
    public void Resolve_UnknownName_Throws() =>
        Assert.Throws<DeviceNotFoundException>(() => Locator(Headset).Resolve("Nope"));

    [Fact]
    public void Resolve_UnknownMac_Throws() =>
        Assert.Throws<DeviceNotFoundException>(() => Locator(Headset).Resolve("11:22:33:44:55:66"));

    [Fact]
    public void Resolve_DuplicateName_ThrowsAmbiguousListingMacs()
    {
        var twin = Fake.Audio("WH-CH720N", 0x0000000000AA);
        var ex = Assert.Throws<AmbiguousDeviceException>(() => Locator(Headset, twin).Resolve("WH-CH720N"));
        Assert.Contains("00:A4:1C:06:64:0C", ex.Message);
        Assert.Contains("00:00:00:00:00:AA", ex.Message);
    }

    [Fact]
    public void Resolve_DuplicateName_CanStillBeChosenByMac() =>
        Assert.Equal(0xAAUL, Locator(Headset, Fake.Audio("WH-CH720N", 0xAA)).Resolve("00:00:00:00:00:AA").Address);

    [Fact]
    public void ResolveSelected_WithoutSavedMac_UsesDefaultName() =>
        Assert.Equal(Headset, Locator(Other, Headset).ResolveSelected(null));

    [Fact]
    public void ResolveSelected_WithSavedMac_UsesIt() =>
        Assert.Equal(Other, Locator(Other, Headset).ResolveSelected("AA:BB:CC:11:22:33"));
}
