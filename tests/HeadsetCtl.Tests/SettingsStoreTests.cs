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
    public void DefaultPath_IsUnderAppData() =>
        Assert.EndsWith(Path.Combine("HeadsetCtl", "settings.json"), SettingsStore.DefaultPath);
}
