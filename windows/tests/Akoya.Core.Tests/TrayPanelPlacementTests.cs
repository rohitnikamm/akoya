using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class TrayPanelPlacementTests
{
    private const double Width = 300;
    private const double Height = 420;
    private static readonly RectPx Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void BottomTaskbar_AboveTheTrayCorner_TwelveInside()
    {
        var work = new RectPx(0, 0, 1920, 1032);
        var p = TrayPanelPlacement.Compute(Monitor, work, Width, Height, 1.0);

        Assert.Equal(TaskbarEdge.Bottom, TrayPanelPlacement.Edge(Monitor, work));
        Assert.Equal(new PointPx(1920 - 12 - 300, 1032 - 12 - 420), p);
    }

    [Fact]
    public void TopTaskbar_BelowItAtTheRight()
    {
        var work = new RectPx(0, 48, 1920, 1032);
        var p = TrayPanelPlacement.Compute(Monitor, work, Width, Height, 1.0);

        Assert.Equal(TaskbarEdge.Top, TrayPanelPlacement.Edge(Monitor, work));
        Assert.Equal(new PointPx(1920 - 12 - 300, 48 + 12), p);
    }

    [Fact]
    public void LeftTaskbar_BesideItAtTheBottom()
    {
        var work = new RectPx(62, 0, 1858, 1080);
        var p = TrayPanelPlacement.Compute(Monitor, work, Width, Height, 1.0);

        Assert.Equal(TaskbarEdge.Left, TrayPanelPlacement.Edge(Monitor, work));
        Assert.Equal(new PointPx(62 + 12, 1080 - 12 - 420), p);
    }

    [Fact]
    public void RightTaskbar_BesideItAtTheBottom()
    {
        var work = new RectPx(0, 0, 1858, 1080);
        var p = TrayPanelPlacement.Compute(Monitor, work, Width, Height, 1.0);

        Assert.Equal(TaskbarEdge.Right, TrayPanelPlacement.Edge(Monitor, work));
        Assert.Equal(new PointPx(1858 - 12 - 300, 1080 - 12 - 420), p);
    }

    [Fact]
    public void AutoHideTaskbar_CountsAsBottom()
    {
        Assert.Equal(TaskbarEdge.Bottom, TrayPanelPlacement.Edge(Monitor, Monitor));
        Assert.Equal(new PointPx(1920 - 312, 1080 - 432), TrayPanelPlacement.Compute(Monitor, Monitor, Width, Height, 1.0));
    }

    [Fact]
    public void ScaledSecondaryMonitor_NegativeCoordinates_SizesAndGapScaled()
    {
        var monitor = new RectPx(-2560, -200, 2560, 1440);
        var work = new RectPx(-2560, -200, 2560, 1368);
        var p = TrayPanelPlacement.Compute(monitor, work, Width, Height, 1.5);

        Assert.Equal(new PointPx(-18 - 450, -200 + 1368 - 18 - 630), p);
    }

    [Fact]
    public void PanelTallerThanWorkArea_PinnedToTheTop()
    {
        var work = new RectPx(0, 0, 1024, 300);
        var p = TrayPanelPlacement.Compute(new RectPx(0, 0, 1024, 348), work, Width, Height, 1.0);

        Assert.Equal(0, p.Y);
        Assert.Equal(1024 - 12 - 300, p.X);
    }

    [Fact]
    public void InvalidScale_CountsAsOne()
    {
        var work = new RectPx(0, 0, 1920, 1032);
        Assert.Equal(TrayPanelPlacement.Compute(Monitor, work, Width, Height, 1.0),
                     TrayPanelPlacement.Compute(Monitor, work, Width, Height, double.NaN));
    }
}
