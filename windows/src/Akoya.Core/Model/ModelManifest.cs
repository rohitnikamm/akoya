namespace Akoya.Core.Model;

/// <summary>Where a speech model comes from and how to recognise a good copy of it.</summary>
public sealed record ModelSpec(
    string Url,
    long TarballBytes,
    string TarballSha256,
    string FolderName,
    ModelFiles Files);

/// <summary>File names inside the model folder.</summary>
public sealed record ModelFiles(string Encoder, string Decoder, string Joiner, string Tokens)
{
    public IEnumerable<string> All => [Encoder, Decoder, Joiner, Tokens];
}

/// <summary>The pinned Parakeet TDT 0.6B v2 int8 model, as pre-converted by sherpa-onnx.</summary>
public static class ModelManifest
{
    public const string Url =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2";

    public const long TarballBytes = 482468385;

    public const string TarballSha256 = "157c157bc51155e03e37d2466522a3a737dd9c72bb25f36eb18912964161e1ad";

    public const string FolderName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";

    public static ModelFiles Files { get; } =
        new("encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt");

    public static ModelSpec Spec { get; } = new(Url, TarballBytes, TarballSha256, FolderName, Files);
}
