using System.IO;
using Akoya.Core.Dictation;
using Akoya.Core.Model;
using Akoya.Core.Settings;
using SherpaOnnx;

namespace Akoya.App.Speech;

/// <summary>Runs Parakeet through sherpa-onnx. One decode at a time, always off the calling thread.</summary>
public sealed class SherpaTranscriber : ITranscriber, IDisposable
{
    private const int SampleRate = 16000;

    private readonly OfflineRecognizer _recognizer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SherpaTranscriber(string modelDir, InferenceProvider provider, int threads)
    {
        _ = provider;
        var files = ModelManifest.Files;
        foreach (var name in files.All)
        {
            var path = Path.Combine(modelDir, name);
            if (!File.Exists(path)) throw new FileNotFoundException("Speech model file is missing.", path);
        }

        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDir, files.Encoder);
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDir, files.Decoder);
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDir, files.Joiner);
        config.ModelConfig.Tokens = Path.Combine(modelDir, files.Tokens);
        config.ModelConfig.ModelType = "nemo_transducer";
        // v1 is CPU-only: Auto means CPU until a GPU route is chosen (see the spec's GPU section).
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = Math.Max(1, threads);
        _recognizer = new OfflineRecognizer(config);
    }

    /// <summary>Transcribes 0.5 s of silence so the first real dictation is not slow.</summary>
    public void WarmUp()
    {
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Decode(new float[SampleRate / 2]);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct = default)
    {
        if (samples16k.Length == 0) return "";

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() => Decode(samples16k), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string Decode(float[] samples)
    {
        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(SampleRate, samples);
        _recognizer.Decode(stream);
        return stream.Result.Text.Trim();
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            _recognizer.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }
}
