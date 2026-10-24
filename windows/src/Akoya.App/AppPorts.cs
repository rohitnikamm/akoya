using Akoya.App.Pill;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Akoya.Core.Mics;
using Akoya.Core.Settings;

namespace Akoya.App;

// The seams AppHost is built from. The real implementations are in AppAdapters.cs (and the tray and setup
// windows); tests pass fakes. Every member is called on the UI thread unless it says otherwise.

/// <summary>The active capture endpoints (<see cref="Platform.MicrophoneCatalog"/>).</summary>
public interface IMicrophoneCatalog
{
    /// <summary>An input was added or removed, or the Windows default changed. Raised on the UI thread.</summary>
    event Action? Changed;

    IReadOnlyList<EndpointFacts> Snapshot();
}

/// <summary>The low-level keyboard hook (<see cref="Platform.KeyboardHook"/>).</summary>
public interface IHotkeyHook : IDisposable
{
    /// <summary>Throws <see cref="Platform.HotkeyUnavailableException"/>.</summary>
    void Install();

    /// <summary>
    /// Throws <see cref="Platform.HotkeyUnavailableException"/>. With <paramref name="onlyIfSilentFor"/>, the hook thread
    /// re-checks the silence first and leaves the hook (and any held key) alone if a key arrived meanwhile: false.
    /// </summary>
    bool Reinstall(TimeSpan? onlyIfSilentFor = null);

    /// <summary>How many keys the hook has seen (any key). Read on the UI thread.</summary>
    long KeysSeen { get; }

    void SetKey(HotkeyKey key);

    void SetRecordingActive(bool active);

    void Reset();

    /// <summary>How long ago the hook last saw a key (any key), or since it was (re-)installed. Read on the UI thread.</summary>
    TimeSpan SinceLastKey { get; }
}

/// <summary>The speech model on disk and the transcriber built from it.</summary>
public interface ISpeechModel
{
    bool IsInstalled { get; }

    /// <summary>Downloads and installs the model if it is missing. Progress is 0..1, reported on any thread.</summary>
    Task EnsureAsync(IProgress<double> progress, CancellationToken ct);

    /// <summary>Builds the transcriber and warms it up. Slow: AppHost calls it on a background thread.</summary>
    ITranscriber Load(InferenceProvider provider);

    /// <summary>
    /// Deletes the downloaded model (it failed to load), so the next <see cref="EnsureAsync"/> downloads it. Never deletes
    /// the copy shipped with the app.
    /// </summary>
    void Discard();
}

/// <summary>
/// The workstation locked or unlocked (or the session was switched away or back), and the PC went to sleep or woke.
/// Raised on the UI thread.
/// </summary>
public interface ISessionEvents : IDisposable
{
    event Action? Locked;

    event Action? Unlocked;

    /// <summary>The PC is going to sleep or hibernate.</summary>
    event Action? Suspended;

    /// <summary>The PC woke up.</summary>
    event Action? Resumed;
}

/// <summary>The floating pill (<see cref="PillWindow"/>).</summary>
public interface IPillPresenter : IDisposable
{
    /// <summary>
    /// Shows <paramref name="model"/>, fading the pill in if it is hidden. <paramref name="retarget"/> moves an
    /// already visible pill to the foreground window's monitor (a new recording).
    /// </summary>
    void Show(PillViewModel model, bool retarget);

    void FadeOut();
}

/// <summary>
/// What the tray panel's controls do. AppHost supplies these to the tray. <see cref="SelectLook"/> and
/// <see cref="SetRedTint"/> are the Pill options (null: the row does nothing).
/// </summary>
public sealed record TrayCommands(
    Func<string, bool> Copy,
    Action<HotkeyKey> SelectHotkey,
    Action<string?> SelectMic,
    Func<bool, bool> SetLaunchAtStartup,
    Action Retry,
    Action OpenMicSettings,
    Action RestartToUpdate,
    Action Quit,
    Action<WaveLook>? SelectLook = null,
    Action<bool>? SetRedTint = null);

/// <summary>The tray icon and its panel. Mirrors the inputs of <c>Tray.TrayPanelViewModel</c>.</summary>
public interface ITrayPresenter : IDisposable
{
    void SetPhase(DictationPhase phase);

    /// <summary>The smoothed voice level, 0..1, while listening.</summary>
    void SetVoiceLevel(double level);

    /// <param name="loadFailed">With <see cref="ModelState.Failed"/>: the model downloaded but would not load
    /// ("Speech model failed"), rather than the download failing ("Model download failed").</param>
    void SetModel(ModelState state, double? progress, bool loadFailed);

    void SetMicBlocked(bool blocked);

    void SetHotkeyUnavailable(bool unavailable);

    /// <param name="savedName">The saved mic's last known name, shown while it is missing.</param>
    void SetMicrophones(IReadOnlyList<NamedMic> mics, string? savedId, string? savedName);

    void SetSettings(AkoyaSettings settings);

    void SetLastDictation(string? text);

    void SetUpdateReady(bool ready);

    /// <summary>Opens the panel above the tray (a second launch of Akoya).</summary>
    void ShowPanel();

    /// <summary>
    /// The dictation hotkey was pressed: if the panel is open, close it and give focus back to the window the user
    /// was in, so the pill and the paste target that window.
    /// </summary>
    void ClosePanelAndReturnFocus();
}

/// <summary>What the setup window gets to work with. Try it sets <see cref="DictationCoordinator.Destination"/> to Practice.</summary>
public sealed record SetupContext(DictationCoordinator Coordinator, AkoyaSettings Settings, IScheduler Scheduler);

/// <summary>The first-run setup window.</summary>
public interface ISetupPresenter : IDisposable
{
    /// <summary>Continue on the Microphone step: the highlighted device's endpoint ID.</summary>
    event Action<string>? MicChosen;

    /// <summary>Done, Skip or the close button: setup is over (and marked done).</summary>
    event Action? Finished;

    /// <summary>Retry on "Model download failed".</summary>
    event Action? RetryModelRequested;

    void Show();

    void SetModel(ModelState state, double? progress);

    void SetHotkey(HotkeyKey key);

    void SetPhase(DictationPhase phase);

    /// <summary>The RMS of the latest audio window while recording (for the Try it waveform).</summary>
    void SetLevel(float rms);
}

/// <summary>Velopack updates (<see cref="Platform.Updater"/>).</summary>
public interface IUpdates
{
    /// <summary>True when an update is downloaded and waiting. Never throws.</summary>
    Task<bool> CheckAndDownloadAsync();

    Exception? LastError { get; }

    /// <summary>False in a copy not installed by Setup.exe (a source checkout): there is nothing to update.</summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Has the update applied once this process exits, then restarted. The caller shuts down normally afterwards.
    /// False without a ready update.
    /// </summary>
    bool ScheduleApplyOnExit();
}

/// <summary>The Launch at startup registration (<see cref="Platform.StartupRegistration"/>).</summary>
public interface IStartupSetting
{
    bool IsEnabled();

    void Set(bool enabled);

    /// <summary>Rewrites an existing registration with the current launch path (installed copies only).</summary>
    void Refresh();
}

/// <summary>settings.json.</summary>
public interface ISettingsFile
{
    /// <summary>Throws on IO errors other than a missing file (see <see cref="SettingsStore.Load"/>).</summary>
    AkoyaSettings Load();

    void Save(AkoyaSettings settings);
}

/// <summary>The app log. Never given transcripts.</summary>
public interface ILog
{
    void Info(string message);

    void Error(string message, Exception? exception = null);
}

/// <summary>Everything <see cref="AppHost"/> is composed from.</summary>
public sealed class AppPorts
{
    public required IAudioRecorder Recorder { get; init; }

    public required IMicrophoneCatalog Microphones { get; init; }

    public required ITextInserter Inserter { get; init; }

    public required IScheduler Scheduler { get; init; }

    /// <summary>
    /// Creates the hook for the saved key; events go to the callback on the UI thread, with when the key moved
    /// (on <see cref="Scheduler"/>'s clock).
    /// </summary>
    public required Func<HotkeyKey, Action<HotkeyEvent, DateTimeOffset>, IHotkeyHook> CreateHotkeyHook { get; init; }

    public required ISpeechModel Model { get; init; }

    public required ISettingsFile Settings { get; init; }

    public required IPillPresenter Pill { get; init; }

    public required ISessionEvents Session { get; init; }

    public required IStartupSetting Startup { get; init; }

    public required ILog Log { get; init; }

    /// <summary>Ends the app (Application.Shutdown); AppHost is disposed on exit.</summary>
    public required Action Shutdown { get; init; }

    /// <summary>Null until the tray exists (and in tests that don't need it).</summary>
    public Func<TrayCommands, ITrayPresenter>? CreateTray { get; init; }

    public Func<SetupContext, ISetupPresenter>? CreateSetup { get; init; }

    public IUpdates? Updates { get; init; }

    /// <summary>False when Windows' privacy switch blocks microphone access for desktop apps.</summary>
    public Func<bool> MicAccessAllowed { get; init; } = () => true;

    public Action OpenMicSettings { get; init; } = () => { };

    /// <summary>
    /// Time since the last keyboard or mouse input to the session (GetLastInputInfo); null when unknown. With
    /// <see cref="IHotkeyHook.SinceLastKey"/> it shows a hook that Windows removed silently.
    /// </summary>
    public Func<TimeSpan?> InputIdle { get; init; } = () => null;

    /// <summary>
    /// Puts text on the clipboard (Last dictation's Copy) in one attempt, without blocking. False when the clipboard is
    /// busy.
    /// </summary>
    public Func<string, bool> CopyToClipboard { get; init; } = _ => false;

    /// <summary>Disposed last, in order, when the host is disposed (the recorder, the catalog, the clipboard…).</summary>
    public IReadOnlyList<IDisposable> Owned { get; init; } = [];
}
