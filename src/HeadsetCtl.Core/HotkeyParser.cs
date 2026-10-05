namespace HeadsetCtl.Core;

public readonly record struct Hotkey(uint Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    public const uint Alt = 1, Control = 2, Shift = 4, Win = 8;   // RegisterHotKey MOD_* values

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length < 2 || tokens.Any(t => t.Length == 0)) return false;

        uint modifiers = 0;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            uint flag = tokens[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => Control,
                "alt" => Alt,
                "shift" => Shift,
                "win" => Win,
                _ => 0,
            };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }

        if (!TryParseKey(tokens[^1], out var vk)) return false;
        hotkey = new Hotkey(modifiers, vk);
        return true;
    }

    private static bool TryParseKey(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { vk = c; return true; }
            return false;
        }

        if ((token[0] == 'F' || token[0] == 'f') && int.TryParse(token.AsSpan(1), out var n) && n >= 1 && n <= 24)
        {
            vk = (uint)(0x70 + n - 1);
            return true;
        }
        return false;
    }
}
