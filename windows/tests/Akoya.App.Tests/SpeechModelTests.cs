using System.IO;
using System.Net.Http;
using Akoya.App.Speech;
using Akoya.Core.Model;
using Akoya.Core.Settings;
using Xunit;

namespace Akoya.App.Tests;

public sealed class SpeechModelTests : IDisposable
{
    private const string Folder = "fake-model";
    private static readonly ModelFiles Files = new("enc.onnx", "dec.onnx", "join.onnx", "tokens.txt");

    private static readonly ModelSpec Spec = new(
        "https://example.invalid/model.tar.bz2", 1, new string('0', 64), Folder, Files,
        Files.All.ToDictionary(n => n, n => (long)("content of " + n).Length));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "akoya-speechmodel-" + Guid.NewGuid().ToString("N"));
    private readonly string _builtIn = Path.Combine(Path.GetTempPath(), "akoya-builtin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (var dir in new[] { _root, _builtIn })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static void WriteFakeModel(string root)
    {
        var dir = Path.Combine(root, Folder);
        Directory.CreateDirectory(dir);
        foreach (var name in Files.All) File.WriteAllText(Path.Combine(dir, name), "content of " + name);
    }

    private ModelStore Store() => new(_root, new HttpClient(), Spec, builtInRoot: _builtIn);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(8, 4)]
    [InlineData(16, 8)]
    [InlineData(32, 8)]  // Rohit's PC: 8 threads transcribe a short dictation faster than 16 (GPU spike, 2026-10-21)
    [InlineData(64, 8)]
    public void Threads_HalfTheLogicalCores_AtMostEight(int processors, int expected) =>
        Assert.Equal(expected, SpeechModel.ThreadsFor(processors));

    [Fact]
    public void LoadWithBuiltInRemovesTheDownloadedCopy()
    {
        WriteFakeModel(_builtIn);
        WriteFakeModel(_root);
        File.WriteAllText(Path.Combine(_root, Folder + ".tar.bz2.part"), "junk");
        string? loadedFrom = null;
        var model = new SpeechModel(Store(), (dir, _) =>
        {
            loadedFrom = dir;
            return new FakeTranscriber("hi");
        });

        var transcriber = model.Load(InferenceProvider.Cpu);

        Assert.IsType<FakeTranscriber>(transcriber);
        Assert.Equal(Path.Combine(_builtIn, Folder), loadedFrom);
        Assert.False(Directory.Exists(_root));
        Assert.All(Files.All, name => Assert.True(File.Exists(Path.Combine(_builtIn, Folder, name))));
    }

    [Fact]
    public void BuiltInLoadFailure_Once_NextLoadTriesTheBuiltInAgain()
    {
        WriteFakeModel(_builtIn);
        var builtInDir = Path.Combine(_builtIn, Folder);
        var loads = new List<string>();
        var model = new SpeechModel(Store(), (dir, _) =>
        {
            loads.Add(dir);
            return loads.Count == 1 ? throw new InvalidOperationException("out of memory") : new FakeTranscriber("hi");
        });

        Assert.Throws<InvalidOperationException>(() => model.Load(InferenceProvider.Cpu));
        Assert.True(model.IsInstalled); // so AppHost's Retry doesn't download (blocked on some company PCs)
        Assert.IsType<FakeTranscriber>(model.Load(InferenceProvider.Cpu));

        Assert.Equal(new[] { builtInDir, builtInDir }, loads);
    }

    [Fact]
    public void BuiltInLoadFailsTwice_DiscardUsesTheDownloadedCopy_AndKeepsBoth()
    {
        WriteFakeModel(_builtIn);
        WriteFakeModel(_root);
        var builtInDir = Path.Combine(_builtIn, Folder);
        var loads = new List<string>();
        var model = new SpeechModel(Store(), (dir, _) =>
        {
            loads.Add(dir);
            return dir == builtInDir ? throw new InvalidOperationException("corrupt") : new FakeTranscriber("hi");
        });

        Assert.Throws<InvalidOperationException>(() => model.Load(InferenceProvider.Cpu));
        Assert.Throws<InvalidOperationException>(() => model.Load(InferenceProvider.Cpu));
        model.Discard(); // what AppHost does after the second failure
        Assert.True(model.IsInstalled);
        model.Load(InferenceProvider.Cpu);

        Assert.Equal(new[] { builtInDir, builtInDir, Path.Combine(_root, Folder) }, loads);
        Assert.True(Directory.Exists(Path.Combine(_root, Folder)));
        Assert.All(Files.All, name => Assert.True(File.Exists(Path.Combine(builtInDir, name))));
    }

    [Fact]
    public void BuiltInLoadFailsTwice_WithoutADownloadedCopy_DiscardMakesItNotInstalled()
    {
        WriteFakeModel(_builtIn);
        var model = new SpeechModel(Store(), (_, _) => throw new InvalidOperationException("corrupt"));

        Assert.Throws<InvalidOperationException>(() => model.Load(InferenceProvider.Cpu));
        Assert.True(model.IsInstalled);
        Assert.Throws<InvalidOperationException>(() => model.Load(InferenceProvider.Cpu));
        model.Discard();

        Assert.False(model.IsInstalled); // so AppHost's next EnsureAsync downloads it
        Assert.All(Files.All, name => Assert.True(File.Exists(Path.Combine(_builtIn, Folder, name))));
    }

    [Fact]
    public void LoadFromTheDownloadedCopy_KeepsIt()
    {
        WriteFakeModel(_root);
        var model = new SpeechModel(Store(), (_, _) => new FakeTranscriber("hi"));

        model.Load(InferenceProvider.Cpu);

        Assert.True(Directory.Exists(Path.Combine(_root, Folder)));
    }

    /// <summary>Opt-in: runs when AKOYA_BUILT_IN_MODELS (the staged <c>models</c> folder) is set.</summary>
    [Fact]
    public async Task BuiltInModelLoadsAndTranscribes()
    {
        var builtIn = Environment.GetEnvironmentVariable("AKOYA_BUILT_IN_MODELS");
        if (string.IsNullOrEmpty(builtIn)) return; // skipped: the variable is unset
        // A temp downloads root, so nothing outside temp can be deleted by the load.
        var store = new ModelStore(_root, new HttpClient(), ModelManifest.Spec, builtInRoot: builtIn);
        var model = new SpeechModel(store);

        var transcriber = model.Load(InferenceProvider.Cpu);
        await transcriber.TranscribeAsync(new float[16000]);
    }
}
