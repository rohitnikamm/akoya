using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class ShadowRasterTests
{
    private static readonly ShadowColor White = new(255, 255, 255, 1);

    private static (byte B, byte G, byte R, byte A) Pixel(ShadowImage image, int x, int y)
    {
        var i = (y * image.PixelWidth + x) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]);
    }

    [Fact]
    public void Inset_CoversExactlyTheBox()
    {
        var image = ShadowRaster.RenderInset(100, 34, 17, [new BoxShadow(0, 0, 0, 1, White, Inset: true)], 2)!;

        Assert.Equal((200, 68), (image.PixelWidth, image.PixelHeight));
        Assert.Equal((0.0, 0.0, 100.0, 34.0), (image.Left, image.Top, image.Width, image.Height));
    }

    [Fact]
    public void Inset_OnePixelRim_EdgeLitCentreEmpty()
    {
        var image = ShadowRaster.RenderInset(100, 34, 17, [new BoxShadow(0, 0, 0, 1, White, Inset: true)], 1)!;

        Assert.Equal(255, Pixel(image, 50, 0).A);   // top edge, inside the 1 px rim
        Assert.Equal(255, Pixel(image, 50, 33).A);  // bottom edge
        Assert.Equal(0, Pixel(image, 50, 17).A);    // centre
        Assert.Equal(0, Pixel(image, 0, 0).A);      // outside the rounded corner
    }

    [Fact]
    public void Inset_BottomGlow_BrighterAtBottomThanTop()
    {
        var image = ShadowRaster.RenderInset(179, 34, 17, RimGlowRecipes.PillBlue, 2)!;

        Assert.True(Pixel(image, 179, 64).A > Pixel(image, 179, 4).A + 60);
    }

    [Fact]
    public void Inset_PremultipliedColour()
    {
        var half = new ShadowColor(200, 100, 50, 0.5);
        var image = ShadowRaster.RenderInset(40, 40, 0, [new BoxShadow(0, 0, 0, 5, half, Inset: true)], 1)!;

        Assert.Equal(((byte)25, (byte)50, (byte)100, (byte)128), Pixel(image, 1, 20));
    }

    [Fact]
    public void Outer_NotDrawnUnderTheBox_AndOffsetDownward()
    {
        var image = ShadowRaster.RenderOuter(100, 34, 17, [new BoxShadow(0, 12, 40, 0, White)], 1)!;

        Assert.True(image.Left < 0 && image.Top < 0);
        var ox = (int)Math.Round(-image.Left);
        var oy = (int)Math.Round(-image.Top);
        Assert.Equal(0, Pixel(image, ox + 50, oy + 17).A);                          // under the box
        Assert.True(Pixel(image, ox + 50, oy + 40).A > Pixel(image, ox + 50, oy - 6).A); // more below than above
    }

    [Fact]
    public void NoMatchingLayers_ReturnsNull()
    {
        Assert.Null(ShadowRaster.RenderOuter(10, 10, 5, [new BoxShadow(0, 0, 4, 0, White, Inset: true)], 1));
        Assert.Null(ShadowRaster.RenderInset(10, 10, 5, [new BoxShadow(0, 0, 4, 0, White)], 1));
        Assert.Null(ShadowRaster.RenderInset(0, 10, 5, [new BoxShadow(0, 0, 4, 0, White, Inset: true)], 1));
    }

    [Fact]
    public void Pill_UsesExactRecipesForAccentAndRed()
    {
        Assert.Same(RimGlowRecipes.PillBlue, RimGlowRecipes.Pill(RimGlowRecipes.Accent));
        Assert.Same(RimGlowRecipes.PillRed, RimGlowRecipes.Pill(RimGlowRecipes.Red));
        Assert.Equal(5, RimGlowRecipes.Pill(new ShadowColor(0x4A, 0xDE, 0x80, 1)).Count);
    }
}
