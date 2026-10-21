namespace Akoya.Core.Ui;

/// <summary>A rectangle in physical (device) pixels, e.g. a monitor's work area.</summary>
public readonly record struct RectPx(int X, int Y, int Width, int Height)
{
    public int Bottom => Y + Height;
}

/// <summary>A point in physical (device) pixels.</summary>
public readonly record struct PointPx(int X, int Y);

/// <summary>
/// A monitor's taskbar: its window rectangle in physical pixels (the same size shown or auto-hidden; hidden, it is
/// moved mostly off the screen) and whether it auto-hides.
/// </summary>
public readonly record struct TaskbarInfo(RectPx Bounds, bool AutoHide);

/// <summary>
/// Where the pill goes: centred horizontally on the work area of the target monitor, its bottom edge
/// <see cref="GapAboveTaskbarDip"/> (DPI-scaled) above the top of the taskbar as if the taskbar were showing, so it sits
/// at the same height whether the taskbar is shown or auto-hidden (with the default 48 DIP taskbar, 100 DIP above the
/// screen's bottom), and always fully inside the work area.
/// </summary>
public static class PillPlacement
{
    public const double GapAboveTaskbarDip = 52;

    /// <summary>
    /// The y (physical pixels) the gap is measured from: the top of a bottom taskbar, shown (the work area's bottom)
    /// or auto-hidden (the monitor's bottom minus the taskbar's thickness); with the taskbar on another edge, or none,
    /// the work area's bottom. Never below the work area's bottom.
    /// </summary>
    public static int ReservedBottom(RectPx monitor, RectPx workArea, TaskbarInfo? taskbar)
    {
        if (taskbar is not { } bar || !IsAtBottom(bar.Bounds, monitor)) return workArea.Bottom;
        var reserved = bar.AutoHide ? monitor.Bottom - bar.Bounds.Height : workArea.Bottom;
        return Math.Min(reserved, workArea.Bottom);
    }

    /// <summary>Top-left of the pill in physical pixels.</summary>
    /// <param name="workArea">The target monitor's work area, physical pixels (may have negative coordinates).</param>
    /// <param name="reservedBottom">From <see cref="ReservedBottom"/>, physical pixels.</param>
    /// <param name="pillWidthDip">Pill width in DIP.</param>
    /// <param name="pillHeightDip">Pill height in DIP.</param>
    /// <param name="dpiScale">The monitor's scale (1.0 = 96 DPI, 1.5 = 150 %); non-positive values count as 1.</param>
    public static PointPx Compute(RectPx workArea, int reservedBottom, double pillWidthDip, double pillHeightDip, double dpiScale)
    {
        var scale = dpiScale > 0 && double.IsFinite(dpiScale) ? dpiScale : 1.0;
        var width = ToPx(pillWidthDip, scale);
        var height = ToPx(pillHeightDip, scale);
        var gap = ToPx(GapAboveTaskbarDip, scale);

        var x = workArea.X + (int)Math.Floor((workArea.Width - width) / 2.0);
        var y = reservedBottom - gap - height;

        return new PointPx(Clamp(x, workArea.X, workArea.X + workArea.Width - width),
                           Clamp(y, workArea.Y, workArea.Bottom - height));
    }

    /// <summary>A horizontal bar whose middle is in the monitor's lower half.</summary>
    private static bool IsAtBottom(RectPx bar, RectPx monitor) =>
        bar.Width > bar.Height && bar.Y + bar.Height / 2.0 > monitor.Y + monitor.Height / 2.0;

    private static int ToPx(double dip, double scale) =>
        (int)Math.Round(Math.Max(0, dip) * scale, MidpointRounding.AwayFromZero);

    /// <summary>Clamps into [min, max]; when the item is bigger than the area (max &lt; min) it is pinned to min.</summary>
    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Min(Math.Max(value, min), max);
}
