namespace Akoya.Core.Dictation;

/// <summary>
/// Decides when the practice-mode live preview may re-transcribe the audio captured so far.
/// Port of the Mac's LivePreviewScheduler.swift.
/// </summary>
/// <remarks>
/// A pass runs only when no other pass is in flight, the recording has risen above the silence
/// gate (<see cref="DictationCoordinator.SilenceMaxStep"/>) and holds at least
/// <see cref="MinAudio"/>, and <see cref="Interval"/> has passed since the last pass started.
/// </remarks>
public sealed class LivePreviewScheduler
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan DefaultMinAudio = TimeSpan.FromMilliseconds(500);

    private DateTimeOffset? _lastRun;

    public LivePreviewScheduler()
        : this(DefaultInterval, DefaultMinAudio) { }

    public LivePreviewScheduler(TimeSpan interval, TimeSpan minAudio)
    {
        Interval = interval;
        MinAudio = minAudio;
    }

    public TimeSpan Interval { get; }

    public TimeSpan MinAudio { get; }

    /// <summary>
    /// Returns true, and starts the interval, when a pass should run now. A tick that is skipped
    /// (in flight, silent, too short) does not consume the interval.
    /// </summary>
    public bool ShouldRun(DateTimeOffset now, TimeSpan audio, int loudestStep, bool inFlight)
    {
        if (inFlight || loudestStep <= DictationCoordinator.SilenceMaxStep || audio < MinAudio)
            return false;
        if (_lastRun is { } last && now - last < Interval)
            return false;
        _lastRun = now;
        return true;
    }
}
