namespace Akoya.Core.Ui;

/// <summary>
/// Dot opacities for the pill. Arrays are column-major (index = column * Rows + row), row 0 at the top.
/// </summary>
public static class PillDots
{
    public const int Rows = 4;

    private const double Faint = 0.12;
    private const double Dim = 0.35;
    private const double Lit = 0.95;

    /// <summary>
    /// Listening waveform. Step 1 dims the middle rows (1, 2); from step 2 the middle rows are fully lit
    /// and the outer rows (0, 3) rise from step 4 to 7 so step 8 lights all four.
    /// </summary>
    public static double[] Listening(IReadOnlyList<int> steps)
    {
        var dots = new double[steps.Count * Rows];
        for (var c = 0; c < steps.Count; c++)
        {
            var step = Math.Min(8, Math.Max(1, steps[c]));
            double middle, outer;
            if (step <= 1)
            {
                middle = Dim;
                outer = Faint;
            }
            else
            {
                middle = Lit;
                outer = Faint + (Lit - Faint) * Math.Clamp((step - 4) / 3.0, 0, 1);
            }
            dots[c * Rows + 0] = outer;
            dots[c * Rows + 1] = middle;
            dots[c * Rows + 2] = middle;
            dots[c * Rows + 3] = outer;
        }
        return dots;
    }

    /// <summary>
    /// Transcribing scanner: a bright head eases back and forth across the columns (period 1.4 s)
    /// with a fading tail, applied to every row.
    /// </summary>
    public static double[] Scanner(double elapsedSeconds, int columns = 23)
    {
        const double period = 1.4;
        var p = elapsedSeconds % period / period;
        var leg = p < 0.5 ? p * 2 : 2 - p * 2;
        var head = (1 - Math.Cos(Math.PI * leg)) / 2 * (columns - 1);
        var dots = new double[columns * Rows];
        for (var c = 0; c < columns; c++)
        {
            var d = Math.Abs(c - head);
            var opacity = d < 1 ? 1.0 : d < 2 ? 0.7 : d < 3 ? 0.48 : d < 4 ? 0.32 : 0.18;
            for (var row = 0; row < Rows; row++) dots[c * Rows + row] = opacity;
        }
        return dots;
    }
}
