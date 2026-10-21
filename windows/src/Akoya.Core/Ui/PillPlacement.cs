namespace Akoya.Core.Ui;

/// <summary>A rectangle in physical (device) pixels, e.g. a monitor's work area.</summary>
public readonly record struct RectPx(int X, int Y, int Width, int Height);

/// <summary>A point in physical (device) pixels.</summary>
public readonly record struct PointPx(int X, int Y);

/// <summary>
/// Where the pill goes: centred horizontally on the work area of the target monitor, its bottom edge
/// <see cref="BottomMarginDip"/> (DPI-scaled) above the work area's bottom, and always fully inside it.
/// </summary>
public static class PillPlacement
{
    public const double BottomMarginDip = 18;

    /// <summary>Top-left of the pill in physical pixels.</summary>
    /// <param name="workArea">The target monitor's work area, physical pixels (may have negative coordinates).</param>
    /// <param name="pillWidthDip">Pill width in DIP.</param>
    /// <param name="pillHeightDip">Pill height in DIP.</param>
    /// <param name="dpiScale">The monitor's scale (1.0 = 96 DPI, 1.5 = 150 %); non-positive values count as 1.</param>
    public static PointPx Compute(RectPx workArea, double pillWidthDip, double pillHeightDip, double dpiScale)
    {
        var scale = dpiScale > 0 && double.IsFinite(dpiScale) ? dpiScale : 1.0;
        var width = ToPx(pillWidthDip, scale);
        var height = ToPx(pillHeightDip, scale);
        var margin = ToPx(BottomMarginDip, scale);

        var x = workArea.X + (int)Math.Floor((workArea.Width - width) / 2.0);
        var y = workArea.Y + workArea.Height - margin - height;

        return new PointPx(Clamp(x, workArea.X, workArea.X + workArea.Width - width),
                           Clamp(y, workArea.Y, workArea.Y + workArea.Height - height));
    }

    private static int ToPx(double dip, double scale) =>
        (int)Math.Round(Math.Max(0, dip) * scale, MidpointRounding.AwayFromZero);

    /// <summary>Clamps into [min, max]; when the item is bigger than the area (max &lt; min) it is pinned to min.</summary>
    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Min(Math.Max(value, min), max);
}
