using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Akoya.Core.Mics;
using Akoya.Core.Settings;
using Akoya.Core.Ui;

namespace Akoya.App.Tray;

/// <summary>The colour of the status dot: blue for Ready and Busy, amber for Warning, red for Error.</summary>
public enum TrayStatusKind { Ready, Busy, Warning, Error }

/// <summary>The button the status card offers under its hint, if any.</summary>
public enum TrayStatusAction { None, Retry, OpenSettings }

/// <summary>A row of the hotkey menu.</summary>
public sealed record HotkeyOption(HotkeyKey Key, string Label, bool IsSelected);

/// <summary>A row of the microphone menu. <see cref="Id"/> null is "System default (…)".</summary>
public sealed record MicOption(string? Id, string Label, bool IsSelected);

/// <summary>What the panel's controls do; the app supplies these and persists the changes. All optional.</summary>
public sealed class TrayPanelActions
{
    /// <summary>Copies the text to the clipboard; returns whether it worked (a throw counts as failure).</summary>
    public Func<string, bool> Copy { get; init; } = _ => true;
    public Action<HotkeyKey> SelectHotkey { get; init; } = _ => { };

    /// <summary>The chosen microphone's ID; null for System default.</summary>
    public Action<string?> SelectMic { get; init; } = _ => { };
    /// <summary>Turns Launch at startup on or off; returns whether it worked (if not, or on a throw, the switch reverts).</summary>
    public Func<bool, bool> SetLaunchAtStartup { get; init; } = _ => true;

    /// <summary>Retry what failed: the model download/load or the keyboard hook.</summary>
    public Action Retry { get; init; } = () => { };

    /// <summary>Open Windows' microphone privacy settings (<c>ms-settings:privacy-microphone</c>).</summary>
    public Action OpenMicSettings { get; init; } = () => { };
    public Action RestartToUpdate { get; init; } = () => { };
    public Action Quit { get; init; } = () => { };
}

/// <summary>
/// State of the tray panel (spec "Tray icon and panel"): the status card, last dictation, the options and Quit.
/// The app feeds it with the <c>Set…</c> methods; the commands call <see cref="TrayPanelActions"/>. Pure .NET (no WPF
/// or Win32), so it is testable anywhere.
/// <para>Threading: it belongs to the thread that constructs it (the UI thread). <c>Set…</c> calls from other threads
/// (audio, model download) are posted to that thread's <see cref="SynchronizationContext"/>, so every change and every
/// <see cref="PropertyChanged"/> happens there. Without a context (plain unit tests) calls run inline.</para>
/// <para>Status precedence: listening, transcribing, hotkey unavailable, microphone blocked, model failed, model
/// loading (with %), microphone not detected, ready.</para>
/// </summary>
public sealed class TrayPanelViewModel : INotifyPropertyChanged
{
    public const string DownloadingText = "Getting the speech model ready…";
    public static readonly TimeSpan CopiedFeedback = TimeSpan.FromSeconds(1.5);

    private static readonly HotkeyKey[] HotkeyOrder = [HotkeyKey.RightAlt, HotkeyKey.RightCtrl, HotkeyKey.Copilot];

    private readonly TrayPanelActions _actions;
    private readonly IScheduler? _scheduler;
    private readonly SynchronizationContext? _context;
    private readonly int _ownerThread;
    private IDisposable? _copiedReset;

    // Inputs
    private DictationPhase _phase = DictationPhase.Idle;
    private ModelState _model = ModelState.Ready;
    private double? _modelProgress;
    private bool _modelLoadFailed;
    private bool _micBlocked;
    private bool _hotkeyUnavailable;
    private IReadOnlyList<NamedMic> _mics = [];
    private string? _savedMicId;
    private string? _savedMicName;
    private HotkeyKey _hotkey = HotkeyKey.RightAlt;
    private double _voiceLevel;

    // Outputs
    private string _statusText = "Ready";
    private TrayStatusKind _statusKind = TrayStatusKind.Ready;
    private TrayStatusAction _statusAction = TrayStatusAction.None;
    private int? _progressPercent;
    private bool _statusInHeader = true;
    private string _hotkeyLabel = Label(HotkeyKey.RightAlt);
    private IReadOnlyList<HotkeyOption> _hotkeyOptions = HotkeyOptionsFor(HotkeyKey.RightAlt);
    private bool _isHotkeyMenuOpen;
    private string? _lastDictation;
    private string _copyLabel = "Copy";
    private string _micLabel = "System default";
    private bool _micMissing = true;
    private IReadOnlyList<MicOption> _micOptions = [new MicOption(null, "System default", true)];
    private bool _isMicMenuOpen;
    private bool _launchAtStartup;
    private bool _updateReady;
    private double _glowStrength;
    private readonly Func<bool> _reduceMotion;

    /// <param name="actions">What the controls do.</param>
    /// <param name="scheduler">Timers (the Copied label).</param>
    /// <param name="reduceMotion">
    /// Whether Windows' "Animation effects" is off (read on every glow update); then the glow holds steady while
    /// listening, like the Mac with Reduce Motion. Defaults to <see cref="SystemParameters.ClientAreaAnimation"/>.
    /// </param>
    public TrayPanelViewModel(TrayPanelActions? actions = null, IScheduler? scheduler = null, Func<bool>? reduceMotion = null)
    {
        _actions = actions ?? new TrayPanelActions();
        _scheduler = scheduler;
        _reduceMotion = reduceMotion ?? (() => !SystemParameters.ClientAreaAnimation);
        _context = SynchronizationContext.Current;
        _ownerThread = Environment.CurrentManagedThreadId;

        Copy = new RelayCommand(DoCopy);
        SelectHotkey = new RelayCommand(p => DoSelectHotkey(p));
        SelectMic = new RelayCommand(p => DoSelectMic(p));
        Retry = new RelayCommand(() => _actions.Retry());
        OpenMicSettings = new RelayCommand(() => _actions.OpenMicSettings());
        RestartToUpdate = new RelayCommand(() => _actions.RestartToUpdate());
        Quit = new RelayCommand(() =>
        {
            IsHotkeyMenuOpen = false;
            IsMicMenuOpen = false;
            _actions.Quit();
        });
        ToggleHotkeyMenu = new RelayCommand(() =>
        {
            IsMicMenuOpen = false;
            IsHotkeyMenuOpen = !IsHotkeyMenuOpen;
        });
        ToggleMicMenu = new RelayCommand(() =>
        {
            IsHotkeyMenuOpen = false;
            IsMicMenuOpen = !IsMicMenuOpen;
        });
        CloseMenus = new RelayCommand(() =>
        {
            IsHotkeyMenuOpen = false;
            IsMicMenuOpen = false;
        });

        RecomputeMics();
        RecomputeStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---- Status card ----

    /// <summary>"Ready", "Listening", "Getting the speech model ready… 62%", "Model download failed"…</summary>
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public TrayStatusKind StatusKind
    {
        get => _statusKind;
        private set => Set(ref _statusKind, value);
    }

    public TrayStatusAction StatusAction
    {
        get => _statusAction;
        private set
        {
            if (Set(ref _statusAction, value))
            {
                OnPropertyChanged(nameof(ShowRetry));
                OnPropertyChanged(nameof(ShowOpenSettings));
            }
        }
    }

    public bool ShowRetry => StatusAction == TrayStatusAction.Retry;

    public bool ShowOpenSettings => StatusAction == TrayStatusAction.OpenSettings;

    /// <summary>Model download progress, 0–100, while it is known; null otherwise.</summary>
    public int? ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (Set(ref _progressPercent, value))
            {
                OnPropertyChanged(nameof(ProgressFraction));
                OnPropertyChanged(nameof(IsDownloading));
            }
        }
    }

    /// <summary><see cref="ProgressPercent"/> as 0..1 (0 when unknown), for the progress bar.</summary>
    public double ProgressFraction => (ProgressPercent ?? 0) / 100.0;

    /// <summary>The model is downloading with known progress: a progress bar shows under the status line.</summary>
    public bool IsDownloading => ProgressPercent is not null;

    /// <summary>
    /// The status sits in the card's header beside "Akoya"; the long "Getting the speech model ready…" line doesn't
    /// fit there, so while the model loads it moves under the hint instead.
    /// </summary>
    public bool StatusInHeader
    {
        get => _statusInHeader;
        private set
        {
            if (Set(ref _statusInHeader, value)) OnPropertyChanged(nameof(StatusUnderHint));
        }
    }

    /// <summary>The status line under the hint (while the model loads); the opposite of <see cref="StatusInHeader"/>.</summary>
    public bool StatusUnderHint => !StatusInHeader;

    public bool IsListening => _phase == DictationPhase.Recording;

    /// <summary>The status card's rim glow, 0..1 (0 at rest); follows the voice while listening.</summary>
    public double GlowStrength
    {
        get => _glowStrength;
        private set
        {
            if (Set(ref _glowStrength, value)) OnPropertyChanged(nameof(GlowBaseOpacity));
        }
    }

    /// <summary>The dark glow base the card cross-fades to under the glow; fully on at the listening floor.</summary>
    public double GlowBaseOpacity => Math.Min(1, GlowStrength * 4);

    // ---- Hotkey ----

    /// <summary>The keycap's label: "Right Alt", "Right Ctrl" or "Copilot key".</summary>
    public string HotkeyLabel
    {
        get => _hotkeyLabel;
        private set => Set(ref _hotkeyLabel, value);
    }

    public IReadOnlyList<HotkeyOption> HotkeyOptions
    {
        get => _hotkeyOptions;
        private set => Set(ref _hotkeyOptions, value);
    }

    public bool IsHotkeyMenuOpen
    {
        get => _isHotkeyMenuOpen;
        set
        {
            if (Set(ref _isHotkeyMenuOpen, value)) OnPropertyChanged(nameof(IsMenuOpen));
        }
    }

    /// <summary>The hotkey or the microphone menu is open (a click elsewhere in the panel, or Esc, closes it).</summary>
    public bool IsMenuOpen => IsHotkeyMenuOpen || IsMicMenuOpen;

    // ---- Last dictation ----

    public string? LastDictation
    {
        get => _lastDictation;
        private set
        {
            if (Set(ref _lastDictation, value)) OnPropertyChanged(nameof(HasLastDictation));
        }
    }

    public bool HasLastDictation => !string.IsNullOrEmpty(LastDictation);

    /// <summary>"Copy", or "Copied" for a moment after copying.</summary>
    public string CopyLabel
    {
        get => _copyLabel;
        private set => Set(ref _copyLabel, value);
    }

    // ---- Microphone ----

    /// <summary>The Microphone row's value: the chosen mic's plain name (the saved name while it is missing).</summary>
    public string MicLabel
    {
        get => _micLabel;
        private set => Set(ref _micLabel, value);
    }

    /// <summary>No usable mic: the row is highlighted yellow with "Not detected".</summary>
    public bool MicMissing
    {
        get => _micMissing;
        private set => Set(ref _micMissing, value);
    }

    /// <summary>"System default (&lt;name&gt;)", then every input by plain name; the selected one checked.</summary>
    public IReadOnlyList<MicOption> MicOptions
    {
        get => _micOptions;
        private set => Set(ref _micOptions, value);
    }

    public bool IsMicMenuOpen
    {
        get => _isMicMenuOpen;
        set
        {
            if (Set(ref _isMicMenuOpen, value)) OnPropertyChanged(nameof(IsMenuOpen));
        }
    }

    // ---- Options ----

    /// <summary>The Launch at startup switch; setting it calls <see cref="TrayPanelActions.SetLaunchAtStartup"/>.</summary>
    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (!Set(ref _launchAtStartup, value)) return;
            bool ok;
            try
            {
                ok = _actions.SetLaunchAtStartup(value);
            }
            catch (Exception)
            {
                ok = false;
            }
            if (ok) return;

            // Revert. A two-way binding ignores PropertyChanged raised while it writes the source, so raise it again
            // once that write is over.
            _launchAtStartup = !value;
            OnPropertyChanged();
            _context?.Post(_ => OnPropertyChanged(nameof(LaunchAtStartup)), null);
        }
    }

    /// <summary>An update is downloaded: the "Update ready · Restart" row shows.</summary>
    public bool UpdateReady
    {
        get => _updateReady;
        private set => Set(ref _updateReady, value);
    }

    // ---- Commands ----

    public ICommand Copy { get; }

    /// <summary>Parameter: a <see cref="HotkeyOption"/> or a <see cref="HotkeyKey"/>.</summary>
    public ICommand SelectHotkey { get; }

    /// <summary>Parameter: a <see cref="MicOption"/>, a mic ID string, or null for System default.</summary>
    public ICommand SelectMic { get; }
    public ICommand Retry { get; }
    public ICommand OpenMicSettings { get; }
    public ICommand RestartToUpdate { get; }
    public ICommand Quit { get; }
    public ICommand ToggleHotkeyMenu { get; }
    public ICommand ToggleMicMenu { get; }
    public ICommand CloseMenus { get; }

    // ---- Inputs ----

    /// <summary>The coordinator's phase: Recording shows Listening, Transcribing shows Transcribing; the rest are idle.</summary>
    public void SetPhase(DictationPhase phase)
    {
        if (PostToOwner(() => SetPhase(phase))) return;
        var wasListening = IsListening;
        _phase = phase;
        if (wasListening != IsListening)
        {
            OnPropertyChanged(nameof(IsListening));
            _voiceLevel = 0; // a level from another recording is stale
        }
        UpdateGlow();
        RecomputeStatus();
    }

    /// <summary>The smoothed voice level, 0..1, while listening; drives the rim glow.</summary>
    public void SetVoiceLevel(double level)
    {
        if (PostToOwner(() => SetVoiceLevel(level))) return;
        _voiceLevel = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        UpdateGlow();
    }

    /// <summary>The speech model's state, with download progress 0..1 while loading if known.</summary>
    /// <param name="loadFailed">The model downloaded but would not load: "Speech model failed".</param>
    public void SetModel(ModelState state, double? progress = null, bool loadFailed = false)
    {
        if (PostToOwner(() => SetModel(state, progress, loadFailed))) return;
        _model = state;
        _modelLoadFailed = state == ModelState.Failed && loadFailed;
        _modelProgress = state == ModelState.Loading && progress is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : null;
        RecomputeStatus();
    }

    public void SetMicBlocked(bool blocked)
    {
        if (PostToOwner(() => SetMicBlocked(blocked))) return;
        _micBlocked = blocked;
        RecomputeStatus();
    }

    public void SetHotkeyUnavailable(bool unavailable)
    {
        if (PostToOwner(() => SetHotkeyUnavailable(unavailable))) return;
        _hotkeyUnavailable = unavailable;
        RecomputeStatus();
    }

    /// <summary>
    /// The connected inputs (plain names) and the saved choice: <paramref name="savedId"/> null follows Windows.
    /// <paramref name="savedName"/> is the saved mic's last known name, shown while it is missing.
    /// </summary>
    public void SetMicrophones(IReadOnlyList<NamedMic> mics, string? savedId, string? savedName = null)
    {
        ArgumentNullException.ThrowIfNull(mics);
        if (PostToOwner(() => SetMicrophones(mics, savedId, savedName))) return;
        _mics = mics;
        _savedMicId = savedId;
        _savedMicName = savedName;
        RecomputeMics();
        RecomputeStatus();
    }

    /// <summary>Shows saved settings (hotkey, Launch at startup) without calling the actions back.</summary>
    public void SetSettings(AkoyaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (PostToOwner(() => SetSettings(settings))) return;
        ApplyHotkey(settings.Hotkey);
        Set(ref _launchAtStartup, settings.LaunchAtStartup, nameof(LaunchAtStartup));
        if (!string.Equals(settings.MicrophoneId, _savedMicId, StringComparison.OrdinalIgnoreCase))
        {
            _savedMicId = settings.MicrophoneId;
            RecomputeMics();
            RecomputeStatus();
        }
    }

    public void SetLastDictation(string? text)
    {
        if (PostToOwner(() => SetLastDictation(text))) return;
        LastDictation = string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public void SetUpdateReady(bool ready)
    {
        if (PostToOwner(() => SetUpdateReady(ready))) return;
        UpdateReady = ready;
    }

    // ---- Internals ----

    /// <summary>
    /// Off the owner thread (with a context to post to): posts <paramref name="call"/> there and returns true, so the
    /// caller returns. On the owner thread, or without a context, returns false and the caller runs inline.
    /// </summary>
    private bool PostToOwner(Action call)
    {
        if (_context is null || Environment.CurrentManagedThreadId == _ownerThread) return false;
        _context.Post(_ => call(), null);
        return true;
    }

    internal static string Label(HotkeyKey key) => key switch
    {
        HotkeyKey.RightCtrl => "Right Ctrl",
        HotkeyKey.Copilot => "Copilot key",
        _ => "Right Alt",
    };

    private static IReadOnlyList<HotkeyOption> HotkeyOptionsFor(HotkeyKey current) =>
        HotkeyOrder.Select(k => new HotkeyOption(k, Label(k), k == current)).ToArray();

    private void ApplyHotkey(HotkeyKey key)
    {
        _hotkey = key;
        HotkeyLabel = Label(key);
        HotkeyOptions = HotkeyOptionsFor(key);
    }

    private void DoSelectHotkey(object? parameter)
    {
        HotkeyKey? key = parameter switch
        {
            HotkeyOption option => option.Key,
            HotkeyKey k => k,
            _ => null,
        };
        IsHotkeyMenuOpen = false;
        if (key is not { } chosen || chosen == _hotkey) return;
        ApplyHotkey(chosen);
        _actions.SelectHotkey(chosen);
    }

    private void DoSelectMic(object? parameter)
    {
        string? id;
        switch (parameter)
        {
            case MicOption option:
                id = option.Id;
                break;
            case string s:
                id = s;
                break;
            case null:
                id = null;
                break;
            default:
                return;
        }

        IsMicMenuOpen = false;
        var unchanged = string.Equals(id, _savedMicId, StringComparison.OrdinalIgnoreCase);
        _savedMicId = id;
        _savedMicName = id is null ? null : _mics.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))?.Name;
        RecomputeMics();
        RecomputeStatus();
        if (!unchanged) _actions.SelectMic(id);
    }

    private void DoCopy()
    {
        if (LastDictation is not { } text) return;
        bool ok;
        try
        {
            ok = _actions.Copy(text);
        }
        catch (Exception)
        {
            ok = false;
        }
        if (!ok || _scheduler is null) return; // "Copied" only when it really is
        CopyLabel = "Copied";
        _copiedReset?.Dispose();
        _copiedReset = _scheduler.After(CopiedFeedback, ResetCopyLabel);
    }

    private void ResetCopyLabel()
    {
        if (PostToOwner(ResetCopyLabel)) return; // the scheduler may call back on a timer thread
        CopyLabel = "Copy";
    }

    private void RecomputeMics()
    {
        var choice = MicrophoneResolver.Resolve(_savedMicId, _mics);
        MicMissing = choice.Missing;
        MicLabel = choice.Mic?.Name
            ?? (_savedMicId is null ? "System default" : _savedMicName ?? "Selected microphone");

        var systemDefault = _mics.FirstOrDefault(m => m.IsDefault);
        var options = new List<MicOption>(_mics.Count + 1)
        {
            new(null, systemDefault is null ? "System default" : $"System default ({systemDefault.Name})", _savedMicId is null),
        };
        options.AddRange(_mics.Select(m => new MicOption(m.Id, m.Name,
            _savedMicId is not null && string.Equals(m.Id, _savedMicId, StringComparison.OrdinalIgnoreCase))));
        MicOptions = options;
    }

    private void RecomputeStatus()
    {
        (string Text, TrayStatusKind Kind, TrayStatusAction Action, int? Percent) status = _phase switch
        {
            DictationPhase.Recording => ("Listening", TrayStatusKind.Busy, TrayStatusAction.None, null),
            DictationPhase.Transcribing => ("Transcribing", TrayStatusKind.Busy, TrayStatusAction.None, null),
            _ when _hotkeyUnavailable => ("Hotkey unavailable", TrayStatusKind.Error, TrayStatusAction.Retry, null),
            _ when _micBlocked => ("Microphone blocked", TrayStatusKind.Warning, TrayStatusAction.OpenSettings, null),
            _ when _model == ModelState.Failed => (_modelLoadFailed ? "Speech model failed" : "Model download failed",
                TrayStatusKind.Error, TrayStatusAction.Retry, null),
            _ when _model == ModelState.Loading => Loading(_modelProgress),
            _ when MicMissing => ("Microphone not detected", TrayStatusKind.Warning, TrayStatusAction.None, null),
            _ => ("Ready", TrayStatusKind.Ready, TrayStatusAction.None, null),
        };

        StatusText = status.Text;
        StatusKind = status.Kind;
        StatusAction = status.Action;
        ProgressPercent = status.Percent;
        StatusInHeader = !status.Text.StartsWith(DownloadingText, StringComparison.Ordinal);

        static (string, TrayStatusKind, TrayStatusAction, int?) Loading(double? progress)
        {
            if (progress is not { } p) return (DownloadingText, TrayStatusKind.Busy, TrayStatusAction.None, null);
            var percent = (int)Math.Round(p * 100, MidpointRounding.AwayFromZero);
            return ($"{DownloadingText} {percent}%", TrayStatusKind.Busy, TrayStatusAction.None, percent);
        }
    }

    private void UpdateGlow()
    {
        // The Mac's mapping: GlowEnvelope opacity (0.2 floor, 0.8 at peak voice; a fixed 0.5 with animations off)
        // scaled so peak voice is full strength.
        var opacity = GlowEnvelope.Opacity(_voiceLevel, IsListening, ReduceMotion());
        GlowStrength = GlowEnvelope.LayerOpacity(opacity);
    }

    private bool ReduceMotion()
    {
        try
        {
            return _reduceMotion();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
