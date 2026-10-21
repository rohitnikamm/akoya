using Akoya.Core.Mics;
using Xunit;

namespace Akoya.Core.Tests;

public class MicStatusTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 9, 41, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    /// <summary>Feeds 40 ms meter windows from <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
    private static void Feed(MicStatusTracker tracker, double from, double to, Func<double, (string id, int step, bool more)[]> steps,
        Action<double>? after = null)
    {
        for (var i = 0; from + i * 0.04 < to - 1e-9; i++)
        {
            var t = from + i * 0.04;
            foreach (var (id, step, more) in steps(t)) tracker.Record(id, step, more, At(t));
            after?.Invoke(t);
        }
    }

    private static MicActivity Speech(double last, double runStart, bool more = false) => new(At(last), At(runStart), more);

    [Fact]
    public void BeforeAnySpeech_IsNone()
    {
        var activity = new Dictionary<string, MicActivity> { ["buds"] = default, ["laptop"] = default };
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", activity, At(0)));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", activity, At(10)));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("", activity, At(10)));
    }

    [Fact]
    public void HighlightedHearsSpeech_IsHearingYou_ForHalfASecond()
    {
        var activity = new Dictionary<string, MicActivity> { ["laptop"] = Speech(2, 1) };
        Assert.Equal(MicStatusKind.HearingYou, MicStatus.Evaluate("laptop", activity, At(2)));
        Assert.Equal(MicStatusKind.HearingYou, MicStatus.Evaluate("laptop", activity, At(2.5)));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("laptop", activity, At(2.51)));
    }

    [Fact]
    public void OtherMicSpeakingThreeSeconds_WhileHighlightedQuiet_IsNotHearing()
    {
        var activity = new Dictionary<string, MicActivity> { ["buds"] = default, ["laptop"] = Speech(12.9, 10) };
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", activity, At(12.9)));

        activity["laptop"] = Speech(13, 10);
        Assert.Equal(MicStatusKind.NotHearing, MicStatus.Evaluate("buds", activity, At(13)));
    }

    [Fact]
    public void HighlightedHeardPartOfTheRun_IsNotNotHearing()
    {
        var activity = new Dictionary<string, MicActivity> { ["buds"] = Speech(10.2, 10.2), ["laptop"] = Speech(14, 10) };
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", activity, At(14)));
    }

    [Fact]
    public void OtherMicStoppedSpeaking_IsNotNotHearing()
    {
        var activity = new Dictionary<string, MicActivity> { ["buds"] = default, ["laptop"] = Speech(14, 10) };
        Assert.Equal(MicStatusKind.NotHearing, MicStatus.Evaluate("buds", activity, At(14.6)));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", activity, At(14.61)));
    }

    [Fact]
    public void MoreInputs_NeverCountAsAnotherMicHearingYou()
    {
        var activity = new Dictionary<string, MicActivity> { ["laptop"] = default, ["mix"] = Speech(20, 0, more: true) };
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("laptop", activity, At(20)));
    }

    [Fact]
    public void Tracker_AfterTenSecondsOfSilence_OtherMicFirstBy200ms_NeverNotHearing()
    {
        var tracker = new MicStatusTracker();
        var seen = new List<MicStatusKind>();
        Feed(tracker, 0, 10, _ => [("laptop", 1, false), ("buds", 1, false)], t => seen.Add(tracker.Update("laptop", At(t))));
        // The buds start hearing the user 200 ms before the highlighted laptop mic does, then both keep hearing.
        Feed(tracker, 10, 16, t => [("buds", 6, false), ("laptop", t >= 10.2 ? 6 : 1, false)],
            t => seen.Add(tracker.Update("laptop", At(t))));

        Assert.DoesNotContain(MicStatusKind.NotHearing, seen);
        Assert.Equal(MicStatusKind.HearingYou, seen[^1]);
    }

    [Fact]
    public void Tracker_OtherMicSpeaksThreeSeconds_WhileHighlightedSilent_NotHearing()
    {
        var tracker = new MicStatusTracker();
        Feed(tracker, 0, 10, _ => [("laptop", 1, false), ("buds", 1, false)]);
        var seen = new List<(double t, MicStatusKind kind)>();
        Feed(tracker, 10, 13.2, _ => [("laptop", 7, false), ("buds", 1, false)], t => seen.Add((t, tracker.Update("buds", At(t)))));

        Assert.All(seen.Where(s => s.t < 12.99), s => Assert.Equal(MicStatusKind.None, s.kind));
        Assert.Equal(MicStatusKind.NotHearing, seen[^1].kind);
    }

    [Fact]
    public void Tracker_PausesBetweenWords_KeepTheRun()
    {
        var tracker = new MicStatusTracker();
        // Words with 0.4 s gaps: one run from 0.
        Feed(tracker, 0, 3.2, t => [("laptop", t % 1 < 0.6 ? 6 : 1, false), ("buds", 1, false)]);
        Assert.Equal(MicStatusKind.NotHearing, tracker.Update("buds", At(3.2)));
    }

    [Fact]
    public void Tracker_StereoMixLoud_NobodySpeaking_NoChip()
    {
        var tracker = new MicStatusTracker();
        var seen = new List<MicStatusKind>();
        Feed(tracker, 0, 10, _ => [("laptop", 1, false), ("mix", 6, true)], t => seen.Add(tracker.Update("laptop", At(t))));
        Assert.All(seen, s => Assert.Equal(MicStatusKind.None, s));
    }

    [Fact]
    public void Tracker_StatusOutlivesWordGaps_ButNotThreeSecondsOfSilence()
    {
        var tracker = new MicStatusTracker();
        Feed(tracker, 0, 1, _ => [("laptop", 6, false)]);
        Assert.Equal(MicStatusKind.HearingYou, tracker.Update("laptop", At(1)));

        Feed(tracker, 1, 2.5, _ => [("laptop", 1, false)]);
        Assert.Equal(MicStatusKind.HearingYou, tracker.Update("laptop", At(2.5)));

        Feed(tracker, 2.5, 4.1, _ => [("laptop", 1, false)]);
        Assert.Equal(MicStatusKind.None, tracker.Update("laptop", At(4.1)));
    }

    [Fact]
    public void Tracker_ResetForgetsEverything()
    {
        var tracker = new MicStatusTracker();
        Feed(tracker, 0, 3.2, _ => [("laptop", 7, false), ("buds", 1, false)]);
        Assert.Equal(MicStatusKind.NotHearing, tracker.Update("buds", At(3.2)));

        tracker.Reset();

        Assert.Equal(MicStatusKind.None, tracker.Shown);
        Assert.Empty(tracker.Activity);
        Assert.Equal(MicStatusKind.None, tracker.Update("buds", At(3.2)));
    }
}
