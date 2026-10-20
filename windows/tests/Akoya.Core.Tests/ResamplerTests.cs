using Akoya.Core.Audio;
using Xunit;

namespace Akoya.Core.Tests;

public class ResamplerTests
{
    private static float[] Sine(double freq, int rate, double seconds, int channels, double amplitude = 0.5)
    {
        var frames = (int)(rate * seconds);
        var data = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var v = (float)(amplitude * Math.Sin(2 * Math.PI * freq * i / rate));
            for (var c = 0; c < channels; c++) data[i * channels + c] = v;
        }
        return data;
    }

    private static double Rms(float[] x)
    {
        double sum = 0;
        foreach (var s in x) sum += s * s;
        return Math.Sqrt(sum / x.Length);
    }

    [Fact]
    public void StereoFortyEightK_SineKeepsLengthAndRms()
    {
        var input = Sine(1000, 48_000, 1.0, 2);
        var output = Resampler.ToMono16k(input, 2, 48_000);

        Assert.InRange(output.Length, 16_000 - 1, 16_000 + 1);
        var expected = 0.5 / Math.Sqrt(2);
        Assert.InRange(Rms(output), expected * 0.98, expected * 1.02);
    }

    [Fact]
    public void FortyFourOneMono_HasCorrectLength()
    {
        var input = Sine(440, 44_100, 1.0, 1);
        var output = Resampler.ToMono16k(input, 1, 44_100);

        Assert.InRange(output.Length, 16_000 - 1, 16_000 + 1);
    }

    [Fact]
    public void ChannelsAreAveraged()
    {
        // Left 1.0, right -1.0 cancels to silence; left 1.0, right 0.0 averages to 0.5.
        var cancel = Resampler.ToMono16k(new float[] { 1, -1, 1, -1, 1, -1, 1, -1 }, 2, 16_000);
        Assert.All(cancel, s => Assert.Equal(0f, s, 6));

        var half = Resampler.ToMono16k(new float[] { 1, 0, 1, 0, 1, 0 }, 2, 16_000);
        Assert.Equal(3, half.Length);
        Assert.All(half, s => Assert.Equal(0.5f, s, 6));
    }

    [Fact]
    public void SixteenK_MonoPassesThroughUnchanged()
    {
        var input = new float[] { 0.1f, -0.2f, 0.3f, -0.4f };
        Assert.Equal(input, Resampler.ToMono16k(input, 1, 16_000));
    }

    [Fact]
    public void EmptyInput_GivesEmptyOutput()
    {
        Assert.Empty(Resampler.ToMono16k(ReadOnlySpan<float>.Empty, 2, 48_000));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resampler.ToMono16k(new float[4], 0, 48_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => Resampler.ToMono16k(new float[4], 1, 0));
    }
}
