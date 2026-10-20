namespace Akoya.Core.Audio;

/// <summary>
/// Converts interleaved capture audio to the 16 kHz mono stream the recognizer expects: channels
/// are averaged, then the rate is changed by linear interpolation. No low-pass filter is applied
/// (speech energy sits well below 8 kHz and the model is robust to the small aliasing this allows).
/// </summary>
public static class Resampler
{
    public const int TargetRate = 16_000;

    public static float[] ToMono16k(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);

        var frames = interleaved.Length / channels;
        if (frames == 0) return Array.Empty<float>();

        var mono = new float[frames];
        if (channels == 1)
        {
            interleaved[..frames].CopyTo(mono);
        }
        else
        {
            for (var i = 0; i < frames; i++)
            {
                float sum = 0;
                var baseIndex = i * channels;
                for (var c = 0; c < channels; c++) sum += interleaved[baseIndex + c];
                mono[i] = sum / channels;
            }
        }

        if (sampleRate == TargetRate) return mono;

        var outLength = (int)Math.Round((double)frames * TargetRate / sampleRate, MidpointRounding.AwayFromZero);
        if (outLength == 0) return Array.Empty<float>();

        var output = new float[outLength];
        var step = (double)sampleRate / TargetRate;
        var last = frames - 1;
        for (var i = 0; i < outLength; i++)
        {
            var position = i * step;
            var index = (int)position;
            if (index >= last)
            {
                output[i] = mono[last];
                continue;
            }
            var fraction = (float)(position - index);
            output[i] = mono[index] + (mono[index + 1] - mono[index]) * fraction;
        }
        return output;
    }
}
