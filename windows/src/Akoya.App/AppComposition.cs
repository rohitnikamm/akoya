using System.Net.Http;
using System.Windows.Threading;
using Akoya.App.Platform;
using Akoya.App.Setup;
using Akoya.App.Speech;
using Akoya.App.Tray;

namespace Akoya.App;

/// <summary>The real ports for <see cref="AppHost"/>. Call on the UI thread after the dispatcher exists.</summary>
public static class AppComposition
{
    /// <param name="modelsRoot">Where the speech model lives; default <c>%LocalAppData%\Akoya\models</c>.</param>
    public static AppPorts CreatePorts(Dispatcher dispatcher, Action shutdown, ILog log, string? modelsRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(log);

        // These capture SynchronizationContext.Current, so they must be built here, on the UI thread.
        var scheduler = new DispatcherScheduler(dispatcher);
        var catalog = new MicrophoneCatalog();
        var recorder = new WasapiRecorder();
        var clipboard = new SystemClipboard();
        var http = new HttpClient();

        return new AppPorts
        {
            Recorder = recorder,
            Microphones = catalog,
            Inserter = new ClipboardPasteInserter(clipboard, ElevationCheck.ForegroundIsElevated, KeyInjector.SendCtrlV),
            Scheduler = scheduler,
            CreateHotkeyHook = (key, onEvent) => new KeyboardHookPort(key, onEvent),
            Model = new SpeechModel(new ModelStore(modelsRoot ?? SpeechModel.DefaultRoot, http)),
            Settings = new SettingsFile(SettingsFile.DefaultPath),
            Pill = new PillPresenter(),
            Session = new SystemSessionEvents(dispatcher),
            Startup = new RegistryStartup(),
            Log = log,
            Shutdown = shutdown,
            Updates = new UpdaterPort(new Updater(AppHost.RepoUrl)),
            MicAccessAllowed = MicPrivacy.IsDesktopAccessAllowed,
            OpenMicSettings = MicPrivacy.OpenSettings,
            InputIdle = InputIdle.Time,
            // One attempt, no waiting on the UI thread.
            CopyToClipboard = clipboard.TrySetText,
            CreateTray = commands => new TrayPresenter(commands, scheduler),
            CreateSetup = context => new SetupPresenter(context, catalog),
            Owned = [recorder, catalog, clipboard, http],
        };
    }
}
