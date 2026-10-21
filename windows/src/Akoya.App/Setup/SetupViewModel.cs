using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Akoya.Core.Audio;
using Akoya.Core.Dictation;
using Akoya.Core.Mics;
using Akoya.Core.Ui;

namespace Akoya.App.Setup;

/// <summary>The three setup steps, in order.</summary>
public enum SetupStep
{
    Welcome,
    Microphone,
    TryIt,
}

/// <summary>What the Try it text card shows.</summary>
public enum PracticeCardKind
{
    /// <summary>"Your words will appear here."</summary>
    Placeholder,

    /// <summary>The live (then final) transcript.</summary>
    Text,

    /// <summary>The last take was lost (a partial showed but no final arrived, or the final was empty).</summary>
    RetryHint,

    /// <summary>"Getting the speech model ready… 62%".</summary>
    ModelLoading,

    /// <summary>"Model download failed" with Retry.</summary>
    ModelFailed,
}

/// <summary>Raises <see cref="PropertyChanged"/>.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A command that always runs.</summary>
public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public RelayCommand(Action execute) : this(_ => execute()) { }

    event EventHandler? ICommand.CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);
}

/// <summary>A row of setup's microphone list.</summary>
public abstract class ListRow : Observable
{
    private bool _showsSeparator;

    /// <summary>The thin line above the row: every row but the first.</summary>
    public bool ShowsSeparator
    {
        get => _showsSeparator;
        internal set => Set(ref _showsSeparator, value);
    }
}

/// <summary>An input by plain name, with its live 10-dot meter.</summary>
public sealed class MicRow : ListRow
{
    public const int MeterDots = 10;
    public const double LitOpacity = 0.95;
    public const double UnlitOpacity = 0.12;

    private static readonly double[] Quiet = Enumerable.Repeat(UnlitOpacity, MeterDots).ToArray();

    private bool _isHighlighted;
    private int _litDots;
    private double[] _meterOpacities = Quiet;

    internal MicRow(NamedMic mic, Action<string> select)
    {
        Mic = mic;
        SelectCommand = new RelayCommand(() => select(mic.Id));
    }

    public NamedMic Mic { get; }

    public string Id => Mic.Id;

    public string Name => Mic.Name;

    public bool IsHighlighted
    {
        get => _isHighlighted;
        internal set => Set(ref _isHighlighted, value);
    }

    /// <summary>Lit meter dots, 0 to 10 (<see cref="MeterLevel.LitDots"/> of the newest step).</summary>
    public int LitDots
    {
        get => _litDots;
        internal set
        {
            value = Math.Clamp(value, 0, MeterDots);
            if (!Set(ref _litDots, value)) return;
            var opacities = new double[MeterDots];
            for (var i = 0; i < MeterDots; i++) opacities[i] = i < value ? LitOpacity : UnlitOpacity;
            MeterOpacities = opacities;
        }
    }

    /// <summary>The meter's dot opacities, left to right; a new array on every change.</summary>
    public double[] MeterOpacities
    {
        get => _meterOpacities;
        private set
        {
            _meterOpacities = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Highlights this row (the user clicked it).</summary>
    public ICommand SelectCommand { get; }
}

/// <summary>The last row: "More (n)", folding away virtual and loopback inputs.</summary>
public sealed class MoreRow : ListRow
{
    private string _label = "";

    internal MoreRow(Action toggle) => ToggleCommand = new RelayCommand(toggle);

    public string Label
    {
        get => _label;
        internal set => Set(ref _label, value);
    }

    public ICommand ToggleCommand { get; }
}

/// <summary>
/// Setup (first run): Welcome → Microphone → Try it (spec "Setup (first run)"). Call it on the UI thread; the ports
/// raise their events there. The host starts the model download when it shows setup and reports it with
/// <see cref="SetModel"/>, saves <see cref="MicChosen"/>, and marks setup done on <see cref="Finished"/> (Done, Skip or ✕).
/// <para>
/// Microphone: every input by plain name with a live meter; the Windows default is highlighted and only the user moves
/// the highlight. The status chip is decided by <see cref="MicStatus.Evaluate"/> and, once shown, stays until the
/// other status replaces it or the highlight moves, so it doesn't blink between words.
/// </para>
/// </summary>
public sealed class SetupViewModel : Observable, IDisposable
{
    /// <summary>How often the privacy switch is re-read while the Microphone step shows (Windows sends no event).</summary>
    public static readonly TimeSpan PrivacyPoll = TimeSpan.FromMilliseconds(500);

    private const int WaveColumns = 23;

    private readonly IMicCatalog _catalog;
    private readonly IMicLevels _levels;
    private readonly IMicPrivacy _privacy;
    private readonly IPracticeDictation _practice;
    private readonly IScheduler _scheduler;
    private readonly MoreRow _moreRow;
    private readonly Dictionary<string, (int step, DateTimeOffset at)> _recent = new(StringComparer.Ordinal);
    private readonly LevelMeter _wave = new(WaveColumns);

    private SetupStep _step = SetupStep.Welcome;
    private IReadOnlyList<MicRow> _mics = [];
    private IReadOnlyList<MicRow> _moreMics = [];
    private IReadOnlyList<ListRow> _listItems = [];
    private bool _moreExpanded;
    private string? _highlightedId;
    private MicStatusKind _status;
    private bool _isBlocked;
    private bool _metering;
    private IDisposable? _poll;

    private ModelState _modelState = ModelState.Ready;
    private double? _modelProgress;

    private bool _practicing;
    private bool _isRecording;
    private string _practiceText = "";
    private bool _hasFinal;
    private bool _retryHint;
    private bool _awaitingResult;
    private bool _partialThisTake;
    private double[] _waveOpacities;

    private string _hotkeyLabel = "Right Alt";
    private bool _finished;
    private bool _disposed;

    public SetupViewModel(IMicCatalog catalog, IMicLevels levels, IMicPrivacy privacy, IPracticeDictation practice, IScheduler scheduler)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        _privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        _practice = practice ?? throw new ArgumentNullException(nameof(practice));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        _moreRow = new MoreRow(ToggleMore);
        _waveOpacities = TryItWave.Opacities(_wave.Columns, live: false);

        GetStartedCommand = new RelayCommand(GetStarted);
        ToggleMoreCommand = new RelayCommand(ToggleMore);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ContinueCommand = new RelayCommand(Continue);
        DoneCommand = new RelayCommand(Done);
        SkipCommand = new RelayCommand(Skip);
        RetryModelCommand = new RelayCommand(RetryModel);

        _catalog.Changed += OnCatalogChanged;
        _levels.Step += OnStep;
        _practice.PhaseChanged += OnPhase;
        _practice.Level += OnLevel;
        ReloadMics();
    }

    /// <summary>Continue was pressed: save this endpoint id as the microphone.</summary>
    public event Action<string>? MicChosen;

    /// <summary>Done, Skip or ✕: setup is over (mark it done). Raised once.</summary>
    public event Action? Finished;

    /// <summary>The Try it card's Retry after a failed model download.</summary>
    public event Action? RetryModelRequested;

    // Step and chrome

    public SetupStep Step
    {
        get => _step;
        private set
        {
            if (!Set(ref _step, value)) return;
            OnPropertyChanged(nameof(PageIndex));
            OnPropertyChanged(nameof(ShowsSkip));
        }
    }

    /// <summary>The lit page dot, 0 to 2.</summary>
    public int PageIndex => (int)Step;

    /// <summary>Skip sits beside the page dots on Microphone and Try it (not on Welcome).</summary>
    public bool ShowsSkip => Step != SetupStep.Welcome;

    // Microphone

    /// <summary>The inputs shown as rows (not in More), in naming order.</summary>
    public IReadOnlyList<MicRow> Mics
    {
        get => _mics;
        private set => Set(ref _mics, value);
    }

    /// <summary>Virtual and loopback inputs, behind the "More (n)" row.</summary>
    public IReadOnlyList<MicRow> MoreMics
    {
        get => _moreMics;
        private set => Set(ref _moreMics, value);
    }

    /// <summary>
    /// The list as shown: <see cref="Mics"/>, then the "More (n)" row last; expanded, the More row gives way to
    /// <see cref="MoreMics"/> in place (so a four-input list still fits the card).
    /// </summary>
    public IReadOnlyList<ListRow> ListItems
    {
        get => _listItems;
        private set => Set(ref _listItems, value);
    }

    public bool MoreExpanded
    {
        get => _moreExpanded;
        private set => Set(ref _moreExpanded, value);
    }

    /// <summary>The highlighted input's endpoint id: the Windows default until the user clicks a row.</summary>
    public string? HighlightedId
    {
        get => _highlightedId;
        private set => Set(ref _highlightedId, value);
    }

    public MicStatusKind Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value)) OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>"Hearing you", "&lt;name&gt; isn't hearing you", or empty.</summary>
    public string StatusText => Status switch
    {
        MicStatusKind.HearingYou => "Hearing you",
        MicStatusKind.NotHearing => $"{HighlightedRow?.Name ?? "This mic"} isn't hearing you",
        _ => "",
    };

    /// <summary>Windows' privacy switch blocks microphone access for desktop apps.</summary>
    public bool IsBlocked
    {
        get => _isBlocked;
        private set => Set(ref _isBlocked, value);
    }

    public string MicTitle => (Mics.Count + MoreMics.Count) switch
    {
        0 => "No mic found",
        _ when Mics.Count == 1 => "Is this the mic you want?",
        _ => "Which mic do you talk into?",
    };

    public string MicBody => (Mics.Count + MoreMics.Count) switch
    {
        0 => "Plug one in. It will show up here.",
        _ when Mics.Count == 1 => "Say something. The dots should move.",
        _ => "Say something. Pick the one that moves.",
    };

    private MicRow? HighlightedRow => Mics.Concat(MoreMics).FirstOrDefault(m => m.Id == HighlightedId);

    // Try it

    /// <summary>The hotkey's name on the keycap, e.g. "Right Alt".</summary>
    public string HotkeyLabel
    {
        get => _hotkeyLabel;
        set => Set(ref _hotkeyLabel, value ?? "");
    }

    /// <summary>Download progress, 0 to 1, while the model is being fetched; null otherwise.</summary>
    public double? ModelProgress
    {
        get => _modelProgress;
        private set => Set(ref _modelProgress, value);
    }

    /// <summary>The hotkey hint dims while the model isn't ready (presses can't record yet).</summary>
    public bool IsHintDimmed => _modelState != ModelState.Ready;

    /// <summary>The latest practice transcript (live partial or final).</summary>
    public string PracticeText
    {
        get => _practiceText;
        private set => Set(ref _practiceText, value);
    }

    /// <summary>A practice recording is running: the waveform is live.</summary>
    public bool IsRecording
    {
        get => _isRecording;
        private set => Set(ref _isRecording, value);
    }

    /// <summary>A non-empty final transcript arrived: Done turns light and works.</summary>
    public bool DoneReady => _hasFinal;

    public PracticeCardKind CardKind
    {
        get
        {
            if (PracticeText.Length > 0) return PracticeCardKind.Text;
            if (_modelState == ModelState.Failed) return PracticeCardKind.ModelFailed;
            if (_modelState == ModelState.Loading) return PracticeCardKind.ModelLoading;
            return _retryHint ? PracticeCardKind.RetryHint : PracticeCardKind.Placeholder;
        }
    }

    public string CardText => CardKind switch
    {
        PracticeCardKind.Text => PracticeText,
        PracticeCardKind.ModelFailed => "Model download failed",
        PracticeCardKind.ModelLoading => ModelProgress is { } p and < 1
            ? string.Create(CultureInfo.InvariantCulture, $"Getting the speech model ready… {(int)Math.Floor(p * 100)}%")
            : "Getting the speech model ready…",
        PracticeCardKind.RetryHint => "Didn't catch that, try again",
        _ => "Your words will appear here.",
    };

    /// <summary>The Try it waveform's dot opacities (<see cref="TryItWave"/>, 23 columns × 9 rows).</summary>
    public double[] WaveOpacities
    {
        get => _waveOpacities;
        private set
        {
            _waveOpacities = value;
            OnPropertyChanged();
        }
    }

    // Commands

    public ICommand GetStartedCommand { get; }

    public ICommand ToggleMoreCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand ContinueCommand { get; }

    public ICommand DoneCommand { get; }

    public ICommand SkipCommand { get; }

    public ICommand RetryModelCommand { get; }

    /// <summary>Welcome → Microphone.</summary>
    public void GetStarted()
    {
        if (_finished || Step != SetupStep.Welcome) return;
        Step = SetupStep.Microphone;
        IsBlocked = !_privacy.IsDesktopAccessAllowed();
        ReloadMics();
        if (!IsBlocked) StartMeters();
        SchedulePoll();
    }

    /// <summary>Highlights a row. Only the user does this; Akoya never moves the highlight itself.</summary>
    public void SelectMic(string id)
    {
        if (_finished || HighlightedId == id) return;
        var row = Mics.Concat(MoreMics).FirstOrDefault(m => m.Id == id);
        if (row is null) return;
        Highlight(row.Id);
    }

    public void ToggleMore()
    {
        if (MoreMics.Count == 0) return;
        MoreExpanded = !MoreExpanded;
        BuildList();
    }

    /// <summary>Opens Settings > Privacy > Microphone.</summary>
    public void OpenSettings() => _privacy.OpenSettings();

    /// <summary>Microphone → Try it, saving the highlighted input.</summary>
    public void Continue()
    {
        if (_finished || Step != SetupStep.Microphone || IsBlocked) return;
        StopMicrophone();
        if (HighlightedId is { } id) MicChosen?.Invoke(id);
        Step = SetupStep.TryIt;
        BeginPractice();
    }

    /// <summary>Finishes setup once a practice transcript has arrived (inert before, like the Mac).</summary>
    public void Done()
    {
        if (Step == SetupStep.TryIt && DoneReady) Finish();
    }

    /// <summary>Skip or ✕: ends setup on any step.</summary>
    public void Skip() => Finish();

    /// <summary>Enter: the visible primary button.</summary>
    public void PrimaryAction()
    {
        switch (Step)
        {
            case SetupStep.Welcome:
                GetStarted();
                break;
            case SetupStep.Microphone when IsBlocked:
                OpenSettings();
                break;
            case SetupStep.Microphone:
                Continue();
                break;
            case SetupStep.TryIt:
                Done();
                break;
        }
    }

    public void RetryModel() => RetryModelRequested?.Invoke();

    /// <summary>The speech model's state; <paramref name="progress"/> (0 to 1) while it downloads.</summary>
    public void SetModel(ModelState state, double? progress = null)
    {
        _modelState = state;
        ModelProgress = state == ModelState.Loading ? progress : null;
        OnPropertyChanged(nameof(IsHintDimmed));
        CardChanged();
    }

    /// <summary>Stops meters, polling and practice without raising <see cref="Finished"/> (e.g. the app quits).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TearDown();
        _catalog.Changed -= OnCatalogChanged;
        _levels.Step -= OnStep;
        _practice.PhaseChanged -= OnPhase;
        _practice.Level -= OnLevel;
    }

    private void Finish()
    {
        if (_finished) return;
        TearDown();
        Finished?.Invoke();
    }

    private void TearDown()
    {
        _finished = true;
        StopMicrophone();
        if (_practicing)
        {
            _practicing = false;
            _practice.End();
        }
    }

    // Microphone internals

    private void OnCatalogChanged()
    {
        if (_finished) return;
        ReloadMics();
        if (_metering) StartMeters();
    }

    private void ReloadMics()
    {
        var named = MicrophoneNaming.Name(_catalog.Snapshot());
        var rows = named.Select(m => new MicRow(m, SelectMic)).ToList();
        var main = rows.Where(r => !r.Mic.InMore).ToList();
        var more = rows.Where(r => r.Mic.InMore).ToList();
        if (main.Count == 0)
        {
            // Only virtual inputs: show them as the list, with nothing to fold away.
            main = more;
            more = [];
        }

        // Keep the user's pick if it's still there; else the Windows default; else the first row.
        var highlight = rows.FirstOrDefault(r => r.Id == HighlightedId)
            ?? rows.FirstOrDefault(r => r.Mic.IsDefault)
            ?? main.FirstOrDefault();
        var changed = highlight?.Id != HighlightedId;

        Mics = main;
        MoreMics = more;
        if (highlight is not null && more.Contains(highlight)) MoreExpanded = true;
        if (more.Count == 0) MoreExpanded = false;
        foreach (var row in rows) row.IsHighlighted = row == highlight;
        if (changed) Highlight(highlight?.Id);
        BuildList();
        OnPropertyChanged(nameof(MicTitle));
        OnPropertyChanged(nameof(MicBody));
        OnPropertyChanged(nameof(StatusText));
    }

    private void BuildList()
    {
        var items = new List<ListRow>(Mics);
        if (MoreExpanded)
        {
            items.AddRange(MoreMics);
        }
        else if (MoreMics.Count > 0)
        {
            _moreRow.Label = $"More ({MoreMics.Count})";
            items.Add(_moreRow);
        }
        for (var i = 0; i < items.Count; i++) items[i].ShowsSeparator = i > 0;
        ListItems = items;
    }

    private void Highlight(string? id)
    {
        HighlightedId = id;
        foreach (var row in Mics.Concat(MoreMics)) row.IsHighlighted = row.Id == id;
        Status = MicStatusKind.None;
        OnPropertyChanged(nameof(StatusText));
        // Quiet time for "isn't hearing you" counts from when this mic was highlighted.
        if (id is not null && _metering) _recent[id] = (1, _scheduler.Now);
    }

    private void StartMeters()
    {
        _metering = true;
        _recent.Clear();
        foreach (var row in Mics.Concat(MoreMics)) row.LitDots = 0;
        if (HighlightedId is { } id) _recent[id] = (1, _scheduler.Now);
        _levels.Start(Mics.Concat(MoreMics).Select(m => m.Id).ToList());
    }

    private void StopMeters()
    {
        if (!_metering) return;
        _metering = false;
        _levels.Stop();
        foreach (var row in Mics.Concat(MoreMics)) row.LitDots = 0;
    }

    private void StopMicrophone()
    {
        _poll?.Dispose();
        _poll = null;
        StopMeters();
    }

    private void OnStep(string id, int step)
    {
        if (!_metering) return;
        var row = Mics.Concat(MoreMics).FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (row is null) return;
        row.LitDots = MeterLevel.LitDots(step, MicRow.MeterDots);
        if (step >= MicStatus.SpeechStep) _recent[row.Id] = (step, _scheduler.Now);
        EvaluateStatus();
    }

    private void EvaluateStatus()
    {
        if (!_metering || HighlightedId is not { } id) return;
        var kind = MicStatus.Evaluate(id, _recent, _scheduler.Now);
        if (kind != MicStatusKind.None) Status = kind;
    }

    private void SchedulePoll()
    {
        _poll?.Dispose();
        _poll = _scheduler.After(PrivacyPoll, () =>
        {
            _poll = null;
            if (_finished || Step != SetupStep.Microphone) return;
            var allowed = _privacy.IsDesktopAccessAllowed();
            if (IsBlocked && allowed)
            {
                IsBlocked = false;
                ReloadMics();
                StartMeters();
            }
            else if (!IsBlocked && !allowed)
            {
                IsBlocked = true;
                StopMeters();
            }
            EvaluateStatus();
            SchedulePoll();
        });
    }

    // Try it internals

    private void BeginPractice()
    {
        _practicing = true;
        _hasFinal = false;
        _retryHint = false;
        _awaitingResult = false;
        PracticeText = "";
        _practice.Begin(OnText);
        CardChanged();
    }

    private void OnText(string text, bool isFinal)
    {
        if (!_practicing) return;
        text ??= "";
        if (!isFinal)
        {
            PracticeText = text;
            _partialThisTake = text.Length > 0;
        }
        else if (text.Length > 0)
        {
            PracticeText = text;
            _hasFinal = true;
            _retryHint = false;
            _awaitingResult = false;
        }
        else
        {
            // An empty final: the take produced nothing.
            PracticeText = "";
            _retryHint = true;
            _awaitingResult = false;
        }
        CardChanged();
    }

    private void OnPhase(DictationPhase phase)
    {
        if (!_practicing) return;
        IsRecording = phase == DictationPhase.Recording;
        switch (phase)
        {
            case DictationPhase.Recording:
                _awaitingResult = true;
                _partialThisTake = false;
                _retryHint = false;
                _wave.Reset();
                break;
            case DictationPhase.Idle when _awaitingResult:
                // Back to Idle with no final text (the recorder's Stop failed, or the transcription did): drop a
                // partial so it doesn't look final. A silent take showed nothing, so nothing changes.
                _awaitingResult = false;
                if (_partialThisTake)
                {
                    PracticeText = "";
                    _retryHint = true;
                }
                break;
        }
        WaveOpacities = TryItWave.Opacities(_wave.Columns, IsRecording);
        CardChanged();
    }

    private void OnLevel(float rms)
    {
        if (!_practicing || !IsRecording) return;
        _wave.Push(rms);
        WaveOpacities = TryItWave.Opacities(_wave.Columns, live: true);
    }

    private void CardChanged()
    {
        OnPropertyChanged(nameof(CardKind));
        OnPropertyChanged(nameof(CardText));
        OnPropertyChanged(nameof(DoneReady));
    }
}
