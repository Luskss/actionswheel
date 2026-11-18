using System.Windows.Media;
using AcoesRapidas.UI;

namespace AcoesRapidas.Tests;

public class ThemePaletteTests
{
    private const double ReadableContrast = 4.5;

    public static TheoryData<bool, string> AccentSamples()
    {
        var data = new TheoryData<bool, string>();
        string[] accents =
        [
            "#0078D4", "#FFB900", "#E81123", "#107C10", "#00B7C3", "#8764B8",
            "#FF8C00", "#E3008C", "#767676", "#000000", "#FFFFFF", "#FFFF00",
            "#00FF00", "#00FFFF", "#FF00FF", "#7A7574", "#498205", "#038387",
        ];

        foreach (var accent in accents)
        {
            data.Add(true, accent);
            data.Add(false, accent);
        }

        return data;
    }

    private static ThemePalette Create(bool isLight, string hex)
    {
        var accent = (Color)ColorConverter.ConvertFromString(hex);
        return ThemePalette.Create(WindowsTheme.DeriveShades(accent), isLight);
    }

    [Theory]
    [MemberData(nameof(AccentSamples))]
    public void TextOnSelectedSliceIsReadable(bool isLight, string accent)
    {
        var palette = Create(isLight, accent);

        Assert.True(ColorMath.Contrast(palette.OnAccent, palette.AccentStart) >= ReadableContrast);
        Assert.True(ColorMath.Contrast(palette.OnAccent, palette.AccentEnd) >= ReadableContrast);
    }

    [Theory]
    [MemberData(nameof(AccentSamples))]
    public void TextOnSlicesAndHubIsReadable(bool isLight, string accent)
    {
        var palette = Create(isLight, accent);

        Assert.True(ColorMath.Contrast(palette.Text, palette.Slice) >= ReadableContrast);
        Assert.True(ColorMath.Contrast(palette.MutedText, palette.Slice) >= ReadableContrast);
        Assert.True(ColorMath.Contrast(palette.Text, palette.Hub) >= ReadableContrast);
        Assert.True(ColorMath.Contrast(palette.MutedText, palette.Hub) >= ReadableContrast);
    }

    [Fact]
    public void ErrorColorsAreReadable()
    {
        var palette = Create(false, "#0078D4");

        Assert.True(ColorMath.Contrast(palette.OnDanger, palette.Danger) >= ReadableContrast);
    }

    [Fact]
    public void ParsesAccentPaletteInRgbaOrder()
    {
        var bytes = new byte[32];
        bytes[4] = 0x11; bytes[5] = 0x22; bytes[6] = 0x33;
        bytes[12] = 0x44; bytes[13] = 0x55; bytes[14] = 0x66;
        bytes[16] = 0x77; bytes[17] = 0x88; bytes[18] = 0x99;

        var shades = WindowsTheme.ParseAccentPalette(bytes);

        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), shades?.Light);
        Assert.Equal(Color.FromRgb(0x44, 0x55, 0x66), shades?.Accent);
        Assert.Equal(Color.FromRgb(0x77, 0x88, 0x99), shades?.Dark);
    }

    [Fact]
    public void IncompleteAccentPaletteIsIgnored()
    {
        Assert.Null(WindowsTheme.ParseAccentPalette(new byte[10]));
        Assert.Null(WindowsTheme.ParseAccentPalette(null));
    }

    [Fact]
    public void ConvertsRegistryColorFromAbgr()
    {
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), WindowsTheme.FromAbgr(unchecked((int)0xFF332211)));
    }

    [Theory]
    [InlineData(350, 10, 20)]
    [InlineData(10, 350, -20)]
    [InlineData(0, 90, 90)]
    [InlineData(720, 60, 60)]
    [InlineData(-30, 30, 60)]
    public void SelectorTurnsTheShortestWay(double from, double to, double expected)
    {
        Assert.Equal(expected, RadialMenuGeometry.ShortestRotation(from, to), 6);
    }
}
