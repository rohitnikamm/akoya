using System.Windows.Threading;
using Akoya.App.Platform;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Xunit;

namespace Akoya.App.Tests;

/// <summary>The final fix round's AppHost behaviour (see the report's item names).</summary>
[Collection(DesktopCollection.Name)]
public class AppHostFinalFixTests
{
    private const float Speech = 0.05f;

    [Fact]
    public void I1_TheHooksStampsDecideTapOrHold_NotWhenTheHostGetsThem()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            var pressedAt = rig.Scheduler.Now;

            rig.Hook.Fire(HotkeyEvent.Pressed, pressedAt);
            rig.Recorder.RaiseLevel(Speech);
            rig.Scheduler.Advance(TimeSpan.FromSeconds(1)); // e.g. the mic took a second to open
            rig.Hook.Fire(HotkeyEvent.Released, pressedAt + TimeSpan.FromMilliseconds(100));

            Assert.Equal(DictationPhase.Recording, host.Coordinator.Phase); // a tap latches
        });
    }

    [Fact]
    public void M4_DispatcherScheduler_UsesTheMonotonicClock()
    {
        UiThread.Run(() =>
        {
            var scheduler = new DispatcherScheduler(Dispatcher.CurrentDispatcher);
            var before = MonotonicClock.Now;
            var now = scheduler.Now;
            var after = MonotonicClock.Now;

            Assert.InRange(now, before, after);
            Assert.True(now.Year < 2000, "not the wall clock");
        });
    }
}
