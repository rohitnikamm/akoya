using System.Windows;
using System.Windows.Media.Animation;

namespace Akoya.App.Controls;

/// <summary>
/// CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing. Leave <see cref="EasingFunctionBase.EasingMode"/> at
/// <see cref="EasingMode.EaseIn"/> so the curve is used as given. Default: Akoya's <c>cubic-bezier(.2,.8,.2,1)</c>.
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    public CubicBezierEase()
    {
        EasingMode = EasingMode.EaseIn;
    }

    public double X1 { get; init; } = 0.2;
    public double Y1 { get; init; } = 0.8;
    public double X2 { get; init; } = 0.2;
    public double Y2 { get; init; } = 1.0;

    protected override double EaseInCore(double normalizedTime)
    {
        var x = Math.Clamp(normalizedTime, 0, 1);
        if (x is 0 or 1) return x;

        // Solve bezierX(t) = x (Newton, falling back to bisection), then return bezierY(t).
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = Bezier(t, X1, X2) - x;
            if (Math.Abs(error) < 1e-7) return Bezier(t, Y1, Y2);
            var slope = Slope(t, X1, X2);
            if (Math.Abs(slope) < 1e-6) break;
            t -= error / slope;
        }

        double lo = 0, hi = 1;
        t = x;
        for (var i = 0; i < 40; i++)
        {
            var value = Bezier(t, X1, X2);
            if (Math.Abs(value - x) < 1e-7) break;
            if (value < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return Bezier(t, Y1, Y2);
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase { X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2 };

    private static double Bezier(double t, double p1, double p2)
    {
        var u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }

    private static double Slope(double t, double p1, double p2)
    {
        var u = 1 - t;
        return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }
}
