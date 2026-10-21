namespace Akoya.Core.Audio;

/// <summary>
/// Append-only sample storage in fixed-size chunks, so a 300 s recording (4.8 million floats) never
/// needs one huge reallocating array. Not thread-safe.
/// </summary>
public sealed class SampleBuffer
{
    public const int ChunkSize = 16_384;

    private readonly List<float[]> _full = new();
    private float[] _current = new float[ChunkSize];
    private int _fill;

    public long Count => (long)_full.Count * ChunkSize + _fill;

    public void Append(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            if (_fill == ChunkSize)
            {
                _full.Add(_current);
                _current = new float[ChunkSize];
                _fill = 0;
            }

            var take = Math.Min(samples.Length, ChunkSize - _fill);
            samples[..take].CopyTo(_current.AsSpan(_fill));
            _fill += take;
            samples = samples[take..];
        }
    }

    public float[] ToArray()
    {
        var result = new float[Count];
        var offset = 0;
        foreach (var chunk in _full)
        {
            chunk.CopyTo(result, offset);
            offset += ChunkSize;
        }
        _current.AsSpan(0, _fill).CopyTo(result.AsSpan(offset));
        return result;
    }
}
