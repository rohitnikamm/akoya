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
/// Decides setup's microphone status from the level steps the rows' meters report. Akoya never moves the highlight
/// itself; this only says whether the highlighted mic is the one hearing the user.
/// </summary>
public static class MicStatus
{
    /// <summary>A step at or above this is speech (the silence gate is <c>SilenceMaxStep</c> = 2).</summary>
    public const int SpeechStep = 3;

    /// <summary>The highlighted mic heard speech this recently: "Hearing you".</summary>
    public static readonly TimeSpan HearingWindow = TimeSpan.FromSeconds(0.5);

    /// <summary>The highlighted mic stayed quiet this long while another mic heard speech: "isn't hearing you".</summary>
    public static readonly TimeSpan NotHearingAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// <paramref name="recent"/> holds, per mic id, its latest speech reading (step ≥ <see cref="SpeechStep"/>) and
    /// when it arrived. A mic that has not heard speech since it was highlighted has a quiet entry (step ≤ 2) stamped
    /// when listening to it began, so its quiet time is measured from then; other mics without speech have no entry.
    /// <list type="bullet">
    /// <item><see cref="MicStatusKind.HearingYou"/> when the highlighted mic's step is ≥ 3 and at most 0.5 s old.</item>
    /// <item><see cref="MicStatusKind.NotHearing"/> when the highlighted mic has been quiet (≤ 2) for at least 3 s
    /// and another mic reached ≥ 3 within that quiet time (in the last 3 s).</item>
    /// <item>Otherwise <see cref="MicStatusKind.None"/>, including when the highlighted mic has no entry.</item>
    /// </list>
    /// </summary>
    public static MicStatusKind Evaluate(
        string highlightedId, IReadOnlyDictionary<string, (int step, DateTimeOffset at)> recent, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(recent);
        if (string.IsNullOrEmpty(highlightedId) || !recent.TryGetValue(highlightedId, out var mine))
            return MicStatusKind.None;

        if (mine.step >= SpeechStep && now - mine.at <= HearingWindow)
            return MicStatusKind.HearingYou;

        // Quiet since its last speech (or since listening began, for a quiet entry).
        if (now - mine.at < NotHearingAfter)
            return MicStatusKind.None;

        foreach (var (id, other) in recent)
        {
            if (string.Equals(id, highlightedId, StringComparison.Ordinal)) continue;
            if (other.step >= SpeechStep && other.at > mine.at && now - other.at <= NotHearingAfter)
                return MicStatusKind.NotHearing;
        }
        return MicStatusKind.None;
    }
}
