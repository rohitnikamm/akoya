using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Akoya.App.Speech;
using Akoya.Core.Model;
using SharpCompress.Common;
using SharpCompress.Writers;
using Xunit;

namespace Akoya.App.Tests;

public sealed class ModelStoreTests : IDisposable
{
    private const string Folder = "fake-model";
    private static readonly ModelFiles Files = new("enc.onnx", "dec.onnx", "join.onnx", "tokens.txt");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "akoya-modelstore-" + Guid.NewGuid().ToString("N"));
    private readonly string _builtIn = Path.Combine(Path.GetTempPath(), "akoya-builtin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (var dir in new[] { _root, _builtIn })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The model files at their pinned names and sizes, under <c>root/Folder</c>.</summary>
    private static void WriteFakeModel(string root)
    {
        var dir = Path.Combine(root, Folder);
        Directory.CreateDirectory(dir);
        foreach (var name in Files.All) File.WriteAllText(Path.Combine(dir, name), "content of " + name);
    }

    private ModelStore BuiltInStore(Func<byte[]>? served = null) =>
        new(_root, new HttpClient(new StubHandler(served ?? (() => throw new InvalidOperationException("downloaded")))),
            SpecFor([]), builtInRoot: _builtIn);

    private sealed class StubHandler(Func<byte[]> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body()) });
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>Delivers a few bytes, then never produces more data until the read is cancelled.</summary>
    private sealed class StalledBodyHandler(byte[] firstChunk) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream(firstChunk)) });
    }

    private sealed class StalledStream(byte[] firstChunk) : Stream
    {
        private bool _gaveFirst;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_gaveFirst)
            {
                _gaveFirst = true;
                firstChunk.CopyTo(buffer);
                return firstChunk.Length;
            }

            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static ModelSpec SpecFor(byte[] tarball, long? bytes = null, string? sha = null, IReadOnlyDictionary<string, long>? fileBytes = null) => new(
        "https://example.invalid/model.tar.bz2",
        bytes ?? tarball.Length,
        sha ?? Convert.ToHexString(SHA256.HashData(tarball)),
        Folder,
        Files,
        fileBytes ?? Files.All.ToDictionary(n => n, n => (long)("content of " + n).Length));

    private ModelStore StoreFor(byte[] served, ModelSpec spec) =>
        new(_root, new HttpClient(new StubHandler(() => served)), spec);

    private static byte[] MakeTarBz2()
    {
        using var ms = new MemoryStream();
        using (var writer = WriterFactory.OpenWriter(ms, ArchiveType.Tar, new WriterOptions(CompressionType.BZip2) { LeaveStreamOpen = true }))
        {
            foreach (var name in new[] { Files.Encoder, Files.Decoder, Files.Joiner, Files.Tokens, "test_wavs/0.wav" })
            {
                using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content of " + name));
                writer.Write($"{Folder}/{name}", content, DateTime.UtcNow);
            }
        }
        return ms.ToArray();
    }

    private void AssertNoLeftovers()
    {
        if (!Directory.Exists(_root)) return;
        Assert.Empty(Directory.EnumerateFiles(_root, "*.part"));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".extract-*"));
    }

    [Fact]
    public async Task ModelStore_HashMismatch_ThrowsAndCleansUp()
    {
        var zeros = new byte[1024];
        var store = StoreFor(zeros, SpecFor(zeros, sha: new string('0', 64)));

        await Assert.ThrowsAsync<ModelDownloadException>(() => store.EnsureAsync(new Progress<double>(), CancellationToken.None));

        Assert.False(store.IsInstalled);
        Assert.False(Directory.Exists(store.ModelDirectory));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task ModelStore_SizeMismatch_ThrowsAndCleansUp()
    {
        var zeros = new byte[1024];
        var store = StoreFor(zeros, SpecFor(zeros, bytes: 2048));

        await Assert.ThrowsAsync<ModelDownloadException>(() => store.EnsureAsync(new Progress<double>(), CancellationToken.None));

        Assert.False(Directory.Exists(store.ModelDirectory));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task ModelStore_Success_MovesFolderAtomically()
    {
        var tarball = MakeTarBz2();
        var store = StoreFor(tarball, SpecFor(tarball));
        var reports = new List<double>();

        await store.EnsureAsync(new SyncProgress(reports.Add), CancellationToken.None);

        Assert.True(store.IsInstalled);
        Assert.Equal("content of " + Files.Encoder, File.ReadAllText(Path.Combine(store.ModelDirectory, Files.Encoder)));
        Assert.True(File.Exists(Path.Combine(store.ModelDirectory, "test_wavs", "0.wav")));
        AssertNoLeftovers();
        Assert.Equal(1.0, reports[^1]);
        Assert.All(reports, r => Assert.InRange(r, 0.0, 1.0));
    }

    [Fact]
    public async Task ModelStore_StalePartialsAndHalfInstall_AreReplaced()
    {
        var tarball = MakeTarBz2();
        Directory.CreateDirectory(Path.Combine(_root, ".extract-stale"));
        File.WriteAllText(Path.Combine(_root, Folder + ".tar.bz2.part"), "junk");
        Directory.CreateDirectory(Path.Combine(_root, Folder));
        File.WriteAllText(Path.Combine(_root, Folder, Files.Tokens), "half");
        var store = StoreFor(tarball, SpecFor(tarball));
        Assert.False(store.IsInstalled);

        await store.EnsureAsync(new Progress<double>(), CancellationToken.None);

        Assert.True(store.IsInstalled);
        Assert.Equal("content of " + Files.Tokens, File.ReadAllText(Path.Combine(store.ModelDirectory, Files.Tokens)));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task ModelStore_AlreadyInstalled_DoesNotDownload()
    {
        var tarball = MakeTarBz2();
        var first = StoreFor(tarball, SpecFor(tarball));
        await first.EnsureAsync(new Progress<double>(), CancellationToken.None);

        var second = new ModelStore(_root, new HttpClient(new StubHandler(() => throw new InvalidOperationException("downloaded"))), SpecFor(tarball));
        await second.EnsureAsync(new Progress<double>(), CancellationToken.None);

        Assert.True(second.IsInstalled);
    }

    [Fact]
    public async Task ModelStore_StalledBody_ThrowsModelDownloadExceptionAndReleasesGate()
    {
        var spec = SpecFor(new byte[4096]);
        var store = new ModelStore(_root, new HttpClient(new StalledBodyHandler(new byte[100])), spec, TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAsync<ModelDownloadException>(() => store.EnsureAsync(new Progress<double>(), CancellationToken.None));
        Assert.Contains("stalled", ex.Message);
        AssertNoLeftovers();

        // The gate must not stay held: a second call fails the same way instead of hanging.
        await Assert.ThrowsAsync<ModelDownloadException>(() => store.EnsureAsync(new Progress<double>(), CancellationToken.None));
    }

    [Fact]
    public async Task ModelStore_UserCancel_ThrowsOperationCanceledAndCleansUp()
    {
        var spec = SpecFor(new byte[4096]);
        var store = new ModelStore(_root, new HttpClient(new StalledBodyHandler(new byte[100])), spec, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.EnsureAsync(new Progress<double>(), cts.Token));

        Assert.False(Directory.Exists(store.ModelDirectory));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task ModelStore_Http404_ThrowsModelDownloadException()
    {
        var store = new ModelStore(_root, new HttpClient(new StatusHandler(HttpStatusCode.NotFound)), SpecFor(new byte[1024]));

        await Assert.ThrowsAsync<ModelDownloadException>(() => store.EnsureAsync(new Progress<double>(), CancellationToken.None));

        AssertNoLeftovers();
    }

    [Fact]
    public async Task ModelStore_MoreBytesThanExpected_FailsFast()
    {
        var served = new byte[4096];
        var store = StoreFor(served, SpecFor(served, bytes: 1024));

        var ex = await Assert.ThrowsAsync<ModelDownloadException>(() => store.EnsureAsync(new Progress<double>(), CancellationToken.None));

        Assert.Contains("larger than expected", ex.Message);
        AssertNoLeftovers();
    }

    [Fact]
    public async Task ModelStore_TruncatedFile_IsNotInstalled_AndEnsureDownloadsAgain()
    {
        var tarball = MakeTarBz2();
        var downloads = 0;
        var store = new ModelStore(_root, new HttpClient(new StubHandler(() =>
        {
            downloads++;
            return tarball;
        })), SpecFor(tarball));
        await store.EnsureAsync(new Progress<double>(), CancellationToken.None);
        Assert.True(store.IsInstalled);

        File.WriteAllBytes(Path.Combine(store.ModelDirectory, Files.Encoder), []); // a zero-byte encoder

        Assert.False(store.IsInstalled);
        await store.EnsureAsync(new Progress<double>(), CancellationToken.None);
        Assert.Equal(2, downloads);
        Assert.True(store.IsInstalled);
        Assert.Equal("content of " + Files.Encoder, File.ReadAllText(Path.Combine(store.ModelDirectory, Files.Encoder)));
    }

    [Fact]
    public async Task ModelStore_ArchiveFileWithTheWrongSize_FailsTheInstall()
    {
        var tarball = MakeTarBz2();
        var sizes = Files.All.ToDictionary(n => n, n => (long)("content of " + n).Length);
        sizes[Files.Joiner] += 1;
        var store = StoreFor(tarball, SpecFor(tarball, fileBytes: sizes));

        await Assert.ThrowsAsync<ModelDownloadException>(() => store.EnsureAsync(new Progress<double>(), CancellationToken.None));
        Assert.False(store.IsInstalled);
        AssertNoLeftovers();
    }

    [Fact]
    public async Task ModelStore_Discard_RemovesTheModelSoTheNextEnsureDownloads()
    {
        var tarball = MakeTarBz2();
        var store = StoreFor(tarball, SpecFor(tarball));
        await store.EnsureAsync(new Progress<double>(), CancellationToken.None);

        store.Discard();

        Assert.False(store.IsInstalled);
        Assert.False(Directory.Exists(store.ModelDirectory));
    }

    [Fact]
    public void BuiltInCompleteIsUsed()
    {
        WriteFakeModel(_builtIn);
        var store = BuiltInStore();

        Assert.True(store.UsesBuiltIn);
        Assert.Equal(Path.Combine(_builtIn, Folder), store.ModelDirectory);
        Assert.True(store.IsInstalled);
    }

    [Fact]
    public void BuiltInIncompleteFallsBackToDownloadFolder()
    {
        WriteFakeModel(_builtIn);
        File.WriteAllBytes(Path.Combine(_builtIn, Folder, Files.Encoder), []); // a zero-byte encoder
        var store = BuiltInStore();

        Assert.False(store.UsesBuiltIn);
        Assert.Equal(Path.Combine(_root, Folder), store.ModelDirectory);
        Assert.False(store.IsInstalled);

        WriteFakeModel(_root);
        Assert.True(store.IsInstalled);
        Assert.Equal(Path.Combine(_root, Folder), store.ModelDirectory);
    }

    [Fact]
    public async Task EnsureDoesNothingWithBuiltIn()
    {
        WriteFakeModel(_builtIn);
        var requests = 0;
        var store = BuiltInStore(() =>
        {
            requests++;
            throw new InvalidOperationException("downloaded");
        });
        var reports = new List<double>();

        await store.EnsureAsync(new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(0, requests);
        Assert.Equal(new[] { 1.0 }, reports);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void DiscardNeverDeletesBuiltIn()
    {
        WriteFakeModel(_builtIn);
        WriteFakeModel(_root);
        var store = BuiltInStore();

        store.Discard();

        Assert.All(Files.All, name => Assert.True(File.Exists(Path.Combine(_builtIn, Folder, name))));
        Assert.True(store.UsesBuiltIn);
        Assert.False(Directory.Exists(Path.Combine(_root, Folder)));
    }

    [Fact]
    public void RemoveDownloadedCopyDeletesOnlyTheDownloadRoot()
    {
        WriteFakeModel(_builtIn);
        WriteFakeModel(_root);
        File.WriteAllText(Path.Combine(_root, Folder + ".tar.bz2.part"), "junk");
        Directory.CreateDirectory(Path.Combine(_root, ".extract-stale"));
        var store = BuiltInStore();

        store.RemoveDownloadedCopy();

        Assert.False(Directory.Exists(_root));
        Assert.All(Files.All, name => Assert.True(File.Exists(Path.Combine(_builtIn, Folder, name))));
        Assert.True(store.UsesBuiltIn);

        store.RemoveDownloadedCopy(); // nothing left to delete: no error
    }

    [Fact]
    public void DiscardWhileUsingBuiltIn_KeepsTheDownloadedCopyAndSwitchesToIt()
    {
        WriteFakeModel(_builtIn);
        WriteFakeModel(_root);
        var store = BuiltInStore();
        Assert.True(store.UsesBuiltIn);

        store.Discard();

        Assert.False(store.UsesBuiltIn);
        Assert.Equal(Path.Combine(_root, Folder), store.ModelDirectory);
        Assert.True(store.IsInstalled);
        Assert.All(Files.All, name => Assert.True(File.Exists(Path.Combine(_root, Folder, name))));
        Assert.All(Files.All, name => Assert.True(File.Exists(Path.Combine(_builtIn, Folder, name))));
    }

    [Fact]
    public async Task RejectedBuiltIn_EnsureDownloads()
    {
        WriteFakeModel(_builtIn);
        var tarball = MakeTarBz2();
        var store = new ModelStore(_root, new HttpClient(new StubHandler(() => tarball)), SpecFor(tarball), builtInRoot: _builtIn);

        store.RejectBuiltIn();
        Assert.False(store.IsInstalled);
        await store.EnsureAsync(new Progress<double>(), CancellationToken.None);

        Assert.True(store.IsInstalled);
        Assert.Equal(Path.Combine(_root, Folder), store.ModelDirectory);
        Assert.All(Files.All, name => Assert.True(File.Exists(Path.Combine(_builtIn, Folder, name))));
    }

    [Fact]
    public void Resolve_ReturnsTheDirectoryAndWhetherItIsBuiltIn()
    {
        WriteFakeModel(_builtIn);
        var store = BuiltInStore();

        Assert.Equal((Path.Combine(_builtIn, Folder), true), store.Resolve());
        store.RejectBuiltIn();
        Assert.Equal((Path.Combine(_root, Folder), false), store.Resolve());
    }

    [Fact]
    public void RemoveDownloadedCopy_DoesNothingWhenTheBuiltInIsInsideTheDownloadRoot()
    {
        var builtIn = Path.Combine(_root, "app", "models");
        WriteFakeModel(builtIn);
        var store = new ModelStore(_root, new HttpClient(), SpecFor([]), builtInRoot: builtIn);
        Assert.True(store.UsesBuiltIn);

        store.RemoveDownloadedCopy();
        Assert.True(store.UsesBuiltIn);

        var same = new ModelStore(builtIn, new HttpClient(), SpecFor([]), builtInRoot: builtIn);
        same.RemoveDownloadedCopy();
        Assert.True(same.UsesBuiltIn);
    }

    [ModelFact]
    public void RealModel_FileSizesMatchTheManifest()
    {
        foreach (var (name, bytes) in ModelManifest.Spec.FileBytes!)
        {
            Assert.Equal(bytes, new FileInfo(Path.Combine(ModelFixture.Dir!, name)).Length);
        }
    }

    private sealed class SyncProgress(Action<double> onReport) : IProgress<double>
    {
        public void Report(double value) => onReport(value);
    }
}
