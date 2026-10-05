using System.Text.Json;

namespace HeadsetCtl.Core;

public sealed class Settings
{
    public const string DefaultHotkey = "Ctrl+Alt+H";

    public string? DeviceMac { get; set; }
    public string Hotkey { get; set; } = DefaultHotkey;
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeadsetCtl", "settings.json");

    public Settings Load()
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? new Settings();
            if (string.IsNullOrWhiteSpace(settings.Hotkey)) settings.Hotkey = Settings.DefaultHotkey;
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
