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
/// A complete copy shipped with the app under <c>builtInRoot</c> is used instead and never modified.
/// </summary>
public sealed class ModelStore
{
    private const string PartSuffix = ".tar.bz2.part";
    private const string ExtractPrefix = ".extract-";

    private readonly string _root;
    private readonly string? _builtInRoot;
    private volatile bool _builtInRejected;
    private readonly HttpClient _http;
    private readonly ModelSpec _spec;
    private readonly TimeSpan _inactivityTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>How long a single body read may produce no data before the download is abandoned.</summary>
    public static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The copy installed with the app: <c>&lt;app dir&gt;\models</c>.</summary>
    public static string DefaultBuiltInRoot => Path.Combine(AppContext.BaseDirectory, "models");

    public ModelStore(string modelsRoot, HttpClient http)
        : this(modelsRoot, http, ModelManifest.Spec, builtInRoot: DefaultBuiltInRoot) { }

    public ModelStore(string modelsRoot, HttpClient http, ModelSpec spec, TimeSpan? inactivityTimeout = null, string? builtInRoot = null)
    {
        _root = modelsRoot;
        _http = http;
        _spec = spec;
        _inactivityTimeout = inactivityTimeout ?? DefaultInactivityTimeout;
        _builtInRoot = builtInRoot;
    }

    private string DownloadDirectory => Path.Combine(_root, _spec.FolderName);

    /// <summary>
    /// The app ships a complete copy of the model (every file at its pinned size) and it has not been rejected in
    /// this session (see <see cref="RejectBuiltIn"/>).
    /// </summary>
    public bool UsesBuiltIn =>
        !_builtInRejected && _builtInRoot is not null && IsComplete(Path.Combine(_builtInRoot, _spec.FolderName));

    /// <summary>The built-in copy when it is used, else the downloaded one.</summary>
    public string ModelDirectory => Resolve().Directory;

    /// <summary>The model folder to load and whether it is the built-in copy, from one check of the disk.</summary>
    public (string Directory, bool BuiltIn) Resolve() =>
        UsesBuiltIn ? (Path.Combine(_builtInRoot!, _spec.FolderName), true) : (DownloadDirectory, false);

    /// <summary>
    /// The built-in copy failed to load twice (a damaged install): use the downloaded copy for the rest of this session,
    /// or download one. The built-in folder itself is left alone.
    /// </summary>
    public void RejectBuiltIn() => _builtInRejected = true;

    /// <summary>Every file is there, at its pinned size (a truncated or zero-byte encoder is not installed).</summary>
    public bool IsInstalled => UsesBuiltIn || IsComplete(DownloadDirectory);

    /// <summary>
    /// The model in use failed to load twice. A downloaded copy is deleted, so the next <see cref="EnsureAsync"/>
    /// downloads it again; the built-in copy is never deleted, only rejected (see <see cref="RejectBuiltIn"/>), which
    /// keeps a downloaded copy as the fallback. Throws if the folder can't be deleted.
    /// </summary>
    public void Discard()
    {
        _gate.Wait();
        try
        {
            if (UsesBuiltIn)
            {
                RejectBuiltIn();
                return;
            }
            if (Directory.Exists(DownloadDirectory)) Directory.Delete(DownloadDirectory, recursive: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Deletes everything under <c>modelsRoot</c> (the downloaded model, .part files, extract leftovers), once the
    /// built-in copy has loaded. Never touches the built-in copy: does nothing if the built-in folder is inside
    /// <c>modelsRoot</c>. Throws if the folder can't be deleted.
    /// </summary>
    public void RemoveDownloadedCopy()
    {
        _gate.Wait();
        try
        {
            if (BuiltInIsInsideRoot()) return;
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool BuiltInIsInsideRoot()
    {
        if (_builtInRoot is null) return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var builtIn = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_builtInRoot));
        return string.Equals(builtIn, root, StringComparison.OrdinalIgnoreCase)
            || builtIn.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsComplete(string directory) =>
        _spec.Files.All.All(name =>
        {
            var file = new FileInfo(Path.Combine(directory, name));
            return file.Exists && (_spec.FileBytes is not { } sizes || !sizes.TryGetValue(name, out var bytes) || file.Length == bytes);
        });

    public async Task EnsureAsync(IProgress<double> progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (UsesBuiltIn || IsComplete(DownloadDirectory))
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
                if (!IsComplete(extracted))
                {
                    throw new ModelDownloadException("The model archive is missing expected files (or they have the wrong size).");
                }

                // A half-installed folder from an older attempt must not block the move.
                if (Directory.Exists(DownloadDirectory)) Directory.Delete(DownloadDirectory, recursive: true);
                Directory.Move(extracted, DownloadDirectory);
                progress.Report(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
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

        var declared = response.Content.Headers.ContentLength;
        if (declared > _spec.TarballBytes)
        {
            throw new ModelDownloadException($"Model download is larger than expected ({declared} bytes, expected {_spec.TarballBytes}).");
        }

        // HttpClient.Timeout does not cover the body with ResponseHeadersRead, so a stalled read would
        // hang forever (holding the gate). Each read gets its own inactivity window.
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            while (true)
            {
                readCts.CancelAfter(_inactivityTimeout);
                var n = await src.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
                if (n == 0) break;

                total += n;
                if (total > _spec.TarballBytes)
                {
                    throw new ModelDownloadException($"Model download is larger than expected (more than {_spec.TarballBytes} bytes).");
                }

                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                hash.AppendData(buffer, 0, n);
                progress.Report((double)total / _spec.TarballBytes);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ModelDownloadException($"Model download stalled (no data for {_inactivityTimeout.TotalSeconds:0.#} s).");
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
