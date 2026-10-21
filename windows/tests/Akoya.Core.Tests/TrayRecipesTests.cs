using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class TrayRecipesTests
{
    [Fact]
    public void StatusCardListening_IsInsetOnly_LitFromBelow()
    {
        Assert.All(RimGlowRecipes.TrayStatusCardListening, s => Assert.True(s.Inset));
        Assert.Null(ShadowRaster.RenderOuter(272, 160, 24, RimGlowRecipes.TrayStatusCardListening, 1));

        var image = ShadowRaster.RenderInset(272, 160, 24, RimGlowRecipes.TrayStatusCardListening, 1)!;
        Assert.True(Alpha(image, 136, 157) > Alpha(image, 136, 3), "bottom edge should be brighter than the top");
        Assert.True(Alpha(image, 136, 80) < Alpha(image, 136, 150), "the centre stays darker than the lower glow");
    }

    [Fact]
    public void SetupCardListening_IsTheMacCardRecipe_InsetOnly_LitFromBelow()
    {
        var recipe = RimGlowRecipes.SetupCardListening;
        Assert.Equal(8, recipe.Count);
        Assert.Equal(new BoxShadow(0, -6, 10, -2, new ShadowColor(200, 228, 255, 0.95), Inset: true), recipe[0]);
        Assert.Equal(new BoxShadow(0, -130, 110, -40, new ShadowColor(20, 70, 220, 0.4), Inset: true), recipe[3]);
        Assert.Equal(new BoxShadow(0, 0, 50, 10, new ShadowColor(14, 50, 170, 0.3), Inset: true), recipe[7]);
        Assert.All(recipe, s => Assert.True(s.Inset));
        Assert.Null(ShadowRaster.RenderOuter(380, 520, 44, recipe, 1));

        var image = ShadowRaster.RenderInset(380, 520, 44, recipe, 1)!;
        Assert.True(Alpha(image, 190, 515) > Alpha(image, 190, 5), "bottom edge should be brighter than the top");
        Assert.True(Alpha(image, 190, 260) < Alpha(image, 190, 480), "the centre stays darker than the lower glow");
    }

    [Fact]
    public void TrayPanel_HasTopHighlight_AndDropShadowBelow()
    {
        var outer = ShadowRaster.RenderOuter(300, 400, 30, RimGlowRecipes.TrayPanel, 1)!;
        // Offset 30 down: more shadow below the panel than above it.
        var below = Alpha(outer, (int)(-outer.Left + 150), (int)(-outer.Top + 400 + 30));
        var above = Alpha(outer, (int)(-outer.Left + 150), (int)(-outer.Top - 30));
        Assert.True(below > above);

        var inset = ShadowRaster.RenderInset(300, 400, 30, RimGlowRecipes.TrayPanel, 1)!;
        Assert.True(Alpha(inset, 150, 0) > 0);
        Assert.Equal(0, Alpha(inset, 150, 200));
    }

    [Fact]
    public void MenuAndThumb_Recipes()
    {
        Assert.Contains(RimGlowRecipes.TrayMenu, s => s.Inset && s.Spread == 1);
        Assert.Contains(RimGlowRecipes.TrayMenu, s => !s.Inset && s.Y == 14 && s.Blur == 34);
        Assert.Equal(new BoxShadow(0, 1, 3, 0, new ShadowColor(0, 0, 0, 0.4)), Assert.Single(RimGlowRecipes.SwitchThumb));
    }

    [Fact]
    public void DotHalos_AreOuterGlowsCentredOnTheDot()
    {
        foreach (var recipe in new[] { RimGlowRecipes.StatusDotBlue, RimGlowRecipes.StatusDotAmber, RimGlowRecipes.StatusDotRed })
        {
            var shadow = Assert.Single(recipe);
            Assert.Equal((0.0, 0.0, 6.0, false), (shadow.X, shadow.Y, shadow.Blur, shadow.Inset));
        }
        Assert.Equal(new BoxShadow(0, 0, 4, 0, new ShadowColor(147, 197, 253, 0.6)), Assert.Single(RimGlowRecipes.LogoDot));

        var halo = ShadowRaster.RenderOuter(5, 5, 2.5, RimGlowRecipes.LogoDot, 1)!;
        Assert.Equal(halo.Width, halo.Height);
        Assert.True(halo.Left < 0 && halo.Top < 0, "the halo spills around the dot");
    }

    private static byte Alpha(ShadowImage image, int x, int y) => image.Pixels[(y * image.PixelWidth + x) * 4 + 3];
}
