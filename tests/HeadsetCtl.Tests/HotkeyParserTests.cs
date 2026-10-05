using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", HotkeyParser.Control | HotkeyParser.Alt, 0x48u)]
    [InlineData("ctrl + alt + h", HotkeyParser.Control | HotkeyParser.Alt, 0x48u)]
    [InlineData("Control+Shift+F9", HotkeyParser.Control | HotkeyParser.Shift, 0x78u)]
    [InlineData("Win+Alt+5", HotkeyParser.Win | HotkeyParser.Alt, 0x35u)]
    [InlineData("Alt+F1", HotkeyParser.Alt, 0x70u)]
    [InlineData("Ctrl+F24", HotkeyParser.Control, 0x87u)]
    public void TryParse_ValidHotkeys(string text, uint modifiers, uint vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(modifiers, vk), hk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("H")]                 // no modifier
    [InlineData("Ctrl+Alt")]          // no key
    [InlineData("Ctrl+H+J")]          // two keys
    [InlineData("H+Ctrl")]            // key not last
    [InlineData("Ctrl+Ctrl+H")]       // duplicate modifier
    [InlineData("Ctrl+Alt+Foo")]      // unknown key
    [InlineData("Ctrl+F25")]          // out of range
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl++H")]           // empty token
    [InlineData("Ctrl+Alt+")]         // trailing separator
    public void TryParse_InvalidHotkeys(string? text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _));
    }
}
