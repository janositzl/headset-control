using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class MacAddressTests
{
    [Fact]
    public void Format_UsesUppercaseColonSeparatedPairs() =>
        Assert.Equal("00:A4:1C:06:64:0C", MacAddress.Format(0x00A41C06640C));

    [Theory]
    [InlineData("00:A4:1C:06:64:0C")]
    [InlineData("00-a4-1c-06-64-0c")]
    [InlineData("00A41C06640C")]
    [InlineData("00a41c06640c")]
    [InlineData("  00:a4:1c:06:64:0c  ")]
    public void TryParse_AcceptsCommonFormats(string text)
    {
        Assert.True(MacAddress.TryParse(text, out var address));
        Assert.Equal(0x00A41C06640CUL, address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00:A4:1C:06:64")]          // too short
    [InlineData("00:A4:1C:06:64:0C:11")]    // too long
    [InlineData("ZZ:A4:1C:06:64:0C")]       // not hex
    [InlineData("00:A4-1C:06:64:0C")]       // mixed separators
    [InlineData("00A41C06640")]             // 11 digits
    [InlineData("+0A41C06640C")]            // sign is not a hex digit
    [InlineData("WH-CH720N")]               // a device name
    public void TryParse_RejectsInvalidInput(string? text)
    {
        Assert.False(MacAddress.TryParse(text, out var address));
        Assert.Equal(0UL, address);
    }
}
