using Akoya.App.Pill;
using Akoya.App.Platform;
using Akoya.Core.Audio;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Akoya.Core.Mics;
using Akoya.Core.Settings;
using Akoya.Core.Ui;

namespace Akoya.App;

/// <summary>
/// The composition root: connects settings, microphones, the recorder, the speech model, the dictation
/// coordinator, the keyboard hook, the pill, the tray and setup (spec "Dictation flow", "Other behaviour", "Errors").
/// <para>Threading: construct, <see cref="Start"/>, use and <see cref="Dispose"/> it on the UI thread (the one with
/// the dispatcher and its <see cref="SynchronizationContext"/>). Only the model load and the update download run
/// elsewhere, and their results come back to the UI thread.</para>
/// <para>Logs never contain transcripts: only states, device counts and exception types and messages.</para>
/// </summary>
public sealed class AppHost : IDisposable
{
    public const string RepoUrl = "https://github.com/rohitnikamm/akoya";
    public static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);

    /// <summary>Pill timer, scanner and tray glow refresh while a dictation is on screen (about 30 Hz).</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    private static readonly TimeSpan TranscriberDisposeWait = TimeSpan.FromSeconds(2);

    private readonly AppPorts _p;
    private readonly DictationCoordinator _coordinator;
    private readonly TranscriberSlot _transcriber = new();
    private readonly PillViewModel _pill = new();
    private readonly LevelMeter _meter = new(PillViewModel.Columns);
    private readonly GlowEnvelope _glow = new();
    private readonly CancellationTokenSource _modelCts = new();

    private AkoyaSettings _settings;
    private bool _saveDisabled;
    private IHotkeyHook? _hook;
    private bool _hotkeyUnavailable;
    private ITrayPresenter? _tray;
    private ISetupPresenter? _setup;
    private ModelState _modelState = ModelState.Loading;
    private double? _modelProgress;
    private bool _modelLoadFailed;
    private bool _preparingModel;
    private bool _micBlocked;
    private string? _savedMicName;
    private string? _shownLastDictation;
    private bool _updateReady;
    private bool _restartPending;
    private IDisposable? _frameTimer;
    private IDisposable? _updateTimer;
    private DateTimeOffset _lastFrame;
    private DateTimeOffset _transcribingSince;
    private double _voiceTarget;
    private bool _started;
    private bool _disposed;

    public AppHost(AppPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        _p = ports;

        try
        {
            _settings = _p.Settings.Load();
        }
        catch (Exception e)
        {
            // An unreadable (not corrupt) file: run on defaults, and never overwrite the file this session.
            _p.Log.Error("Settings could not be read; using defaults without saving", e);
            _settings = new AkoyaSettings();
            _saveDisabled = true;
        }

        _coordinator = new DictationCoordinator(_p.Recorder, _transcriber, _p.Inserter, _p.Scheduler,
            () => _modelState, () => _settings.MicrophoneId);
    }

    internal DictationCoordinator Coordinator => _coordinator;

    internal PillViewModel Pill => _pill;

    internal AkoyaSettings Settings => _settings;

    internal ModelState ModelState => _modelState;

    internal bool HotkeyUnavailable => _hotkeyUnavailable;

    internal bool SetupOpen => _setup is not null;

    /// <summary>What the tray's controls call.</summary>
    internal TrayCommands Commands => new(
        Copy: CopyLastDictation,
        SelectHotkey: SelectHotkey,
        SelectMic: SelectMic,
        SetLaunchAtStartup: SetLaunchAtStartup,
        Retry: Retry,
        OpenMicSettings: () => _p.OpenMicSettings(),
        RestartToUpdate: RestartToUpdate,
        Quit: Quit);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) throw new InvalidOperationException("AppHost has already started.");
        _started = true;
        _p.Log.Info($"Akoya {typeof(AppHost).Assembly.GetName().Version} starting");

        _coordinator.PhaseChanged += OnPhaseChanged;
        _coordinator.Level += OnLevel;
        _coordinator.Faulted += OnFaulted;
        _p.Microphones.Changed += OnMicrophonesChanged;
        _p.Session.Locked += OnSessionLocked;
        _p.Session.Unlocked += OnSessionUnlocked;

        SyncLaunchAtStartup();

        try
        {
            _tray = _p.CreateTray?.Invoke(Commands);
        }
        catch (Exception e)
        {
            // Without the tray there is no way to quit: don't leave a headless process behind.
            _p.Log.Error("Could not create the tray", e);
            throw;
        }
        _tray?.SetSettings(_settings);
        _tray?.SetModel(_modelState, _modelProgress, _modelLoadFailed);
        RefreshMicrophones();

        InstallHook();

        if (!_settings.SetupDone || !_p.Model.IsInstalled) ShowSetup();

        _ = PrepareModelAsync();

        if (_p.Updates is { } updates)
        {
            bool installed;
            try
            {
                installed = updates.IsInstalled;
            }
            catch (Exception e)
            {
                _p.Log.Error("Could not tell whether this copy is installed", e);
                installed = false;
            }
            if (installed)
            {
                _ = CheckForUpdatesAsync();
                ScheduleUpdateCheck();
            }
            else
            {
                _p.Log.Info("Not installed by Setup.exe; updates off");
            }
        }
    }

    /// <summary>A second launch of Akoya: open the tray panel.</summary>
    public void ShowPanel()
    {
        if (_disposed) return;
        _tray?.ShowPanel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _p.Log.Info("Akoya stopping");

        _modelCts.Cancel();
        DisposeTimer(ref _frameTimer);
        DisposeTimer(ref _updateTimer);

        _coordinator.PhaseChanged -= OnPhaseChanged;
        _coordinator.Level -= OnLevel;
        _p.Microphones.Changed -= OnMicrophonesChanged;
        _p.Session.Locked -= OnSessionLocked;
        _p.Session.Unlocked -= OnSessionUnlocked;

        // A recording in progress is dropped, never pasted on the way out.
        Try(() =>
        {
            if (_coordinator.Phase == DictationPhase.Recording) _coordinator.Handle(HotkeyEvent.Cancel);
        }, "cancel the recording");
        _coordinator.Faulted -= OnFaulted;

        Try(() => _hook?.Dispose(), "release the keyboard hook");
        _hook = null;
        Try(() => _setup?.Dispose(), "close setup");
        _setup = null;
        Try(() => _tray?.Dispose(), "remove the tray icon");
        _tray = null;
        Try(_p.Pill.Dispose, "close the pill");
        Try(_p.Session.Dispose, "stop session events");

        // Disposing sherpa waits for a decode in flight: never on the UI thread.
        Try(() => _transcriber.DisposeInBackground(TranscriberDisposeWait), "release the speech model");

        foreach (var owned in _p.Owned)
            Try(owned.Dispose, "release " + owned.GetType().Name);
        // _modelCts is left undisposed: a download still unwinding may read its token.
    }

    // ---- Dictation ----

    private void OnHotkey(HotkeyEvent e, DateTimeOffset at)
    {
        if (_disposed) return;
        // An open tray panel holds the focus: give it back first, so the pill and the paste target the user's window.
        if (e == HotkeyEvent.Pressed) Guard(() => _tray?.ClosePanelAndReturnFocus(), "tray.ClosePanelAndReturnFocus");
        _coordinator.Handle(e, at);
    }

    // The coordinator raises PhaseChanged in the middle of a transition (before it stops or cancels the recorder), so
    // nothing in these handlers may throw back into it: every presenter call is guarded and logged.
    private void OnPhaseChanged(DictationPhase phase)
    {
        if (_disposed) return;
        Guard(() => _hook?.SetRecordingActive(phase == DictationPhase.Recording), "hook.SetRecordingActive");
        Guard(() => _tray?.SetPhase(phase), "tray.SetPhase");
        Guard(() => _setup?.SetPhase(phase), "setup.SetPhase");
        Guard(() => ReactToPhase(phase), "phase " + phase);
        Guard(() => UpdatePill(phase), "pill " + phase);

        if (!string.Equals(_coordinator.LastDictation, _shownLastDictation, StringComparison.Ordinal))
        {
            _shownLastDictation = _coordinator.LastDictation;
            Guard(() => _tray?.SetLastDictation(_shownLastDictation), "tray.SetLastDictation");
        }

        if (_restartPending && phase is not (DictationPhase.Recording or DictationPhase.Transcribing))
            _p.Scheduler.After(TimeSpan.Zero, RestartToUpdate); // not from inside the coordinator's call
    }

    private void ReactToPhase(DictationPhase phase)
    {
        switch (phase)
        {
            case DictationPhase.Recording:
                _meter.Reset();
                _voiceTarget = 0;
                if (_micBlocked) SetMicBlocked(false); // it recorded, so access is on again
                StartFrames();
                break;
            case DictationPhase.Transcribing:
                _transcribingSince = _p.Scheduler.Now;
                StartFrames();
                break;
            case DictationPhase.MicMissing:
                _p.Log.Info("Microphone not detected");
                RefreshMicrophones(); // the tray row turns yellow
                break;
            case DictationPhase.MicBlocked:
                _p.Log.Info("Microphone blocked by the privacy setting");
                SetMicBlocked(true);
                break;
            case DictationPhase.ModelNotReady or DictationPhase.ModelFailed:
                _p.Log.Info($"Hotkey pressed while the model is {_modelState}");
                break;
            case DictationPhase.PasteFailed:
                _p.Log.Info("Paste failed: the clipboard was busy");
                break;
        }
    }

    private void UpdatePill(DictationPhase phase)
    {
        if (phase == DictationPhase.Idle)
        {
            _pill.Apply(phase);
            _p.Pill.FadeOut();
            return;
        }

        // Setup's Try it card shows the practice dictation itself; the pill stays away.
        if (_coordinator.Destination is Destination.Practice)
        {
            _p.Pill.FadeOut();
            return;
        }

        _pill.Apply(phase);
        _p.Pill.Show(_pill, retarget: phase == DictationPhase.Recording);
    }

    private void OnLevel(float rms)
    {
        if (_disposed) return;
        Guard(() => _setup?.SetLevel(rms), "setup.SetLevel");
        if (_coordinator.Phase != DictationPhase.Recording)
        {
            _voiceTarget = 0;
            return;
        }

        _meter.Push(rms);
        _voiceTarget = (LevelMeter.Step(rms) - 1) / 7.0;
        Guard(() => _pill.SetLevels(_meter.Columns.ToArray()), "pill.SetLevels"); // a copy: the meter keeps changing
    }

    private void OnFaulted(Exception e) => _p.Log.Error("Dictation fault", e);

    private void StartFrames()
    {
        if (_frameTimer is not null) return;
        _lastFrame = _p.Scheduler.Now;
        _frameTimer = _p.Scheduler.After(FrameInterval, OnFrame);
    }

    private void OnFrame()
    {
        _frameTimer = null;
        if (_disposed) return;
        var now = _p.Scheduler.Now;
        var dt = Math.Max(0, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        var phase = _coordinator.Phase;
        var startedAt = _coordinator.RecordingStartedAt;
        Guard(() =>
        {
            if (phase == DictationPhase.Recording && startedAt is { } start)
                _pill.SetElapsed(now - start);
            else if (phase == DictationPhase.Transcribing)
                _pill.Tick((now - _transcribingSince).TotalSeconds);
        }, "pill frame");

        var level = _glow.Step(phase == DictationPhase.Recording ? _voiceTarget : 0, dt);
        var active = phase is DictationPhase.Recording or DictationPhase.Transcribing;
        if (!active && level < 0.002)
        {
            _glow.Step(0, 60); // settle at rest
            Guard(() => _tray?.SetVoiceLevel(0), "tray.SetVoiceLevel");
            return;
        }

        Guard(() => _tray?.SetVoiceLevel(level), "tray.SetVoiceLevel");
        _frameTimer = _p.Scheduler.After(FrameInterval, OnFrame);
    }

    // ---- Keyboard hook ----

    /// <summary>Installs (or re-installs) the hook once. A failure shows "Hotkey unavailable" with Retry; no retry loop.</summary>
    private void InstallHook()
    {
        try
        {
            if (_hook is null)
            {
                _hook = _p.CreateHotkeyHook(_settings.Hotkey, OnHotkey);
                _hook.Install();
            }
            else
            {
                _hook.Reinstall();
            }
            _hook.SetKey(_settings.Hotkey);
            _hook.SetRecordingActive(_coordinator.Phase == DictationPhase.Recording);
            SetHotkeyUnavailable(false);
        }
        catch (Exception e)
        {
            _p.Log.Error("Keyboard hook unavailable", e);
            SetHotkeyUnavailable(true);
        }
    }

    private void SetHotkeyUnavailable(bool unavailable)
    {
        _hotkeyUnavailable = unavailable;
        _tray?.SetHotkeyUnavailable(unavailable);
    }

    private void OnSessionLocked()
    {
        if (_disposed) return;
        _p.Log.Info("Session locked");
        // A key-up that never arrives (Win+L while holding the hotkey) must not leave a recording running.
        _coordinator.SessionLocked();
        _hook?.Reset();
    }

    private void OnSessionUnlocked()
    {
        if (_disposed) return;
        _p.Log.Info("Session unlocked");
        _hook?.Reset();
        InstallHook(); // Windows may have dropped the hook across the lock screen
    }

    // ---- Speech model ----

    private async Task PrepareModelAsync()
    {
        if (_preparingModel || _disposed) return;
        _preparingModel = true;
        var ct = _modelCts.Token;
        try
        {
            SetModelState(ModelState.Loading, null);
            var context = SynchronizationContext.Current;
            var progress = new PercentProgress(p =>
            {
                if (context is null) return;
                context.Post(_ =>
                {
                    if (!_disposed && _modelState == ModelState.Loading) SetModelState(ModelState.Loading, p);
                }, null);
            });

            if (!_p.Model.IsInstalled) _p.Log.Info("Downloading the speech model");
            await _p.Model.EnsureAsync(progress, ct);
            if (_disposed) return;
            SetModelState(ModelState.Loading, null);

            var model = _p.Model;
            var provider = _settings.Provider;
            ITranscriber transcriber;
            try
            {
                transcriber = await Task.Run(() => model.Load(provider), CancellationToken.None);
            }
            catch (Exception e) when (!_disposed)
            {
                // Present but unloadable (a corrupt encoder of the right size): delete it, so Retry downloads it again.
                _p.Log.Error("Speech model failed to load; discarding it", e);
                Try(model.Discard, "discard the speech model");
                SetModelState(ModelState.Failed, null, loadFailed: true);
                return;
            }
            if (_disposed)
            {
                TranscriberSlot.DisposeInBackground(transcriber, TimeSpan.Zero);
                return;
            }

            _transcriber.Set(transcriber);
            SetModelState(ModelState.Ready, null);
            _p.Log.Info("Speech model ready");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception e)
        {
            _p.Log.Error("Speech model failed", e);
            if (!_disposed) SetModelState(ModelState.Failed, null);
        }
        finally
        {
            _preparingModel = false;
        }
    }

    private void SetModelState(ModelState state, double? progress, bool loadFailed = false)
    {
        _modelState = state;
        _modelProgress = progress;
        _modelLoadFailed = loadFailed;
        Guard(() => _tray?.SetModel(state, progress, loadFailed), "tray.SetModel");
        Guard(() => _setup?.SetModel(state, progress), "setup.SetModel");
    }

    // ---- Microphones ----

    private void OnMicrophonesChanged()
    {
        if (_disposed) return;
        RefreshMicrophones();
    }

    private void RefreshMicrophones()
    {
        IReadOnlyList<NamedMic> mics;
        try
        {
            mics = MicrophoneNaming.Name(_p.Microphones.Snapshot());
        }
        catch (Exception e)
        {
            _p.Log.Error("Could not list microphones", e);
            mics = [];
        }

        var savedId = _settings.MicrophoneId;
        var choice = MicrophoneResolver.Resolve(savedId, mics);
        if (savedId is null) _savedMicName = null;
        else if (choice.Mic is { } mic) _savedMicName = mic.Name;
        _tray?.SetMicrophones(mics, savedId, _savedMicName);
        SetMicBlocked(!SafeMicAccessAllowed());
    }

    private bool SafeMicAccessAllowed()
    {
        try
        {
            return _p.MicAccessAllowed();
        }
        catch (Exception)
        {
            return true; // a real block still surfaces when recording starts
        }
    }

    private void SetMicBlocked(bool blocked)
    {
        _micBlocked = blocked;
        _tray?.SetMicBlocked(blocked);
    }

    // ---- Setup ----

    private void ShowSetup()
    {
        if (_setup is not null || _p.CreateSetup is not { } create) return;
        var setup = Create(() => create(new SetupContext(_coordinator, _settings, _p.Scheduler)), "setup");
        if (setup is null) return;
        _setup = setup;
        setup.MicChosen += OnSetupMicChosen;
        setup.Finished += OnSetupFinished;
        setup.RetryModelRequested += Retry;
        Try(() =>
        {
            setup.SetHotkey(_settings.Hotkey);
            setup.SetModel(_modelState, _modelProgress);
            setup.SetPhase(_coordinator.Phase);
            setup.Show();
        }, "show setup");
    }

    private void OnSetupMicChosen(string id)
    {
        if (_disposed) return;
        SelectMic(id);
    }

    private void OnSetupFinished()
    {
        if (_disposed || _setup is not { } setup) return;
        // A practice recording still running would otherwise be pasted into whatever has focus next.
        if (_coordinator.Phase == DictationPhase.Recording && _coordinator.Destination is Destination.Practice)
            _coordinator.Handle(HotkeyEvent.Cancel);
        _coordinator.Destination = new Destination.Paste();
        UpdateSettings(s => s with { SetupDone = true });
        _setup = null;
        setup.MicChosen -= OnSetupMicChosen;
        setup.Finished -= OnSetupFinished;
        setup.RetryModelRequested -= Retry;
        Try(setup.Dispose, "close setup");
    }

    // ---- Tray commands ----

    private void SelectHotkey(HotkeyKey key)
    {
        if (_disposed || key == _settings.Hotkey) return;
        // The old key's release would never come: drop the recording.
        if (_coordinator.Phase == DictationPhase.Recording) _coordinator.Handle(HotkeyEvent.Cancel);
        UpdateSettings(s => s with { Hotkey = key });
        _hook?.SetKey(key);
        _tray?.SetSettings(_settings);
        _setup?.SetHotkey(key);
    }

    private void SelectMic(string? id)
    {
        if (_disposed) return;
        if (!string.Equals(id, _settings.MicrophoneId, StringComparison.OrdinalIgnoreCase))
            UpdateSettings(s => s with { MicrophoneId = id });
        RefreshMicrophones();
    }

    /// <summary>The Launch at startup switch. False (the switch springs back) when the Run entry can't be written.</summary>
    private bool SetLaunchAtStartup(bool enabled)
    {
        if (_disposed) return false;
        try
        {
            _p.Startup.Set(enabled);
            UpdateSettings(s => s with { LaunchAtStartup = enabled });
            return true;
        }
        catch (Exception e)
        {
            _p.Log.Error("Could not change Launch at startup", e);
            return false;
        }
    }

    private void SyncLaunchAtStartup()
    {
        try
        {
            // The Run entry is the truth (the user can remove it elsewhere); keep its path current.
            var enabled = _p.Startup.IsEnabled();
            if (enabled) _p.Startup.Refresh();
            if (enabled != _settings.LaunchAtStartup) UpdateSettings(s => s with { LaunchAtStartup = enabled });
        }
        catch (Exception e)
        {
            _p.Log.Error("Could not read Launch at startup", e);
        }
    }

    private void Retry()
    {
        if (_disposed) return;
        if (_hotkeyUnavailable) InstallHook();
        if (_modelState == ModelState.Failed) _ = PrepareModelAsync();
    }

    /// <summary>Last dictation's Copy. False while a paste is borrowing the clipboard, or when another app holds it.</summary>
    private bool CopyLastDictation(string text)
    {
        if (_disposed) return false;
        // The paste (and its clipboard restore) runs while the phase is Transcribing: don't race it.
        if (_coordinator.Phase == DictationPhase.Transcribing)
        {
            _p.Log.Info("Copy ignored: a paste is in progress");
            return false;
        }
        bool copied;
        try
        {
            copied = _p.CopyToClipboard(text);
        }
        catch (Exception e)
        {
            _p.Log.Error("Copy failed", e);
            return false;
        }
        if (!copied) _p.Log.Info("Copy failed: the clipboard was busy");
        return copied;
    }

    private void Quit()
    {
        if (_disposed) return;
        _p.Log.Info("Quit");
        _p.Shutdown();
    }

    // ---- Updates ----

    private async Task CheckForUpdatesAsync()
    {
        if (_p.Updates is not { } updates || _disposed) return;
        bool ready;
        try
        {
            ready = await updates.CheckAndDownloadAsync();
        }
        catch (Exception e)
        {
            _p.Log.Error("Update check failed", e);
            return;
        }
        if (_disposed) return;

        if (ready)
        {
            if (!_updateReady) _p.Log.Info("Update downloaded; ready to restart");
            _updateReady = true;
            _tray?.SetUpdateReady(true);
        }
        else if (updates.LastError is { } error)
        {
            // A missing VelopackApp.Run() or a rate-limited GitHub API must not fail silently forever.
            _p.Log.Error("Update check failed", error);
        }
    }

    private void ScheduleUpdateCheck()
    {
        _updateTimer = _p.Scheduler.After(UpdateInterval, () =>
        {
            _updateTimer = null;
            if (_disposed) return;
            _ = CheckForUpdatesAsync();
            ScheduleUpdateCheck();
        });
    }

    private void RestartToUpdate()
    {
        if (_disposed || _p.Updates is not { } updates) return;
        _restartPending = false;
        switch (_coordinator.Phase)
        {
            case DictationPhase.Recording:
                _coordinator.Handle(HotkeyEvent.Cancel);
                break;
            case DictationPhase.Transcribing:
                // Let the paste (and the clipboard restore) finish first; OnPhaseChanged comes back here.
                _restartPending = true;
                return;
        }

        bool scheduled;
        try
        {
            scheduled = updates.ScheduleApplyOnExit();
        }
        catch (Exception e)
        {
            _p.Log.Error("Could not start the update", e);
            return;
        }
        if (!scheduled)
        {
            _p.Log.Info("Restart to update: no update is ready");
            return;
        }

        // A normal exit (Dispose releases the hook, the tray icon and the clipboard); Velopack installs once we're gone.
        _p.Log.Info("Restarting to update");
        _p.Shutdown();
    }

    // ---- Plumbing ----

    private void UpdateSettings(Func<AkoyaSettings, AkoyaSettings> change)
    {
        _settings = change(_settings);
        if (_saveDisabled) return;
        try
        {
            _p.Settings.Save(_settings);
        }
        catch (Exception e)
        {
            _p.Log.Error("Settings could not be saved", e);
        }
    }

    private T? Create<T>(Func<T?> create, string what) where T : class
    {
        try
        {
            return create();
        }
        catch (Exception e)
        {
            _p.Log.Error("Could not create the " + what, e);
            return null;
        }
    }

    private string? _lastGuardFailure;

    /// <summary>Runs a presenter call; a failure is logged (once per distinct failure in a row), never rethrown.</summary>
    private void Guard(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            var failure = what + ": " + FileLog.Describe(e);
            if (failure == _lastGuardFailure) return; // a frame loop must not flood the log
            _lastGuardFailure = failure;
            _p.Log.Error("UI update failed (" + what + ")", e);
        }
    }

    private void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _p.Log.Error("Could not " + what, e);
        }
    }

    private static void DisposeTimer(ref IDisposable? timer)
    {
        timer?.Dispose();
        timer = null;
    }

    /// <summary>Forwards progress only when the whole percent changes, so a download doesn't flood the UI thread.</summary>
    private sealed class PercentProgress(Action<double> report) : IProgress<double>
    {
        private int _lastPercent = -1;

        public void Report(double value)
        {
            if (!double.IsFinite(value)) return;
            var clamped = Math.Clamp(value, 0, 1);
            var percent = (int)Math.Round(clamped * 100, MidpointRounding.AwayFromZero);
            if (Interlocked.Exchange(ref _lastPercent, percent) == percent) return;
            report(clamped);
        }
    }
}

/// <summary>The coordinator's transcriber: forwards to the loaded model, which arrives after the coordinator exists.</summary>
internal sealed class TranscriberSlot : ITranscriber
{
    private ITranscriber? _inner;

    public void Set(ITranscriber transcriber)
    {
        var old = Interlocked.Exchange(ref _inner, transcriber);
        if (old is not null) DisposeInBackground(old, TimeSpan.Zero);
    }

    public Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct = default) =>
        (Volatile.Read(ref _inner) ?? throw new InvalidOperationException("The speech model is not loaded."))
            .TranscribeAsync(samples16k, ct);

    /// <summary>Releases the model on a pool thread, waiting up to <paramref name="wait"/> for it.</summary>
    public void DisposeInBackground(TimeSpan wait)
    {
        var inner = Interlocked.Exchange(ref _inner, null);
        if (inner is not null) DisposeInBackground(inner, wait);
    }

    public static void DisposeInBackground(ITranscriber transcriber, TimeSpan wait)
    {
        if (transcriber is not IDisposable disposable) return;
        var task = Task.Run(disposable.Dispose);
        if (wait > TimeSpan.Zero) task.Wait(wait);
    }
}
