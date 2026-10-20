using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Akoya.Core.Model;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Akoya.App.Speech;

public sealed class ModelDownloadException : Exception
{
    public ModelDownloadException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Owns the speech model folder under <c>modelsRoot</c>: downloads the tarball to a .part file, checks
/// its size and SHA-256, extracts into a temp folder, then renames that into place in one step.
/// </summary>
public sealed class ModelStore
{
    private const string PartSuffix = ".tar.bz2.part";
    private const string ExtractPrefix = ".extract-";

    private readonly string _root;
    private readonly HttpClient _http;
    private readonly ModelSpec _spec;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ModelStore(string modelsRoot, HttpClient http) : this(modelsRoot, http, ModelManifest.Spec) { }

    public ModelStore(string modelsRoot, HttpClient http, ModelSpec spec)
    {
        _root = modelsRoot;
        _http = http;
        _spec = spec;
    }

    public string ModelDirectory => Path.Combine(_root, _spec.FolderName);

    public bool IsInstalled =>
        _spec.Files.All.All(name => File.Exists(Path.Combine(ModelDirectory, name)));

    public async Task EnsureAsync(IProgress<double> progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsInstalled)
            {
                progress.Report(1);
                return;
            }

            Directory.CreateDirectory(_root);
            CleanPartials();

            var part = Path.Combine(_root, _spec.FolderName + PartSuffix);
            var extractDir = Path.Combine(_root, ExtractPrefix + Guid.NewGuid().ToString("N"));
            try
            {
                progress.Report(0);
                await DownloadAsync(part, progress, ct).ConfigureAwait(false);
                await Task.Run(() => Extract(part, extractDir, ct), ct).ConfigureAwait(false);

                var extracted = Path.Combine(extractDir, _spec.FolderName);
                if (!_spec.Files.All.All(name => File.Exists(Path.Combine(extracted, name))))
                {
                    throw new ModelDownloadException("The model archive is missing expected files.");
                }

                // A half-installed folder from an older attempt must not block the move.
                if (Directory.Exists(ModelDirectory)) Directory.Delete(ModelDirectory, recursive: true);
                Directory.Move(extracted, ModelDirectory);
                progress.Report(1);
            }
            catch (OperationCanceledException)
            {
                CleanPartials();
                throw;
            }
            catch (ModelDownloadException)
            {
                CleanPartials();
                throw;
            }
            catch (Exception ex)
            {
                CleanPartials();
                throw new ModelDownloadException("Model download failed: " + ex.Message, ex);
            }
            finally
            {
                DeleteQuietly(part);
                DeleteDirQuietly(extractDir);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DownloadAsync(string part, IProgress<double> progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(_spec.Url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        await using (var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                hash.AppendData(buffer, 0, n);
                total += n;
                progress.Report(Math.Min(1.0, (double)total / _spec.TarballBytes));
            }
        }

        if (total != _spec.TarballBytes)
        {
            throw new ModelDownloadException($"Model download has the wrong size ({total} bytes, expected {_spec.TarballBytes}).");
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.Equals(actual, _spec.TarballSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModelDownloadException("Model download failed its integrity check.");
        }
    }

    private static void Extract(string tarball, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        var destRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using var file = File.OpenRead(tarball);
        using var reader = ReaderFactory.OpenReader(file);
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (entry.IsDirectory || entry.Key is null) continue;

            var target = Path.GetFullPath(Path.Combine(destination, entry.Key));
            if (!target.StartsWith(destRoot, StringComparison.Ordinal))
            {
                throw new ModelDownloadException("The model archive contains an unsafe path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var entryStream = reader.OpenEntryStream();
            using var output = File.Create(target);
            entryStream.CopyTo(output);
        }
    }

    private void CleanPartials()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*" + PartSuffix)) DeleteQuietly(file);
        foreach (var dir in Directory.EnumerateDirectories(_root, ExtractPrefix + "*")) DeleteDirQuietly(dir);
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void DeleteDirQuietly(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
