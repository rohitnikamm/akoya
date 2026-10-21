using Akoya.Core.Model;
using Xunit;

namespace Akoya.Core.Tests;

public class ModelManifestTests
{
    [Fact]
    public void PinsTheByteSizeOfEveryModelFile_FromThePinnedTarball()
    {
        // `tar tvjf` of the tarball with sha256 157c157b… (482468385 bytes).
        Assert.Equal(652184296, ModelManifest.Spec.FileBytes!["encoder.int8.onnx"]);
        Assert.Equal(7257753, ModelManifest.Spec.FileBytes["decoder.int8.onnx"]);
        Assert.Equal(1739080, ModelManifest.Spec.FileBytes["joiner.int8.onnx"]);
        Assert.Equal(9384, ModelManifest.Spec.FileBytes["tokens.txt"]);
        Assert.Equal(ModelManifest.Files.All.OrderBy(n => n), ModelManifest.Spec.FileBytes.Keys.OrderBy(n => n));
    }
}
