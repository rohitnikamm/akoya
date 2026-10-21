using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class PillPlacementTests
{
    private const double PillWidth = 179; // listening pill, DIP
    private const double PillHeight = 34;

    private static readonly RectPx Monitor = new(0, 0, 1920, 1080);

    private static int Bottom(PointPx topLeft, double scale = 1) =>
        topLeft.Y + (int)Math.Round(PillHeight * scale, MidpointRounding.AwayFromZero);

    [Fact]
    public void GapAboveTaskbar_Is52() => Assert.Equal(52, PillPlacement.GapAboveTaskbarDip);

    // ---- Reserved bottom ----

    [Fact]
    public void ShownBottomTaskbar_ReservesTheWorkAreaBottom()
    {
        var taskbar = new TaskbarInfo(new RectPx(0, 1032, 1920, 48), AutoHide: false);

        Assert.Equal(1032, PillPlacement.ReservedBottom(Monitor, new RectPx(0, 0, 1920, 1032), taskbar));
    }

    [Fact]
    public void AutoHiddenBottomTaskbar_ReservesItsThickness_AsIfShown()
    {
        // Hidden, the taskbar slides down so only a sliver shows, and the work area is the whole monitor.
        var hidden = new TaskbarInfo(new RectPx(0, 1078, 1920, 48), AutoHide: true);
        var showing = new TaskbarInfo(new RectPx(0, 1032, 1920, 48), AutoHide: true);

        Assert.Equal(1032, PillPlacement.ReservedBottom(Monitor, Monitor, hidden));
        Assert.Equal(1032, PillPlacement.ReservedBottom(Monitor, Monitor, showing));
    }

    [Fact]
    public void NoTaskbar_ReservesTheWorkAreaBottom() =>
        Assert.Equal(1080, PillPlacement.ReservedBottom(Monitor, Monitor, null));

    [Fact]
    public void TaskbarOnTop_ReservesTheWorkAreaBottom()
    {
        Assert.Equal(1080, PillPlacement.ReservedBottom(Monitor, new RectPx(0, 48, 1920, 1032),
            new TaskbarInfo(new RectPx(0, 0, 1920, 48), AutoHide: false)));
        Assert.Equal(1080, PillPlacement.ReservedBottom(Monitor, Monitor,
            new TaskbarInfo(new RectPx(0, -46, 1920, 48), AutoHide: true)));
    }

    [Fact]
    public void TaskbarOnLeft_ReservesTheWorkAreaBottom()
    {
        Assert.Equal(1080, PillPlacement.ReservedBottom(Monitor, new RectPx(62, 0, 1858, 1080),
            new TaskbarInfo(new RectPx(0, 0, 62, 1080), AutoHide: false)));
        Assert.Equal(1080, PillPlacement.ReservedBottom(Monitor, Monitor,
            new TaskbarInfo(new RectPx(-60, 0, 62, 1080), AutoHide: true)));
    }

    [Fact]
    public void ReservedBottom_NeverBelowTheWorkArea()
    {
        // Another app bar at the bottom takes more room than the hidden taskbar would.
        var hidden = new TaskbarInfo(new RectPx(0, 1078, 1920, 48), AutoHide: true);

        Assert.Equal(1000, PillPlacement.ReservedBottom(Monitor, new RectPx(0, 0, 1920, 1000), hidden));
    }

    [Fact]
    public void NegativeCoordinates_AutoHiddenTaskbar()
    {
        var monitor = new RectPx(-1920, -200, 1920, 1080); // bottom at 880
        var hidden = new TaskbarInfo(new RectPx(-1920, 878, 1920, 48), AutoHide: true);

        Assert.Equal(832, PillPlacement.ReservedBottom(monitor, monitor, hidden));
    }

    // ---- Position ----

    [Fact]
    public void DefaultTaskbar_PillBottomIs100AboveTheScreenBottom_ShownOrHidden()
    {
        var shownWork = new RectPx(0, 0, 1920, 1032);
        var shown = PillPlacement.Compute(shownWork,
            PillPlacement.ReservedBottom(Monitor, shownWork, new TaskbarInfo(new RectPx(0, 1032, 1920, 48), false)),
            PillWidth, PillHeight, 1.0);
        var hidden = PillPlacement.Compute(Monitor,
            PillPlacement.ReservedBottom(Monitor, Monitor, new TaskbarInfo(new RectPx(0, 1078, 1920, 48), true)),
            PillWidth, PillHeight, 1.0);

        Assert.Equal(1080 - 100, Bottom(shown));
        Assert.Equal(shown, hidden);
        Assert.InRange(shown.X + PillWidth / 2, 959.5, 960.5);
    }

    [Fact]
    public void NoTaskbar_PillBottom52AboveTheWorkArea()
    {
        var p = PillPlacement.Compute(Monitor, 1080, PillWidth, PillHeight, 1.0);

        Assert.Equal(1080 - 52, Bottom(p));
    }

    [Fact]
    public void ScaledSecondaryMonitor_GapAndTaskbarInItsPixels()
    {
        // 150 %: the default 48 DIP taskbar is 72 px, the gap 78 px.
        var monitor = new RectPx(1920, 0, 2560, 1440);
        var work = new RectPx(1920, 0, 2560, 1368);
        var reserved = PillPlacement.ReservedBottom(monitor, work, new TaskbarInfo(new RectPx(1920, 1368, 2560, 72), false));
        var p = PillPlacement.Compute(work, reserved, PillWidth, PillHeight, 1.5);

        var width = (int)Math.Round(PillWidth * 1.5, MidpointRounding.AwayFromZero);
        Assert.Equal(1440 - 150, Bottom(p, 1.5)); // 100 DIP
        Assert.InRange(p.X + width / 2.0, 1920 + 1280 - 0.5, 1920 + 1280 + 0.5);
    }

    [Fact]
    public void MixedDpi_125Percent_GapRounded()
    {
        var p = PillPlacement.Compute(new RectPx(-1280, -1024, 1280, 1024), -60, PillWidth, PillHeight, 1.25);

        Assert.Equal(-60 - 65, Bottom(p, 1.25)); // 52 x 1.25 = 65
        Assert.InRange(p.X, -1280, -1);
    }

    [Fact]
    public void TaskbarOnLeft_CentredOnTheWorkAreaNotTheMonitor()
    {
        var work = new RectPx(48, 0, 1872, 1080);
        var p = PillPlacement.Compute(work, PillPlacement.ReservedBottom(Monitor, work,
            new TaskbarInfo(new RectPx(0, 0, 48, 1080), false)), PillWidth, PillHeight, 1.0);

        Assert.InRange(p.X + PillWidth / 2, 48 + 936 - 0.5, 48 + 936 + 0.5);
        Assert.Equal(1080 - 52, Bottom(p));
    }

    [Fact]
    public void MonitorAtNegativeX_StaysOnThatMonitor()
    {
        var p = PillPlacement.Compute(new RectPx(-1920, 0, 1920, 1032), 1032, PillWidth, PillHeight, 1.0);

        Assert.InRange(p.X, -1920, -179);
        Assert.InRange(p.X + PillWidth / 2, -960.5, -959.5);
        Assert.Equal(1032 - 52, Bottom(p));
    }

    [Fact]
    public void PillWiderThanWorkArea_ClampedToLeftEdge()
    {
        var p = PillPlacement.Compute(new RectPx(100, 0, 150, 1040), 1040, PillWidth, PillHeight, 1.0);

        Assert.Equal(100, p.X);
    }

    [Fact]
    public void TinyWorkArea_ClampedInside()
    {
        var p = PillPlacement.Compute(new RectPx(0, 500, 800, 60), 560, PillWidth, PillHeight, 1.0);

        Assert.Equal(500, p.Y); // 560 - 52 - 34 = 474 would be above the work area
    }

    [Fact]
    public void PillTallerThanWorkArea_PinnedToTop()
    {
        var p = PillPlacement.Compute(new RectPx(0, 500, 800, 20), 520, PillWidth, PillHeight, 1.0);

        Assert.Equal(500, p.Y);
    }

    [Fact]
    public void ReservedBottomBelowTheWorkArea_ClampedInside()
    {
        var p = PillPlacement.Compute(new RectPx(0, 0, 1920, 1032), 5000, PillWidth, PillHeight, 1.0);

        Assert.Equal(1032 - 34, p.Y);
    }

    [Fact]
    public void NonPositiveScale_TreatedAsOne()
    {
        var p = PillPlacement.Compute(new RectPx(0, 0, 1920, 1032), 1032, PillWidth, PillHeight, 0);

        Assert.Equal(1032 - 52, Bottom(p));
    }
}
