namespace Akoya.Core.Ui;

/// <summary>One column of the Solid waveform: a rounded bar <see cref="Height"/> DIP tall, centred on the dot rows.</summary>
public readonly record struct PillBar(double Height, double Opacity);

/// <summary>
/// The pill's waveform. Pixel look: dot opacities, column-major arrays (index = column * Rows + row), row 0 at the top.
/// Solid look: one <see cref="PillBar"/> per column, <see cref="DotSize"/> wide with fully rounded ends.
/// </summary>
public static class PillDots
{
    public const int Rows = 4;

    /// <summary>Dot diameter (and Solid bar width), DIP.</summary>
    public const double DotSize = 3;

    /// <summary>Space between dots, rows and columns alike, DIP.</summary>
    public const double Gap = 2;

    /// <summary>The four dot rows' height, DIP: the tallest Solid bar.</summary>
    public const double MaxBarHeight = Rows * DotSize + (Rows - 1) * Gap;

    /// <summary>The centre two dot rows' height, DIP: the Solid scanner's bars.</summary>
    public const double ScannerBarHeight = 2 * DotSize + Gap;

    /// <summary>A transcribing column at rest (away from the scanner's head and tail).</summary>
    public const double ScannerRest = 0.18;

    private const double SolidOpacity = 0.95;

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
    /// with a fading tail, lighting the centre two rows (1, 2); the outer rows stay at rest.
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
            var opacity = d < 1 ? 1.0 : d < 2 ? 0.7 : d < 3 ? 0.48 : d < 4 ? 0.32 : ScannerRest;
            dots[c * Rows + 0] = ScannerRest;
            dots[c * Rows + 1] = opacity;
            dots[c * Rows + 2] = opacity;
            dots[c * Rows + 3] = ScannerRest;
        }
        return dots;
    }

    /// <summary>
    /// A Solid bar's height for a level step (1..8, clamped): the Mac's Solid column (<c>level × cell</c>, linear in the
    /// level from one cell) on the pill's grid, one dot at step 1 rising linearly to the four rows' height at step 8.
    /// </summary>
    public static double SolidHeight(int step)
    {
        var s = Math.Clamp(step, 1, 8);
        return DotSize + (s - 1) / 7.0 * (MaxBarHeight - DotSize);
    }

    /// <summary>Listening waveform, Solid look: a bright bar per column, its height from the column's step.</summary>
    public static PillBar[] SolidListening(IReadOnlyList<int> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var bars = new PillBar[steps.Count];
        for (var c = 0; c < bars.Length; c++) bars[c] = new PillBar(SolidHeight(steps[c]), SolidOpacity);
        return bars;
    }

    /// <summary>
    /// Transcribing scanner, Solid look: bars the centre two rows tall (the extent the dot scanner lights) at the dot
    /// scanner's opacities, as the Mac's Solid scanner keeps the Pixel scanner's extent.
    /// </summary>
    public static PillBar[] SolidScanner(double elapsedSeconds, int columns = 23)
    {
        var dots = Scanner(elapsedSeconds, columns);
        var bars = new PillBar[columns];
        for (var c = 0; c < columns; c++) bars[c] = new PillBar(ScannerBarHeight, dots[c * Rows + 1]);
        return bars;
    }

    /// <summary>
    /// A scanner column brighter than at rest: the head and its fading tail, the columns that turn amber when Red while
    /// recording is on.
    /// </summary>
    public static bool IsScannerLit(double opacity) => opacity > ScannerRest + 1e-9;
}
