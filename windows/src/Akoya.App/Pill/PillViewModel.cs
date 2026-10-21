using System.ComponentModel;
using System.Runtime.CompilerServices;
using Akoya.Core.Dictation;
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

/// <summary>
/// State of the pill. <see cref="Apply"/> maps a <see cref="DictationPhase"/>; while listening, <see cref="SetLevels"/>
/// and <see cref="SetElapsed"/> feed the waveform and timer, and while transcribing <see cref="Tick"/> drives the scanner.
/// Call it on the UI thread.
/// </summary>
public sealed class PillViewModel : INotifyPropertyChanged
{
    public const int Columns = 23;

    private PillMode _mode = PillMode.Listening;
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
                Opacities = PillDots.Listening(Quiet());
                Show(PillMode.Listening, "", red: false);
                break;
            case DictationPhase.Transcribing:
                Opacities = PillDots.Scanner(0, Columns);
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
        Opacities = PillDots.Listening(steps.ToArray());
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
        Opacities = PillDots.Scanner(Math.Max(0, transcribingSeconds), Columns);
    }

    private void Show(PillMode mode, string text, bool red)
    {
        AlertText = text;
        IsRed = red;
        Mode = mode;
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
