using System.Diagnostics;
using System.IO;
using Akoya.App.Speech;
using Akoya.Core.Settings;
using Xunit;
using Xunit.Abstractions;

namespace Akoya.App.Tests;

/// <summary>A fact that is skipped when AKOYA_MODEL_DIR (the extracted model folder) is not set.</summary>
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute()
    {
        if (string.IsNullOrEmpty(ModelFixture.Dir)) Skip = "AKOYA_MODEL_DIR is not set";
    }
}

public sealed class ModelFixture : IDisposable
{
    public static string? Dir => Environment.GetEnvironmentVariable("AKOYA_MODEL_DIR");

    public SherpaTranscriber? Transcriber { get; }

    public ModelFixture()
    {
        if (!string.IsNullOrEmpty(Dir))
        {
            Transcriber = new SherpaTranscriber(Dir, InferenceProvider.Cpu, Math.Max(1, Environment.ProcessorCount / 2));
            Transcriber.WarmUp();
        }
    }

    public void Dispose() => Transcriber?.Dispose();
}

public sealed class SherpaTranscriberTests : IClassFixture<ModelFixture>
{
    private readonly ModelFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SherpaTranscriberTests(ModelFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private static float[] LoadTestWav() => WavReader.ReadPcm16Mono16k(Path.Combine(ModelFixture.Dir!, "test_wavs", "0.wav"));

    [ModelFact]
    public async Task Transcribe_TestWav_ContainsExpectedWords()
    {
        var samples = LoadTestWav();
        var sw = Stopwatch.StartNew();
        var text = await _fixture.Transcriber!.TranscribeAsync(samples);
        sw.Stop();
        _output.WriteLine($"0.wav ({samples.Length / 16000.0:F1} s of audio) transcribed in {sw.Elapsed.TotalSeconds:F2} s: {text}");

        Assert.Contains("don't wish to see it", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("old portrait", text, StringComparison.OrdinalIgnoreCase);
    }

    [ModelFact]
    public async Task Transcribe_120Seconds_Completes()
    {
        var one = LoadTestWav();
        var samples = new float[120 * 16000];
        for (var i = 0; i < samples.Length; i += one.Length)
        {
            Array.Copy(one, 0, samples, i, Math.Min(one.Length, samples.Length - i));
        }

        var sw = Stopwatch.StartNew();
        var text = await _fixture.Transcriber!.TranscribeAsync(samples);
        sw.Stop();
        _output.WriteLine($"120 s of audio transcribed in {sw.Elapsed.TotalSeconds:F2} s ({text.Length} chars)");

        Assert.True(sw.Elapsed.TotalSeconds < 60, $"took {sw.Elapsed.TotalSeconds:F1} s");
        Assert.NotEmpty(text);
    }

    [ModelFact]
    public async Task Transcribe_Silence_ReturnsEmptyOrShort()
    {
        var text = await _fixture.Transcriber!.TranscribeAsync(new float[3 * 16000]);
        _output.WriteLine($"silence -> \"{text}\"");
        Assert.True(text.Length <= 10, $"got \"{text}\"");
    }

    [ModelFact]
    public async Task Transcribe_ConcurrentCalls_BothComplete()
    {
        var samples = LoadTestWav();
        var results = await Task.WhenAll(
            _fixture.Transcriber!.TranscribeAsync(samples),
            _fixture.Transcriber!.TranscribeAsync(samples));
        Assert.Equal(results[0], results[1]);
    }
}

public sealed class SherpaTranscriberMissingFilesTests
{
    [Fact]
    public void Constructor_MissingModelFiles_ThrowsFileNotFound()
    {
        var empty = Path.Combine(Path.GetTempPath(), "akoya-nomodel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Throws<FileNotFoundException>(() => new SherpaTranscriber(empty, InferenceProvider.Cpu, 1));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }
}

/// <summary>Minimal RIFF reader for the 16 kHz mono PCM16 test wav.</summary>
internal static class WavReader
{
    public static float[] ReadPcm16Mono16k(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));

        var pos = 12;
        var sawFormat = false;
        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            var body = pos + 8;
            if (id == "fmt ")
            {
                Assert.Equal(1, BitConverter.ToInt16(bytes, body));          // PCM
                Assert.Equal(1, BitConverter.ToInt16(bytes, body + 2));      // mono
                Assert.Equal(16000, BitConverter.ToInt32(bytes, body + 4));  // 16 kHz
                Assert.Equal(16, BitConverter.ToInt16(bytes, body + 14));    // 16-bit
                sawFormat = true;
            }
            else if (id == "data")
            {
                Assert.True(sawFormat, "data chunk before fmt chunk");
                var count = Math.Min(size, bytes.Length - body) / 2;
                var samples = new float[count];
                for (var i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(bytes, body + i * 2) / 32768f;
                return samples;
            }
            pos = body + size + (size & 1);
        }

        throw new InvalidDataException("No data chunk in " + path);
    }
}
