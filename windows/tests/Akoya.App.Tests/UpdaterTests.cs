using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Akoya.App.Platform;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Xunit;

namespace Akoya.App.Tests;

public sealed class UpdaterTests : IDisposable
{
    const string Repo = "https://github.com/rohitnikamm/akoya";
    const string ReleasesUrl = "https://api.github.com/repos/rohitnikamm/akoya/releases?per_page=10&page=1";

    readonly string _packages = Path.Combine(Path.GetTempPath(), "akoya-updater-" + Guid.NewGuid().ToString("N"));

    public UpdaterTests() => Directory.CreateDirectory(_packages);

    public void Dispose()
    {
        try { Directory.Delete(_packages, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Updater_NotInstalled_IsNoOp()
    {
        // Under a test runner (or `dotnet run`) the app was not installed by Velopack.
        var updater = new Updater(Repo);

        Assert.False(await updater.CheckAndDownloadAsync());
        Assert.False(updater.UpdateReady);
        updater.ApplyAndRestart(); // nothing to apply: must not throw or exit
    }

    [Fact]
    public async Task Updater_NotInstalled_NeverTouchesTheNetwork()
    {
        var downloader = new FakeDownloader();
        var updater = Make(installedVersion: null, downloader);

        Assert.False(await updater.CheckAndDownloadAsync());
        Assert.Empty(downloader.Requests);
    }

    [Fact]
    public async Task Updater_NewerWindowsRelease_IsDownloadedAndReady()
    {
        var downloader = FeedWithWindowsRelease("1.1.0", out _);
        var updater = Make("1.0.0", downloader);

        Assert.True(await updater.CheckAndDownloadAsync(), updater.LastError?.ToString());

        Assert.True(updater.UpdateReady);
        Assert.Null(updater.LastError);
        Assert.True(File.Exists(Path.Combine(_packages, "Akoya-1.1.0-win-full.nupkg")));
    }

    [Fact]
    public async Task Updater_NewerMacOnlyReleaseExists_StillFindsTheWindowsRelease()
    {
        // The Mac release is newer and has no releases.win.json; Velopack has to skip it.
        var downloader = FeedWithWindowsRelease("1.1.0", out _, macOnlyReleaseIsNewer: true);
        var updater = Make("1.0.0", downloader);

        Assert.True(await updater.CheckAndDownloadAsync(), updater.LastError?.ToString());
        Assert.True(updater.UpdateReady);
    }

    [Fact]
    public async Task Updater_SameVersion_HasNothingToDownload()
    {
        var downloader = FeedWithWindowsRelease("1.0.0", out _);
        var updater = Make("1.0.0", downloader);

        Assert.False(await updater.CheckAndDownloadAsync());
        Assert.False(updater.UpdateReady);
        Assert.Empty(Directory.GetFiles(_packages, "*.nupkg"));
    }

    [Fact]
    public async Task Updater_NetworkFailure_ReturnsFalseAndKeepsTheError()
    {
        var downloader = new FakeDownloader { Fail = true };
        var updater = Make("1.0.0", downloader);

        Assert.False(await updater.CheckAndDownloadAsync());
        Assert.False(updater.UpdateReady);
        Assert.NotNull(updater.LastError);
    }

    [Fact]
    public async Task Updater_BadChecksum_IsNotReady()
    {
        var downloader = FeedWithWindowsRelease("1.1.0", out var package);
        downloader.Files[package.Url] = Encoding.UTF8.GetBytes("a different file of the same length..");
        var updater = Make("1.0.0", downloader);

        Assert.False(await updater.CheckAndDownloadAsync());
        Assert.False(updater.UpdateReady);
        Assert.NotNull(updater.LastError);
    }

    Updater Make(string? installedVersion, FakeDownloader downloader)
    {
        var source = new GithubSource(Repo, null, false, downloader);
        var locator = installedVersion is null
            ? new NotInstalledLocator()
            : (IVelopackLocator)new TestVelopackLocator("Akoya", installedVersion, _packages, null, null, null, "win");
        return new Updater(() => new UpdateManager(source, null, locator));
    }

    record Package(string Url, byte[] Bytes);

    static FakeDownloader FeedWithWindowsRelease(string version, out Package package, bool macOnlyReleaseIsNewer = false)
    {
        var bytes = Encoding.UTF8.GetBytes("pretend this is Akoya-" + version + "-win-full.nupkg");
        var name = $"Akoya-{version}-win-full.nupkg";
        var feedUrl = "https://example.test/dl/releases.win.json";
        var packageUrl = "https://example.test/dl/" + name;
        package = new Package(packageUrl, bytes);

        var feed = $$"""
            {"Assets":[{"PackageId":"Akoya","Version":"{{version}}","Type":"Full","FileName":"{{name}}",
            "SHA1":"{{Hex(SHA1.HashData(bytes))}}","SHA256":"{{Hex(SHA256.HashData(bytes)).ToUpperInvariant()}}",
            "Size":{{bytes.Length}},"NotesMarkdown":"","NotesHTML":""}]}
            """;

        var windows = $$"""
            {"name":"Akoya for Windows {{version}}","prerelease":false,"published_at":"2026-11-01T12:00:00Z",
             "assets":[{"name":"releases.win.json","browser_download_url":"{{feedUrl}}","url":"{{feedUrl}}"},
                       {"name":"{{name}}","browser_download_url":"{{packageUrl}}","url":"{{packageUrl}}"}]}
            """;
        var mac = """
            {"name":"v0.3.0","prerelease":false,"published_at":"2026-12-01T12:00:00Z",
             "assets":[{"name":"Akoya-v0.3.0.zip","browser_download_url":"https://example.test/dl/Akoya-v0.3.0.zip","url":"https://example.test/dl/Akoya-v0.3.0.zip"}]}
            """;
        // GitHub lists releases newest first; Velopack sorts them itself as well.
        var releases = macOnlyReleaseIsNewer ? $"[{mac},{windows}]" : $"[{windows}]";

        var downloader = new FakeDownloader();
        downloader.Strings[ReleasesUrl] = releases;
        downloader.Files[feedUrl] = Encoding.UTF8.GetBytes(feed);
        downloader.Files[packageUrl] = bytes;
        return downloader;
    }

    static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    /// <summary>A locator for a copy that was not installed by Setup.exe (no current version).</summary>
    sealed class NotInstalledLocator : TestVelopackLocator
    {
        public NotInstalledLocator() : base("Akoya", "0.0.0", Path.GetTempPath()) { }
        public override SemanticVersion? CurrentlyInstalledVersion => null;
    }

    sealed class FakeDownloader : IFileDownloader
    {
        public Dictionary<string, string> Strings { get; } = new();
        public Dictionary<string, byte[]> Files { get; } = new();
        public List<string> Requests { get; } = new();
        public bool Fail { get; set; }

        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
        {
            Record(url);
            File.WriteAllBytes(targetFile, Files[url]);
            progress(100);
            return Task.CompletedTask;
        }

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Record(url);
            return Task.FromResult(Files[url]);
        }

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Record(url);
            return Task.FromResult(Strings[url]);
        }

        void Record(string url)
        {
            Requests.Add(url);
            if (Fail) throw new HttpRequestException("offline");
        }
    }
}
