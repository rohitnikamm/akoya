namespace Akoya.Core.Audio;

/// <summary>
/// Groups a stream of mono samples into fixed windows and reports one RMS value per
/// completed window. Leftover samples carry over to the next <see cref="Append"/>.
/// </summary>
public sealed class RmsWindower
{
    private readonly int _windowSize;
    private readonly List<float> _pending = new();

    public RmsWindower(double sampleRate = 16_000, double windowSeconds = 0.04)
    {
        _windowSize = Math.Max(1, (int)Math.Round(sampleRate * windowSeconds, MidpointRounding.AwayFromZero));
    }

    public IReadOnlyList<float> Append(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples) _pending.Add(s);
        var results = new List<float>();
        var start = 0;
        while (_pending.Count - start >= _windowSize)
        {
            float sum = 0;
            for (var i = start; i < start + _windowSize; i++)
                sum += _pending[i] * _pending[i];
            results.Add(MathF.Sqrt(sum / _windowSize));
            start += _windowSize;
        }
        if (start > 0) _pending.RemoveRange(0, start);
        return results;
    }
}
