using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class PillPlacementTests
{
    private const double PillWidth = 179; // listening pill, DIP
    private const double PillHeight = 34;

    [Fact]
    public void PrimaryMonitor_BottomSitsEighteenAboveWorkArea_Centred()
    {
        var p = PillPlacement.Compute(new RectPx(0, 0, 1920, 1040), PillWidth, PillHeight, 1.0);

        Assert.Equal(1040 - 18, p.Y + 34);
        Assert.InRange(p.X + 179 / 2.0, 959.5, 960.5);
    }

    [Fact]
    public void ScaledSecondaryMonitor_CentredOnThatMonitor_MarginScaled()
    {
        var area = new RectPx(1920, 0, 2560, 1400);
        var p = PillPlacement.Compute(area, PillWidth, PillHeight, 1.5);

        var width = (int)Math.Round(PillWidth * 1.5, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(PillHeight * 1.5, MidpointRounding.AwayFromZero);
        Assert.Equal(1400 - 27, p.Y + height);
        Assert.InRange(p.X + width / 2.0, 1920 + 1280 - 0.5, 1920 + 1280 + 0.5);
    }

    [Fact]
    public void TaskbarOnTop_StillAtBottomOfWorkArea()
    {
        var p = PillPlacement.Compute(new RectPx(0, 48, 1920, 1032), PillWidth, PillHeight, 1.0);

        Assert.Equal(48 + 1032 - 18, p.Y + 34);
    }

    [Fact]
    public void TaskbarOnLeft_CentredOnWorkAreaNotMonitor()
    {
        var p = PillPlacement.Compute(new RectPx(48, 0, 1872, 1080), PillWidth, PillHeight, 1.0);

        Assert.InRange(p.X + 179 / 2.0, 48 + 936 - 0.5, 48 + 936 + 0.5);
        Assert.Equal(1080 - 18, p.Y + 34);
    }

    [Fact]
    public void MonitorAtNegativeX_StaysOnThatMonitor()
    {
        var p = PillPlacement.Compute(new RectPx(-1920, 0, 1920, 1040), PillWidth, PillHeight, 1.0);

        Assert.InRange(p.X, -1920, -179);
        Assert.InRange(p.X + 179 / 2.0, -960.5, -959.5);
        Assert.Equal(1040 - 18, p.Y + 34);
    }

    [Fact]
    public void MonitorAboveAndLeft_NegativeCoordinates()
    {
        var p = PillPlacement.Compute(new RectPx(-1280, -1024, 1280, 984), PillWidth, PillHeight, 1.25);

        var height = (int)Math.Round(PillHeight * 1.25, MidpointRounding.AwayFromZero);
        Assert.Equal(-1024 + 984 - 23, p.Y + height); // 18 x 1.25 = 22.5 → 23
        Assert.InRange(p.X, -1280, -1);
    }

    [Fact]
    public void PillWiderThanWorkArea_ClampedToLeftEdge()
    {
        var p = PillPlacement.Compute(new RectPx(100, 0, 150, 1040), PillWidth, PillHeight, 1.0);

        Assert.Equal(100, p.X);
        Assert.Equal(1040 - 18, p.Y + 34);
    }

    [Fact]
    public void WorkAreaTooShortForMargin_ClampedInside()
    {
        var p = PillPlacement.Compute(new RectPx(0, 500, 800, 40), PillWidth, PillHeight, 1.0);

        Assert.InRange(p.Y, 500, 500 + 40 - 34);
    }

    [Fact]
    public void PillTallerThanWorkArea_PinnedToTop()
    {
        var p = PillPlacement.Compute(new RectPx(0, 500, 800, 20), PillWidth, PillHeight, 1.0);

        Assert.Equal(500, p.Y);
    }

    [Fact]
    public void NonPositiveScale_TreatedAsOne()
    {
        var p = PillPlacement.Compute(new RectPx(0, 0, 1920, 1040), PillWidth, PillHeight, 0);

        Assert.Equal(1040 - 18, p.Y + 34);
    }

    [Fact]
    public void BottomMargin_IsEighteen() => Assert.Equal(18, PillPlacement.BottomMarginDip);
}
