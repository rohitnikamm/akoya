using Velopack;
using Velopack.Sources;

namespace Akoya.App.Platform;

/// <summary>
/// Checks the GitHub releases of this repo for a newer Windows build, downloads it in the background
/// and applies it on request. Does nothing when the app was not installed by Velopack's Setup.exe
/// (for example under <c>dotnet run</c>).
/// </summary>
public sealed class Updater
{
    readonly Func<UpdateManager> _create;
    UpdateManager? _manager;
    VelopackAsset? _ready;
    int _checking;

    /// <param name="repoUrl">For example <c>https://github.com/rohitnikamm/akoya</c>. Stable releases only.</param>
    public Updater(string repoUrl)
        : this(() => new UpdateManager(new GithubSource(repoUrl, null, false)))
    {
    }

    /// <summary>For tests: a factory for a manager with a fake source and locator.</summary>
    public Updater(Func<UpdateManager> create) => _create = create;

    /// <summary>True once an update is downloaded and waiting for <see cref="ApplyAndRestart"/>.</summary>
    public bool UpdateReady => Volatile.Read(ref _ready) is not null;

    /// <summary>The exception that made the last check or download fail, or null.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Looks for a newer release and downloads it. Returns <see cref="UpdateReady"/>: true when an update
    /// (new or downloaded earlier) is waiting, false when there is none, when this is not an installed
    /// copy, or when the check failed (see <see cref="LastError"/>). Never throws.
    /// </summary>
    public async Task<bool> CheckAndDownloadAsync()
    {
        // Two overlapping checks would fight over the same package file.
        if (Interlocked.Exchange(ref _checking, 1) == 1) return UpdateReady;
        try
        {
            LastError = null;
            var manager = TryGetInstalledManager();
            if (manager is null) return false;
            try
            {
                var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
                if (info is not null)
                {
                    await manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
                    Volatile.Write(ref _ready, info.TargetFullRelease);
                }
            }
            catch (Exception ex)
            {
                LastError = ex;
            }
            // An update downloaded in an earlier session is still waiting for a restart.
            if (!UpdateReady)
            {
                try { Volatile.Write(ref _ready, manager.UpdatePendingRestart); }
                catch (Exception ex) { LastError ??= ex; }
            }
            return UpdateReady;
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    /// <summary>
    /// Exits this process, installs the downloaded update and starts the new version. The caller must have
    /// released the keyboard hook and restored the clipboard first. Does nothing without a ready update.
    /// </summary>
    public void ApplyAndRestart()
    {
        var ready = Volatile.Read(ref _ready);
        if (ready is null) return;
        _manager?.ApplyUpdatesAndRestart(ready);
    }

    // Velopack's locator only exists after VelopackApp.Build().Run() and only reports a version for a copy
    // installed by Setup.exe. Any failure to find out means "not installed": there is nothing to update.
    UpdateManager? TryGetInstalledManager()
    {
        try
        {
            _manager ??= _create();
            return _manager.IsInstalled ? _manager : null;
        }
        catch (Exception ex)
        {
            LastError = ex;
            return null;
        }
    }
}
