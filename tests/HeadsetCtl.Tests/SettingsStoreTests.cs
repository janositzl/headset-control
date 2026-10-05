using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HeadsetCtlTests-" + Guid.NewGuid());
    private string FilePath => Path.Combine(_dir, "sub", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void WriteRaw(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = new SettingsStore(FilePath).Load();
        Assert.Null(s.DeviceMac);
        Assert.Equal("Ctrl+Alt+H", s.Hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"Hotkey\": ")]        // truncated
    [InlineData("[1,2,3]")]               // wrong shape
    [InlineData("null")]
    public void Load_EmptyOrCorruptFile_ReturnsDefaults(string content)
    {
        WriteRaw(content);
        var s = new SettingsStore(FilePath).Load();
        Assert.Null(s.DeviceMac);
        Assert.Equal("Ctrl+Alt+H", s.Hotkey);
    }

    [Fact]
    public void Load_NullOrBlankHotkey_FallsBackToDefault()
    {
        WriteRaw("{ \"DeviceMac\": \"AA:BB:CC:11:22:33\", \"Hotkey\": null }");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal("AA:BB:CC:11:22:33", s.DeviceMac);
        Assert.Equal("Ctrl+Alt+H", s.Hotkey);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_AndCreatesDirectory()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new Settings { DeviceMac = "00:A4:1C:06:64:0C", Hotkey = "Ctrl+Shift+F9" });
        var s = store.Load();
        Assert.Equal("00:A4:1C:06:64:0C", s.DeviceMac);
        Assert.Equal("Ctrl+Shift+F9", s.Hotkey);
    }

    [Fact]
    public void GetMode_DefaultsToAudio() =>
        Assert.Equal(ConnectionMode.Audio, new Settings().GetMode("00:A4:1C:06:64:0C"));

    [Fact]
    public void SetMode_RoundTripsThroughFile_AndIsCaseInsensitiveOnMac()
    {
        var store = new SettingsStore(FilePath);
        var s = new Settings();
        s.SetMode("41:AA:00:DB:D8:D1", ConnectionMode.Avrcp);
        store.Save(s);

        var loaded = store.Load();
        Assert.Equal(ConnectionMode.Avrcp, loaded.GetMode("41:AA:00:DB:D8:D1"));
        Assert.Equal(ConnectionMode.Avrcp, loaded.GetMode("41:aa:00:db:d8:d1"));
        Assert.Equal(ConnectionMode.Audio, loaded.GetMode("00:A4:1C:06:64:0C"));
    }

    [Fact]
    public void SetMode_ReplacesExistingEntry()
    {
        var s = new Settings();
        s.SetMode("41:AA:00:DB:D8:D1", ConnectionMode.Avrcp);
        s.SetMode("41:aa:00:db:d8:d1", ConnectionMode.Audio);
        Assert.Single(s.DeviceModes);
        Assert.Equal(ConnectionMode.Audio, s.GetMode("41:AA:00:DB:D8:D1"));
    }

    [Theory]
    [InlineData("{ \"DeviceModes\": null }")]
    [InlineData("{ \"DeviceModes\": { \"41:AA:00:DB:D8:D1\": \"nonsense\" } }")]
    [InlineData("{ \"DeviceModes\": { \"41:AA:00:DB:D8:D1\": null } }")]
    public void Load_BadDeviceModes_FallBackToAudio(string content)
    {
        WriteRaw(content);
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(ConnectionMode.Audio, s.GetMode("41:AA:00:DB:D8:D1"));
    }

    [Fact]
    public void DefaultPath_IsUnderAppData() =>
        Assert.EndsWith(Path.Combine("HeadsetCtl", "settings.json"), SettingsStore.DefaultPath);
}
