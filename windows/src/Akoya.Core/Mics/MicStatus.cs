namespace Akoya.Core.Mics;

/// <summary>The status chip under setup's microphone list.</summary>
public enum MicStatusKind
{
    /// <summary>Nothing to say yet: no chip (before the user speaks).</summary>
    None,

    /// <summary>Green "Hearing you": the highlighted mic hears speech.</summary>
    HearingYou,

    /// <summary>Amber "&lt;name&gt; isn't hearing you": the highlighted mic stays quiet while another one hears speech.</summary>
    NotHearing,
}

/// <summary>
/// What one mic's meter has shown: when it last heard speech (step ≥ <see cref="MicStatus.SpeechStep"/>), when its
/// current run of speech began (speech readings no more than <see cref="MicStatus.RunGap"/> apart), and whether it is
/// a virtual or loopback input from the More group, which never counts as "another mic hears you".
/// </summary>
public readonly record struct MicActivity(DateTimeOffset? LastSpeech, DateTimeOffset? RunStart, bool InMore);

/// <summary>
/// Decides setup's microphone status from the level steps the rows' meters report. Akoya never moves the highlight
/// itself; this only says whether the highlighted mic is the one hearing the user.
/// </summary>
public static class MicStatus
{
    /// <summary>A step at or above this is speech (the silence gate is <c>SilenceMaxStep</c> = 2).</summary>
    public const int SpeechStep = 3;

    /// <summary>The highlighted mic heard speech this recently: "Hearing you".</summary>
    public static readonly TimeSpan HearingWindow = TimeSpan.FromSeconds(0.5);

    /// <summary>Another mic has been hearing speech this long while the highlighted one stayed quiet: "isn't hearing you".</summary>
    public static readonly TimeSpan NotHearingAfter = TimeSpan.FromSeconds(3);

    /// <summary>Speech readings at most this far apart belong to one run (the pauses between words).</summary>
    public static readonly TimeSpan RunGap = TimeSpan.FromSeconds(0.6);

    /// <summary>
    /// <list type="bullet">
    /// <item><see cref="MicStatusKind.HearingYou"/> when the highlighted mic heard speech in the last 0.5 s.</item>
    /// <item><see cref="MicStatusKind.NotHearing"/> when another mic, not in More, is in a run of speech that began at
    /// least 3 s ago, and the highlighted mic heard no speech since that run began. Measuring from the other mic's run
    /// (not from the highlighted mic's last reading) means the first word after a pause can't flip the chip amber just
    /// because the other mic's level arrived a moment sooner.</item>
    /// <item>Otherwise <see cref="MicStatusKind.None"/>.</item>
    /// </list>
    /// </summary>
    public static MicStatusKind Evaluate(
        string highlightedId, IReadOnlyDictionary<string, MicActivity> activity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (string.IsNullOrEmpty(highlightedId)) return MicStatusKind.None;

        activity.TryGetValue(highlightedId, out var mine);
        if (mine.LastSpeech is { } heard && now - heard <= HearingWindow)
            return MicStatusKind.HearingYou;

        foreach (var (id, other) in activity)
        {
            if (string.Equals(id, highlightedId, StringComparison.Ordinal) || other.InMore) continue;
            if (other.LastSpeech is not { } last || other.RunStart is not { } start) continue;
            if (now - last > RunGap || now - start < NotHearingAfter) continue; // not speaking now, or not for long
            if (mine.LastSpeech is { } own && own >= start) continue; // the highlighted mic heard part of this run
            return MicStatusKind.NotHearing;
        }
        return MicStatusKind.None;
    }
}

/// <summary>
/// Collects the meters' steps into <see cref="MicActivity"/> per mic and turns <see cref="MicStatus.Evaluate"/> into the
/// chip setup shows: once green or amber it stays (no blinking between words) until the other status replaces it,
/// <see cref="ClearShown"/> (the highlight moved) or <see cref="Reset"/> (meters restarted), or no mic outside More
/// has heard speech for <see cref="MicStatus.NotHearingAfter"/>.
/// </summary>
public sealed class MicStatusTracker
{
    private readonly Dictionary<string, MicActivity> _activity = new(StringComparer.Ordinal);
    private DateTimeOffset? _lastSpeechAnywhere;

    public MicStatusKind Shown { get; private set; }

    public IReadOnlyDictionary<string, MicActivity> Activity => _activity;

    /// <summary>A meter reading. <paramref name="inMore"/>: a virtual or loopback input (never "another mic hears you").</summary>
    public void Record(string id, int step, bool inMore, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(id);
        _activity.TryGetValue(id, out var current);
        if (step < MicStatus.SpeechStep)
        {
            _activity[id] = current with { InMore = inMore };
            return;
        }
        var continues = current.LastSpeech is { } last && at - last <= MicStatus.RunGap;
        _activity[id] = new MicActivity(at, continues ? current.RunStart : at, inMore);
        if (!inMore) _lastSpeechAnywhere = at;
    }

    /// <summary>Re-evaluates for <paramref name="highlightedId"/> and returns the chip to show.</summary>
    public MicStatusKind Update(string? highlightedId, DateTimeOffset now)
    {
        var kind = MicStatus.Evaluate(highlightedId ?? "", _activity, now);
        if (kind != MicStatusKind.None)
        {
            Shown = kind;
        }
        else
        {
            var own = highlightedId is not null && _activity.TryGetValue(highlightedId, out var mine) ? mine.LastSpeech : null;
            var latest = Max(_lastSpeechAnywhere, own);
            if (latest is not { } at || now - at >= MicStatus.NotHearingAfter) Shown = MicStatusKind.None;
        }
        return Shown;
    }

    /// <summary>The highlight moved: show nothing until the next evaluation decides.</summary>
    public void ClearShown() => Shown = MicStatusKind.None;

    /// <summary>The meters restarted: forget everything.</summary>
    public void Reset()
    {
        _activity.Clear();
        _lastSpeechAnywhere = null;
        Shown = MicStatusKind.None;
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;
}
