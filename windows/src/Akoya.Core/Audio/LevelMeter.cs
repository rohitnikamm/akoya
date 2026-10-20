namespace Akoya.Core.Audio;

/// <summary>
/// Rolling bar-height history for the pill waveform. Each column is a step in 1...8;
/// the newest value is on the right.
/// </summary>
public sealed class LevelMeter
{
    private readonly int[] _columns;

    public LevelMeter(int columnCount = 23)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columnCount, 1);
        _columns = new int[columnCount];
        Reset();
    }

    public IReadOnlyList<int> Columns => _columns;

    /// <summary>
    /// Appends a step for <paramref name="rms"/> as the newest (rightmost) column. The new step
    /// never falls more than two below the previous newest column, giving a smooth release.
    /// </summary>
    public void Push(float rms)
    {
        var raw = Step(rms);
        var previous = _columns[^1];
        var step = Math.Max(raw, previous - 2);
        Array.Copy(_columns, 1, _columns, 0, _columns.Length - 1);
        _columns[^1] = step;
    }

    public void Reset() => Array.Fill(_columns, 1);

    /// <summary>Maps dBFS linearly from -50 (step 1) to -10 (step 8), clamped to 1...8.</summary>
    public static int Step(float rms)
    {
        if (!(rms > 0) || !float.IsFinite(rms))
            return float.IsPositiveInfinity(rms) ? 8 : 1;
        var db = 20 * MathF.Log10(rms);
        var raw = 1 + (int)MathF.Floor((db + 50) / 40 * 7);
        return Math.Min(8, Math.Max(1, raw));
    }
}
