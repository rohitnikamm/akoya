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

public class StreamingResamplerTests
{
    private static float[] Noise(int count)
    {
        var rng = new Random(7);
        return Enumerable.Range(0, count).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray();
    }

    [Theory]
    [InlineData(44_100, 1, new[] { 441, 7, 1000, 3 })]
    [InlineData(48_000, 2, new[] { 960, 2, 480, 10 })]
    [InlineData(22_050, 2, new[] { 100, 101, 5 })]
    public void AnyChunking_GivesTheSameOutputAsOneCall(int rate, int channels, int[] chunkFrames)
    {
        var input = Noise(rate * channels / 5);
        var whole = Resampler.ToMono16k(input, channels, rate);

        var streaming = new StreamingResampler(channels, rate);
        var pieces = new List<float>();
        var offset = 0;
        for (var i = 0; offset < input.Length; i++)
        {
            var take = Math.Min(chunkFrames[i % chunkFrames.Length] * channels, input.Length - offset);
            pieces.AddRange(streaming.Process(input.AsSpan(offset, take)));
            offset += take;
        }

        Assert.Equal(whole.Length, pieces.Count);
        for (var i = 0; i < whole.Length; i++) Assert.Equal(whole[i], pieces[i], 4);
    }
}

public class PcmDecoderTests
{
    [Fact]
    public void Sixteen_Bit()
    {
        var bytes = new byte[] { 0x00, 0x40, 0x00, 0xC0, 0xFF, 0x7F };
        Assert.Equal(new[] { 0.5f, -0.5f, 32767 / 32768f }, PcmDecoder.ToFloat(bytes, 16, false));
    }

    [Fact]
    public void TwentyFour_Bit_IsSignExtended()
    {
        // +0.5 = 0x400000, -0.5 = 0xC00000
        var bytes = new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 };
        Assert.Equal(new[] { 0.5f, -0.5f }, PcmDecoder.ToFloat(bytes, 24, false));
    }

    [Fact]
    public void ThirtyTwo_Bit_PcmAndFloat()
    {
        Assert.Equal(new[] { 0.25f }, PcmDecoder.ToFloat(BitConverter.GetBytes(1 << 29), 32, false));
        Assert.Equal(new[] { -0.75f }, PcmDecoder.ToFloat(BitConverter.GetBytes(-0.75f), 32, true));
    }

    [Fact]
    public void PartialTrailingSample_IsIgnored()
    {
        Assert.Single(PcmDecoder.ToFloat(new byte[] { 0, 0, 0 }, 16, false));
    }

    [Fact]
    public void UnsupportedDepth_Throws()
    {
        Assert.False(PcmDecoder.IsSupported(8, false));
        Assert.Throws<NotSupportedException>(() => PcmDecoder.ToFloat(new byte[8], 64, true));
    }
}
