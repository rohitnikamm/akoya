using Akoya.Core.Dictation;
using Xunit;

namespace Akoya.Core.Tests;

public class LivePreviewSchedulerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int ms) => T0.AddMilliseconds(ms);

    private static TimeSpan Audio(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Defaults_MatchTheMac()
    {
        var scheduler = new LivePreviewScheduler();
        Assert.Equal(TimeSpan.FromMilliseconds(600), scheduler.Interval);
        Assert.Equal(TimeSpan.FromMilliseconds(500), scheduler.MinAudio);
    }

    [Fact]
    public void WaitsForSpeech()
    {
        var scheduler = new LivePreviewScheduler();
        Assert.False(scheduler.ShouldRun(At(0), Audio(2000), loudestStep: 1, inFlight: false));
        Assert.True(scheduler.ShouldRun(At(0), Audio(2000), loudestStep: 2, inFlight: false));
    }

    [Fact]
    public void WaitsForMinAudio()
    {
        var scheduler = new LivePreviewScheduler();
        Assert.False(scheduler.ShouldRun(At(0), Audio(400), loudestStep: 8, inFlight: false));
        Assert.True(scheduler.ShouldRun(At(100), Audio(500), loudestStep: 8, inFlight: false));
    }

    [Fact]
    public void RunsOnInterval()
    {
        var scheduler = new LivePreviewScheduler();
        var first = scheduler.ShouldRun(At(0), Audio(1000), loudestStep: 8, inFlight: false);
        var tooSoon = scheduler.ShouldRun(At(300), Audio(1300), loudestStep: 8, inFlight: false);
        var next = scheduler.ShouldRun(At(610), Audio(1600), loudestStep: 8, inFlight: false);
        Assert.True(first);
        Assert.False(tooSoon);
        Assert.True(next);
    }

    [Fact]
    public void SkipsWhileInFlight()
    {
        var scheduler = new LivePreviewScheduler();
        var busy = scheduler.ShouldRun(At(0), Audio(1000), loudestStep: 8, inFlight: true);
        // A skipped tick does not consume the interval: the next free tick runs immediately.
        var free = scheduler.ShouldRun(At(100), Audio(1100), loudestStep: 8, inFlight: false);
        Assert.False(busy);
        Assert.True(free);
    }
}
