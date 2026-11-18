using AcoesRapidas.UI;

namespace AcoesRapidas.Tests;

public class IconStylizerTests
{
    private static readonly byte[] Green = [96, 215, 30, 255];
    private static readonly byte[] Black = [0, 0, 0, 255];
    private static readonly byte[] Clear = [0, 0, 0, 0];

    private static byte[] Pixels(params byte[][] pixels) => pixels.SelectMany(pixel => pixel).ToArray();

    private static byte AlphaAt(byte[] mask, int pixel) => mask[(pixel * 4) + 3];

    [Fact]
    public void LogoKeepsMainColorSolidAndCutsOutContrastingDetail()
    {
        var mask = IconStylizer.CreateMask(Pixels(Green, Green, Green, Black));

        Assert.Equal(255, AlphaAt(mask, 0));
        Assert.Equal(0, AlphaAt(mask, 3));
    }

    [Fact]
    public void TransparentPixelsStayTransparent()
    {
        var mask = IconStylizer.CreateMask(Pixels(Green, Green, Clear));

        Assert.Equal(0, AlphaAt(mask, 2));
    }

    [Fact]
    public void SingleColorIconBecomesSolidShape()
    {
        var mask = IconStylizer.CreateMask(Pixels(Black, Black));

        Assert.Equal(255, AlphaAt(mask, 0));
        Assert.Equal(255, AlphaAt(mask, 1));
    }

    [Fact]
    public void ImageWithManyTonesKeepsLightAndShadow()
    {
        var pixels = Enumerable.Range(0, 16)
            .Select(i => new[] { (byte)(i * 17), (byte)(i * 17), (byte)(i * 17), byte.MaxValue })
            .ToArray();

        var mask = IconStylizer.CreateMask(Pixels(pixels));

        Assert.True(AlphaAt(mask, 15) > AlphaAt(mask, 8));
        Assert.True(AlphaAt(mask, 8) > AlphaAt(mask, 0));
        Assert.True(AlphaAt(mask, 0) > 0);
    }

    [Fact]
    public void MaskIsAlwaysWhiteSoItCanBeTintedByTheTheme()
    {
        var mask = IconStylizer.CreateMask(Pixels(Green, Black, Clear));

        for (var i = 0; i < mask.Length; i += 4)
        {
            Assert.Equal([255, 255, 255], mask[i..(i + 3)]);
        }
    }

    [Fact]
    public void EmptyImageIsAccepted()
    {
        Assert.Empty(IconStylizer.CreateMask([]));
    }
}
