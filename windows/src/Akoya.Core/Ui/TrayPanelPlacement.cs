namespace Akoya.Core.Ui;

/// <summary>Which screen edge the taskbar is docked to.</summary>
public enum TaskbarEdge { Bottom, Top, Left, Right }

/// <summary>
/// Where the tray panel goes: in the tray corner of the taskbar's monitor, <see cref="GapDip"/> (DPI-scaled) inside
/// its work area, like the Windows flyouts. The taskbar edge is read from where the work area is shorter than the
/// monitor; with an auto-hiding taskbar (no difference) it counts as the bottom. The tray corner is the right end of
/// a horizontal taskbar and the bottom end of a vertical one.
/// </summary>
public static class TrayPanelPlacement
{
    public const double GapDip = 12;

    public static TaskbarEdge Edge(RectPx monitor, RectPx workArea)
    {
        if (workArea.Y + workArea.Height < monitor.Y + monitor.Height) return TaskbarEdge.Bottom;
        if (workArea.Y > monitor.Y) return TaskbarEdge.Top;
        if (workArea.X > monitor.X) return TaskbarEdge.Left;
        if (workArea.X + workArea.Width < monitor.X + monitor.Width) return TaskbarEdge.Right;
        return TaskbarEdge.Bottom;
    }

    /// <summary>Top-left of the panel in physical pixels.</summary>
    /// <param name="monitor">The taskbar's monitor, physical pixels.</param>
    /// <param name="workArea">That monitor's work area, physical pixels.</param>
    /// <param name="panelWidthDip">Panel width in DIP.</param>
    /// <param name="panelHeightDip">Panel height in DIP.</param>
    /// <param name="dpiScale">The monitor's scale (1.0 = 96 DPI); non-positive values count as 1.</param>
    public static PointPx Compute(RectPx monitor, RectPx workArea, double panelWidthDip, double panelHeightDip, double dpiScale)
    {
        var scale = dpiScale > 0 && double.IsFinite(dpiScale) ? dpiScale : 1.0;
        var width = ToPx(panelWidthDip, scale);
        var height = ToPx(panelHeightDip, scale);
        var gap = ToPx(GapDip, scale);

        var right = workArea.X + workArea.Width - gap - width;
        var bottom = workArea.Y + workArea.Height - gap - height;
        var (x, y) = Edge(monitor, workArea) switch
        {
            TaskbarEdge.Top => (right, workArea.Y + gap),
            TaskbarEdge.Left => (workArea.X + gap, bottom),
            _ => (right, bottom),
        };

        return new PointPx(Clamp(x, workArea.X, workArea.X + workArea.Width - width),
                           Clamp(y, workArea.Y, workArea.Y + workArea.Height - height));
    }

    private static int ToPx(double dip, double scale) =>
        (int)Math.Round(Math.Max(0, dip) * scale, MidpointRounding.AwayFromZero);

    /// <summary>Clamps into [min, max]; when the panel is bigger than the area (max &lt; min) it is pinned to min.</summary>
    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Min(Math.Max(value, min), max);
}
