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

    [Fact]
    public void I2_M8_LoadFailure_DiscardsTheModel_SaysSpeechModelFailed_AndRetryDownloadsAgain()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Model.LoadError = new InvalidOperationException("Protobuf parsing failed");
            var host = rig.Start(waitForModel: false);
            UiThread.PumpUntil(() => rig.Tray.Model == ModelState.Failed);

            Assert.Equal(1, rig.Model.Discards);
            Assert.True(rig.Tray.ModelLoadFailed); // "Speech model failed", not "Model download failed"

            rig.Model.LoadError = null;
            rig.Tray.Commands!.Retry();
            UiThread.PumpUntil(() => host.ModelState == ModelState.Ready);
            Assert.Equal(2, rig.Model.Ensures);
            Assert.Equal(2, rig.Model.Loads);
            Assert.False(rig.Tray.ModelLoadFailed);
        });
    }

    [Fact]
    public void M8_DownloadFailure_KeepsModelDownloadFailed_AndDiscardsNothing()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Model.Ensure = _ => Task.FromException(new System.Net.Http.HttpRequestException("offline"));
            rig.Start(waitForModel: false);
            UiThread.PumpUntil(() => rig.Tray.Model == ModelState.Failed);

            Assert.False(rig.Tray.ModelLoadFailed);
            Assert.Equal(0, rig.Model.Discards);
        });
    }

    [Fact]
    public void M2_Quit_WaitsForAPasteAndItsClipboardRestoreToFinish()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var paste = new TaskCompletionSource<InsertResult>();
            rig.Inserter.Pending = paste.Task; // the paste (and the clipboard restore) is still running
            var host = rig.Start();
            Dictate(rig, host, until: DictationPhase.Transcribing);
            UiThread.PumpUntil(() => rig.Inserter.Inserted.Count == 1);

            rig.Tray.Commands!.Quit();
            Assert.Equal(0, rig.Shutdowns);

            paste.SetResult(InsertResult.Pasted);
            UiThread.PumpUntil(() => host.Coordinator.Phase == DictationPhase.Pasted);
            rig.Scheduler.Advance(TimeSpan.Zero);
            Assert.Equal(1, rig.Shutdowns);
            rig.Scheduler.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(1, rig.Shutdowns); // once
        });
    }

    [Fact]
    public void M2_Quit_WhileRecording_DropsTheRecordingAndQuitsAtOnce()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            rig.Hook.Fire(HotkeyEvent.Pressed);

            rig.Tray.Commands!.Quit();

            Assert.Equal(1, rig.Recorder.Cancels);
            Assert.Empty(rig.Inserter.Inserted);
            Assert.Equal(1, rig.Shutdowns);
            Assert.Equal(DictationPhase.Idle, host.Coordinator.Phase);
        });
    }

    [Fact]
    public void M6_Suspend_CancelsALatchedRecording_ResumeReinstallsTheHook()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            rig.Hook.Fire(HotkeyEvent.Pressed);
            rig.Recorder.RaiseLevel(Speech);
            rig.Hook.Fire(HotkeyEvent.Released); // a tap: latched
            Assert.Equal(DictationPhase.Recording, host.Coordinator.Phase);

            rig.Session.Suspend();
            Assert.Equal(DictationPhase.Idle, host.Coordinator.Phase);
            Assert.Equal(1, rig.Recorder.Cancels);
            Assert.Empty(rig.Inserter.Inserted);
            var resets = rig.Hook.Resets;

            rig.Session.Resume();
            Assert.Equal(1, rig.Hook.Reinstalls);
            Assert.True(rig.Hook.Resets > resets);
        });
    }

    private static void Dictate(HostRig rig, AppHost host, DictationPhase until = DictationPhase.Pasted)
    {
        rig.Hook.Fire(HotkeyEvent.Pressed);
        rig.Recorder.RaiseLevel(Speech);
        rig.Scheduler.Advance(TimeSpan.FromSeconds(0.5));
        rig.Hook.Fire(HotkeyEvent.Released);
        UiThread.PumpUntil(() => host.Coordinator.Phase == until);
    }
}
