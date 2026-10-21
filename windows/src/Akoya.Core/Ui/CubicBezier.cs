namespace Akoya.Core.Ui;

/// <summary>CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing functions, as the mockups use them.</summary>
public static class CubicBezier
{
    /// <summary>CSS <c>ease-in-out</c>.</summary>
    public static double EaseInOut(double t) => Evaluate(0.42, 0, 0.58, 1, t);

    /// <summary>
    /// The curve through (0,0), (x1,y1), (x2,y2), (1,1) at time <paramref name="t"/> (clamped to 0..1; NaN counts as 0):
    /// solves x(s) = t for the curve parameter (Newton, falling back to bisection), then returns y(s). x1 and x2 must be
    /// in 0..1; y1 and y2 may lie outside it (overshoot).
    /// </summary>
    public static double Evaluate(double x1, double y1, double x2, double y2, double t)
    {
        var x = double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1);
        if (x is 0 or 1) return x;

        var s = x;
        for (var i = 0; i < 8; i++)
        {
            var error = Bezier(s, x1, x2) - x;
            if (Math.Abs(error) < 1e-9) return Bezier(s, y1, y2);
            var slope = Slope(s, x1, x2);
            if (Math.Abs(slope) < 1e-6) break;
            s -= error / slope;
        }

        double lo = 0, hi = 1;
        s = x;
        for (var i = 0; i < 60; i++)
        {
            var value = Bezier(s, x1, x2);
            if (Math.Abs(value - x) < 1e-9) break;
            if (value < x) lo = s; else hi = s;
            s = (lo + hi) / 2;
        }
        return Bezier(s, y1, y2);
    }

    private static double Bezier(double s, double p1, double p2)
    {
        var u = 1 - s;
        return 3 * u * u * s * p1 + 3 * u * s * s * p2 + s * s * s;
    }

    private static double Slope(double s, double p1, double p2)
    {
        var u = 1 - s;
        return 3 * u * u * p1 + 6 * u * s * (p2 - p1) + 3 * s * s * (1 - p2);
    }
}
