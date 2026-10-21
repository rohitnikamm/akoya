using System.ComponentModel;
using System.Runtime.CompilerServices;
using Akoya.Core.Dictation;
using Akoya.Core.Settings;
using Akoya.Core.Ui;

namespace Akoya.App.Pill;

/// <summary>What the pill shows.</summary>
public enum PillMode
{
    /// <summary>Live waveform and the running timer.</summary>
    Listening,

    /// <summary>The scanner sweeps the dots; the timer is frozen.</summary>
    Transcribing,

    /// <summary>Blue check and "Pasted".</summary>
    Pasted,

    /// <summary>Blue check and "Copied. Press Ctrl+V" (elevated foreground app).</summary>
    Copied,

    /// <summary>A message: red with an alert badge (<see cref="PillViewModel.IsRed"/>), or blue without one.</summary>
    Alert,
}

/// <summary>The waveform's colour (Red while recording, see <see cref="PillViewModel.RedTint"/>).</summary>
public enum PillTint
{
    /// <summary>White dots or bars, the blue-white scanner.</summary>
    Normal,

    /// <summary>Listening with Red while recording on: a red waveform (#FF6B6B); the rim glow stays blue.</summary>
    Red,

    /// <summary>Transcribing with Red while recording on: the scanner's lit columns are amber (#FBBF24).</summary>
    Amber,
}

/// <summary>
/// State of the pill. <see cref="Apply"/> maps a <see cref="DictationPhase"/>; while listening, <see cref="SetLevels"/>
/// and <see cref="SetElapsed"/> feed the waveform and timer, and while transcribing <see cref="Tick"/> drives the scanner.
/// <see cref="SetOptions"/> applies the tray's Pill options (look and Red while recording), at once if on screen.
/// Call it on the UI thread.
/// </summary>
public sealed class PillViewModel : INotifyPropertyChanged
{
    public const int Columns = 23;

    private PillMode _mode = PillMode.Listening;
    private WaveLook _look = WaveLook.Pixel;
    private bool _redTint;
    private int[] _steps = Quiet();
    private double _scanSeconds;
    private PillBar[]? _bars;
    private PillTint _tint;
    private double[] _opacities = PillDots.Listening(Quiet());
    private string _timerText = "0:00";
    private string _alertText = "";
    private bool _isRed;
    private bool _isActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PillMode Mode
    {
        get => _mode;
        private set => Set(ref _mode, value);
    }

    /// <summary>Dot opacities, column-major (<see cref="Columns"/> × <see cref="PillDots.Rows"/>); a new array on every change.</summary>
    public double[] Opacities
    {
        get => _opacities;
        private set
        {
            _opacities = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The waveform's look (the tray's Pill, Waveform option).</summary>
    public WaveLook Look => _look;

    /// <summary>Red while recording (the tray's Pill option).</summary>
    public bool RedTint => _redTint;

    /// <summary>The Solid waveform's bars, one per column; null in the Pixel look. A new array on every change.</summary>
    public PillBar[]? Bars
    {
        get => _bars;
        private set
        {
            if (value is null && _bars is null) return;
            _bars = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The waveform's colour.</summary>
    public PillTint Tint
    {
        get => _tint;
        private set => Set(ref _tint, value);
    }

    /// <summary>Recording time as m:ss, e.g. "0:04".</summary>
    public string TimerText
    {
        get => _timerText;
        private set => Set(ref _timerText, value);
    }

    /// <summary>The words beside the badge in Pasted, Copied and Alert modes.</summary>
    public string AlertText
    {
        get => _alertText;
        private set => Set(ref _alertText, value);
    }

    /// <summary>Red alert styling (red rim glow and badge) instead of blue.</summary>
    public bool IsRed
    {
        get => _isRed;
        private set => Set(ref _isRed, value);
    }

    /// <summary>False once the phase is <see cref="DictationPhase.Idle"/>: the pill should fade out.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => Set(ref _isActive, value);
    }

    public void Apply(DictationPhase phase)
    {
        IsActive = phase != DictationPhase.Idle;
        switch (phase)
        {
            case DictationPhase.Idle:
                return; // keep showing the last state while the pill fades out
            case DictationPhase.Recording:
                TimerText = "0:00";
                _steps = Quiet();
                Show(PillMode.Listening, "", red: false);
                break;
            case DictationPhase.Transcribing:
                _scanSeconds = 0;
                Show(PillMode.Transcribing, "", red: false);
                break;
            case DictationPhase.Pasted:
                Show(PillMode.Pasted, "Pasted", red: false);
                break;
            case DictationPhase.Copied:
                Show(PillMode.Copied, "Copied. Press Ctrl+V", red: false);
                break;
            case DictationPhase.MicMissing:
                Show(PillMode.Alert, "Microphone not detected", red: true);
                break;
            case DictationPhase.MicBlocked:
                Show(PillMode.Alert, "Turn on your mic in Settings", red: true);
                break;
            case DictationPhase.ModelFailed:
                Show(PillMode.Alert, "Speech model failed", red: true);
                break;
            case DictationPhase.PasteFailed:
                Show(PillMode.Alert, "Couldn't paste", red: true);
                break;
            case DictationPhase.ModelNotReady:
                Show(PillMode.Alert, "Getting the speech model ready…", red: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }
    }

    /// <summary>Live level steps (1..8) per column while listening; the list is copied, so a live buffer is fine.</summary>
    public void SetLevels(IReadOnlyList<int> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (Mode != PillMode.Listening) return;
        _steps = steps.ToArray();
        DrawWaveform();
    }

    /// <summary>The tray's Pill options; redraws a waveform on screen at once.</summary>
    public void SetOptions(WaveLook look, bool redTint)
    {
        if (look == _look && redTint == _redTint) return;
        _look = look;
        _redTint = redTint;
        OnPropertyChanged(nameof(Look));
        OnPropertyChanged(nameof(RedTint));
        DrawWaveform();
    }

    /// <summary>Recording time so far; ignored once transcribing (the timer freezes).</summary>
    public void SetElapsed(TimeSpan elapsed)
    {
        if (Mode != PillMode.Listening) return;
        var seconds = Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds));
        TimerText = $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>Seconds since transcribing began; moves the scanner.</summary>
    public void Tick(double transcribingSeconds)
    {
        if (Mode != PillMode.Transcribing) return;
        _scanSeconds = Math.Max(0, transcribingSeconds);
        DrawWaveform();
    }

    private void Show(PillMode mode, string text, bool red)
    {
        AlertText = text;
        IsRed = red;
        Mode = mode;
        DrawWaveform();
    }

    /// <summary>The waveform for the mode, look and tint (listening levels or the scanner); other modes keep the last one.</summary>
    private void DrawWaveform()
    {
        var solid = _look == WaveLook.Solid;
        switch (Mode)
        {
            case PillMode.Listening:
                Opacities = PillDots.Listening(_steps);
                Bars = solid ? PillDots.SolidListening(_steps) : null;
                Tint = _redTint ? PillTint.Red : PillTint.Normal;
                break;
            case PillMode.Transcribing:
                Opacities = PillDots.Scanner(_scanSeconds, Columns);
                Bars = solid ? PillDots.SolidScanner(_scanSeconds, Columns) : null;
                Tint = _redTint ? PillTint.Amber : PillTint.Normal;
                break;
            default:
                Tint = PillTint.Normal;
                break;
        }
    }

    private static int[] Quiet() => Enumerable.Repeat(1, Columns).ToArray();

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
