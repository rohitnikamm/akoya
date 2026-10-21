using System.Diagnostics;

namespace Akoya.App.Platform;

/// <summary>
/// The app's one clock for durations: monotonic (Stopwatch), so a wall-clock change (time sync, DST, the user
/// setting the time) never stretches a timer, the hold threshold or the pill's elapsed time. The value is only
/// meaningful relative to another reading; it counts from <see cref="DateTimeOffset.UnixEpoch"/>.
/// <see cref="DispatcherScheduler.Now"/> and the keyboard hook's event stamps both read it.
/// </summary>
public static class MonotonicClock
{
    public static DateTimeOffset Now => DateTimeOffset.UnixEpoch + Stopwatch.GetElapsedTime(0);
}
