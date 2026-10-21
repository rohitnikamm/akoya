namespace Akoya.Core.Audio;

/// <summary>
/// Converts interleaved capture audio to the 16 kHz mono stream the recognizer expects: channels
/// are averaged, then the rate is changed by linear interpolation. No low-pass filter is applied
/// (speech energy sits well below 8 kHz and the model is robust to the small aliasing this allows).
/// </summary>
public static class Resampler
{
    public const int TargetRate = 16_000;

    /// <summary>
    /// Converts one complete buffer. Up to one output sample at the very end can be dropped
    /// (the interpolation needs a following input sample), so the length is the ideal length ±1.
    /// </summary>
    public static float[] ToMono16k(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        return new StreamingResampler(channels, sampleRate).Process(interleaved);
    }
}

/// <summary>
/// The same conversion as <see cref="Resampler.ToMono16k"/> for audio that arrives in chunks:
/// the fractional read position and the last input sample carry over between calls, so chunk
/// boundaries add no glitches. Feeding a signal in any chunking gives the same output as one call.
/// </summary>
public sealed class StreamingResampler
{
    private readonly int _channels;
    private readonly double _step;
    private readonly bool _passThrough;
    private long _nextOutput;
    private long _consumedFrames;
    private float _previous;

    public StreamingResampler(int channels, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        _channels = channels;
        _step = (double)sampleRate / Resampler.TargetRate;
        _passThrough = sampleRate == Resampler.TargetRate;
    }

    public float[] Process(ReadOnlySpan<float> interleaved)
    {
        var frames = interleaved.Length / _channels;
        if (frames == 0) return Array.Empty<float>();

        var mono = new float[frames];
        if (_channels == 1)
        {
            interleaved[..frames].CopyTo(mono);
        }
        else
        {
            for (var i = 0; i < frames; i++)
            {
                float sum = 0;
                var baseIndex = i * _channels;
                for (var c = 0; c < _channels; c++) sum += interleaved[baseIndex + c];
                mono[i] = sum / _channels;
            }
        }

        if (_passThrough) return mono;

        // Positions are in input frames relative to the start of this chunk. Index -1 is the last
        // sample of the previous chunk. An output sample needs the sample at floor(p) and the one
        // after it, so it can be produced only while p < frames - 1; the rest waits for more input.
        var output = new List<float>((int)(frames / _step) + 2);
        while (true)
        {
            var position = _nextOutput * _step - _consumedFrames;
            if (position >= frames - 1) break;

            var index = (int)Math.Floor(position);
            var fraction = (float)(position - index);
            var left = index < 0 ? _previous : mono[index];
            var right = mono[index + 1];
            output.Add(left + (right - left) * fraction);
            _nextOutput++;
        }

        _consumedFrames += frames;
        _previous = mono[frames - 1];
        return output.ToArray();
    }
}
