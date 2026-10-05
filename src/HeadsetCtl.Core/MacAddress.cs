using System.Globalization;

namespace HeadsetCtl.Core;

public static class MacAddress
{
    public static string Format(ulong address)
    {
        var bytes = new string[6];
        for (int i = 0; i < 6; i++)
            bytes[i] = ((address >> (8 * (5 - i))) & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
        return string.Join(':', bytes);
    }

    public static bool TryParse(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        string hex = s;
        if (s.Length == 17 && (s[2] == ':' || s[2] == '-'))
        {
            var parts = s.Split(s[2]);
            if (parts.Length != 6 || parts.Any(p => p.Length != 2)) return false;
            hex = string.Concat(parts);
        }

        if (hex.Length != 12) return false;
        return ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
    }
}
