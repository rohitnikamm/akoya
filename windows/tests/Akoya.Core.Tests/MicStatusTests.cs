using Akoya.Core.Mics;
using Xunit;

namespace Akoya.Core.Tests;

public class MicStatusTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 9, 41, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    private static Dictionary<string, (int step, DateTimeOffset at)> Recent(params (string id, int step, double at)[] entries) =>
        entries.ToDictionary(e => e.id, e => (e.step, At(e.at)));

    [Fact]
    public void BeforeAnySpeech_IsNone()
    {
        // Listening to the highlighted mic began at 0; nobody has said anything.
        var recent = Recent(("buds", 1, 0));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", recent, At(0)));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", recent, At(10)));
    }

    [Fact]
    public void HighlightedWithoutEntry_IsNone()
    {
        var recent = Recent(("laptop", 6, 5));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", recent, At(5)));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("", recent, At(5)));
    }

    [Fact]
    public void HighlightedHearsSpeech_IsHearingYou()
    {
        var recent = Recent(("laptop", 5, 2), ("cam", 3, 2));
        Assert.Equal(MicStatusKind.HearingYou, MicStatus.Evaluate("laptop", recent, At(2)));
        Assert.Equal(MicStatusKind.HearingYou, MicStatus.Evaluate("laptop", recent, At(2.5)));
    }

    [Fact]
    public void Hearing_ExpiresAfterHalfASecond()
    {
        var recent = Recent(("laptop", 5, 2));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("laptop", recent, At(2.51)));
    }

    [Fact]
    public void QuietStepOnHighlighted_IsNotHearingYou()
    {
        // Step 2 is still the silence gate, not speech.
        var recent = Recent(("laptop", 2, 2));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("laptop", recent, At(2)));
    }

    [Fact]
    public void HighlightedQuiet_OtherHearsSpeech_IsNotHearingAfterThreeSeconds()
    {
        // Galaxy Buds highlighted at 0; the laptop mic hears the user.
        var early = Recent(("buds", 1, 0), ("laptop", 6, 2.9));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", early, At(2.9)));

        var later = Recent(("buds", 1, 0), ("laptop", 6, 3));
        Assert.Equal(MicStatusKind.NotHearing, MicStatus.Evaluate("buds", later, At(3)));
    }

    [Fact]
    public void QuietTime_CountsFromTheHighlightedMicsLastSpeech()
    {
        // The buds heard speech at 4, then went quiet while the laptop kept hearing it.
        var recent = Recent(("buds", 5, 4), ("laptop", 6, 6.9));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", recent, At(6.9)));

        recent = Recent(("buds", 5, 4), ("laptop", 6, 7));
        Assert.Equal(MicStatusKind.NotHearing, MicStatus.Evaluate("buds", recent, At(7)));
    }

    [Fact]
    public void NotHearing_NeverFiresWhenNoOtherMicHearsSpeech()
    {
        var recent = Recent(("buds", 1, 0), ("laptop", 2, 1), ("cam", 1, 1));
        foreach (var seconds in new[] { 0.0, 3, 5, 30, 300 })
            Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", recent, At(seconds)));
    }

    [Fact]
    public void NotHearing_NeedsTheOtherMicsSpeechDuringTheQuietTime()
    {
        // The laptop heard speech before the buds were highlighted (at 5), not since.
        var recent = Recent(("buds", 1, 5), ("laptop", 6, 4));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", recent, At(8)));
    }

    [Fact]
    public void NotHearing_EndsThreeSecondsAfterTheOtherMicsLastSpeech()
    {
        var recent = Recent(("buds", 1, 0), ("laptop", 6, 4));
        Assert.Equal(MicStatusKind.NotHearing, MicStatus.Evaluate("buds", recent, At(7)));
        Assert.Equal(MicStatusKind.None, MicStatus.Evaluate("buds", recent, At(7.01)));
    }
}
