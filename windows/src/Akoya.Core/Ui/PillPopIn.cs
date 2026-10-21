namespace Akoya.Core.Ui;

/// <summary>
/// One frame of the pill's pop-in: the pill's drawn <see cref="Width"/> (DIP; its corner radius stays half its height,
/// so at the height it is a circle), the scale about its centre, its opacity, and the opacity of its contents (dots,
/// timer, label).
/// </summary>
public readonly record struct PillPopInFrame(double Width, double ScaleX, double ScaleY, double Opacity, double ContentOpacity);

/// <summary>
/// The "jelly" pop-in (docs/design/windows-pill-height-and-options-mockup.html, <c>play()</c>): over 420 ms the pill
/// grows from a small faint circle to a full circle (0–24 %, a slight overshoot), stretches out to 107 % of its width
/// while squashing to 92 % height (24–64 %), springs back to 98.5 % width and 103 % height (64–84 %), and settles
/// (84–100 %). Its contents fade in from 45 % to the end.
/// </summary>
public static class PillPopIn
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(420);

    private const double CircleEnd = 0.24;
    private const double StretchEnd = 0.64;
    private const double SpringEnd = 0.84;
    private const double ContentStart = 0.45;

    private const double StartScale = 0.55;
    private const double StretchWidth = 1.07;
    private const double StretchScaleY = 0.92;
    private const double SpringWidth = 0.985;
    private const double SpringScaleY = 1.03;

    /// <summary>Progress 0..1 after <paramref name="elapsed"/> (clamped).</summary>
    public static double Progress(TimeSpan elapsed) => Math.Clamp(elapsed / Duration, 0, 1);

    /// <summary>
    /// The frame at <paramref name="progress"/> (0..1, clamped; NaN counts as the end) for a pill
    /// <paramref name="finalWidth"/> × <paramref name="height"/> DIP when settled.
    /// </summary>
    public static PillPopInFrame At(double progress, double finalWidth, double height)
    {
        var p = double.IsNaN(progress) ? 1 : Math.Clamp(progress, 0, 1);
        var content = Math.Clamp((p - ContentStart) / (1 - ContentStart), 0, 1);
        var circle = height;

        double width, scaleX = 1, scaleY = 1, opacity = 1;
        if (p < CircleEnd)
        {
            // cubic-bezier(.2,.9,.3,1.2): scale overshoots a touch past 1; opacity is clamped like CSS clamps it.
            var e = CubicBezier.Evaluate(0.2, 0.9, 0.3, 1.2, p / CircleEnd);
            width = circle;
            scaleX = scaleY = StartScale + (1 - StartScale) * e;
            opacity = Math.Clamp(e, 0, 1);
        }
        else if (p < StretchEnd)
        {
            var e = CubicBezier.Evaluate(0.3, 0.8, 0.4, 1, (p - CircleEnd) / (StretchEnd - CircleEnd));
            width = Lerp(circle, StretchWidth * finalWidth, e);
            scaleY = Lerp(1, StretchScaleY, e);
        }
        else if (p < SpringEnd)
        {
            var e = CubicBezier.EaseInOut((p - StretchEnd) / (SpringEnd - StretchEnd));
            width = Lerp(StretchWidth * finalWidth, SpringWidth * finalWidth, e);
            scaleY = Lerp(StretchScaleY, SpringScaleY, e);
        }
        else if (p < 1)
        {
            var e = CubicBezier.EaseInOut((p - SpringEnd) / (1 - SpringEnd));
            width = Lerp(SpringWidth * finalWidth, finalWidth, e);
            scaleY = Lerp(SpringScaleY, 1, e);
        }
        else
        {
            width = finalWidth;
        }

        return new PillPopInFrame(Math.Max(circle, width), scaleX, scaleY, opacity, content);
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;
}
