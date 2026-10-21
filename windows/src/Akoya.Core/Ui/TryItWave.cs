namespace Akoya.Core.Ui;

/// <summary>
/// Setup's Try it waveform: one column of dots per level-meter column, each column a centred run of
/// 1 to <see cref="MaxDots"/> dots (always odd, so it stays centred), as on the Mac's TryItWaveform.
/// </summary>
public static class TryItWave
{
    /// <summary>The tallest column (and the grid's row count).</summary>
    public const int MaxDots = 9;

    /// <summary>Dot opacity while recording; at rest the waveform dims to <see cref="RestOpacity"/>.</summary>
    public const double LiveOpacity = 1.0;

    public const double RestOpacity = 0.45;

    /// <summary>Dots for a level step (1 to 8): 1, 3, 5, 7 or 9.</summary>
    public static int DotCount(int step)
    {
        var level = (Math.Clamp(step, 1, 8) - 1) / 7.0;
        return Math.Max(1, (int)Math.Round(level * MaxDots, MidpointRounding.AwayFromZero)) | 1;
    }

    /// <summary>
    /// Column-major opacities (index = column * <see cref="MaxDots"/> + row) for a grid of <paramref name="steps"/>.Count
    /// columns by <see cref="MaxDots"/> rows: lit dots at <see cref="LiveOpacity"/> or <see cref="RestOpacity"/>, the rest 0.
    /// </summary>
    public static double[] Opacities(IReadOnlyList<int> steps, bool live)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var result = new double[steps.Count * MaxDots];
        var lit = live ? LiveOpacity : RestOpacity;
        for (var column = 0; column < steps.Count; column++)
        {
            var count = DotCount(steps[column]);
            var first = (MaxDots - count) / 2;
            for (var row = first; row < first + count; row++) result[column * MaxDots + row] = lit;
        }
        return result;
    }
}
