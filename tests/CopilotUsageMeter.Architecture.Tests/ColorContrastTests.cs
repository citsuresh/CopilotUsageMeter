using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ColorContrastTests
{
    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(255, 255, 255, true)]
    [InlineData(232, 137, 12, true)]
    [InlineData(47, 111, 222, false)]
    [InlineData(209, 52, 56, false)]
    public void TextUsesTheHigherContrastForeground(byte red, byte green, byte blue, bool dark)
    {
        Assert.Equal(dark, ColorContrast.UseDarkText(red, green, blue));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 1)]
    [InlineData(128, 0.2158605001)]
    public void LuminanceMatchesReferenceGreyValues(byte value, double expected)
    {
        Assert.Equal(expected, ColorContrast.Luminance(value, value, value), 8);
    }

    [Fact]
    public void AmberGlyphExceedsSmallTextContrastThreshold()
    {
        var luminance = ColorContrast.Luminance(232, 137, 12);
        Assert.True(ColorContrast.UseDarkText(232, 137, 12));
        Assert.InRange((luminance + 0.05) / 0.05, 8.0, 8.02);
        Assert.InRange(1.05 / (luminance + 0.05), 2.61, 2.63);
    }

    [Fact]
    public void SelectedForegroundMeetsFourPointFiveAcrossTheRgbCube()
    {
        for (var red = 0; red <= 255; red += 17)
        for (var green = 0; green <= 255; green += 17)
        for (var blue = 0; blue <= 255; blue += 17)
        {
            var r = (byte)red;
            var g = (byte)green;
            var b = (byte)blue;
            var luminance = ColorContrast.Luminance(r, g, b);
            var ratio = ColorContrast.UseDarkText(r, g, b) ? (luminance + 0.05) / 0.05 : 1.05 / (luminance + 0.05);
            Assert.True(ratio >= 4.5, $"Contrast below threshold at {red},{green},{blue}: {ratio}");
        }
    }
}