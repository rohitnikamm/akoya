using System.Net.Http;
using System.Windows.Threading;
using Akoya.App.Platform;
using Akoya.App.Speech;

namespace Akoya.App;

/// <summary>The real ports for <see cref="AppHost"/>. Call on the UI thread after the dispatcher exists.</summary>
public static class AppComposition
{
    public static AppPorts CreatePorts(Dispatcher dispatcher, Action shutdown, ILog log)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(log);

        // These capture SynchronizationContext.Current, so they must be built here, on the UI thread.
        var scheduler = new DispatcherScheduler(dispatcher);
        var catalog = new MicrophoneCatalog();
        var recorder = new WasapiRecorder();
        var pasteClipboard = new SystemClipboard();
        // Copy from the panel uses its own instance, so a paste's restore never mistakes it for its own text.
        var copyClipboard = new SystemClipboard();
        var http = new HttpClient();

        return new AppPorts
        {
            Recorder = recorder,
            Microphones = catalog,
            Inserter = new ClipboardPasteInserter(pasteClipboard, ElevationCheck.ForegroundIsElevated, KeyInjector.SendCtrlV, scheduler),
            Scheduler = scheduler,
            CreateHotkeyHook = (key, onEvent) => new KeyboardHookPort(key, onEvent),
            Model = new SpeechModel(new ModelStore(SpeechModel.DefaultRoot, http)),
            Settings = new SettingsFile(SettingsFile.DefaultPath),
            Pill = new PillPresenter(),
            Session = new SystemSessionEvents(dispatcher),
            Startup = new RegistryStartup(),
            Log = log,
            Shutdown = shutdown,
            Updates = new UpdaterPort(new Updater(AppHost.RepoUrl)),
            MicAccessAllowed = MicPrivacy.IsDesktopAccessAllowed,
            OpenMicSettings = MicPrivacy.OpenSettings,
            CopyToClipboard = text => CopyWithRetry(copyClipboard, text),
            // TODO(Task 10/11 hookup): CreateTray and CreateSetup.
            Owned = [recorder, catalog, pasteClipboard, copyClipboard, http],
        };
    }

    private static bool CopyWithRetry(SystemClipboard clipboard, string text)
    {
        for (var attempt = 1; attempt <= ClipboardPasteInserter.MaxSetAttempts; attempt++)
        {
            // A copy the user asked for is an ordinary copy: it may go to clipboard history.
            if (clipboard.TrySetText(text, excludeFromHistory: false)) return true;
            Thread.Sleep(ClipboardPasteInserter.RetryDelay);
        }
        return false;
    }
}
