using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Akoya.App.Pill;
using Akoya.App.Platform;
using Akoya.App.Tray;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Akoya.Core.Settings;
using Xunit;

namespace Akoya.App.Tests;

public class AppHostTests
{
    private const float Speech = 0.05f; // about -26 dBFS: well above the silence gate

    [Fact]
    public void Pressed_ShowsListening_ReleasedAfterHalfSecond_TranscribesPastesAndHides()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();

            rig.Hook.Fire(HotkeyEvent.Pressed);

            Assert.True(rig.Pill.Visible);
            Assert.Equal(PillMode.Listening, rig.Pill.Shows[^1].Mode);
            Assert.True(rig.Hook.RecordingActive); // Esc is intercepted while recording
            Assert.Equal(DictationPhase.Recording, rig.Tray.Phases[^1]);

            var quiet = host.Pill.Opacities;
            rig.Recorder.RaiseLevel(Speech);
            Assert.NotEqual(quiet, host.Pill.Opacities); // the waveform follows the level

            rig.Scheduler.Advance(TimeSpan.FromSeconds(0.5));
            Assert.Equal("0:00", host.Pill.TimerText);
            Assert.True(rig.Tray.VoiceLevel > 0);
            rig.Hook.Fire(HotkeyEvent.Released);
            UiThread.PumpUntil(() => host.Coordinator.Phase == DictationPhase.Pasted);

            Assert.Equal(
                new[] { PillMode.Listening, PillMode.Transcribing, PillMode.Pasted },
                rig.Pill.Shows.Select(s => s.Mode).Distinct());
            Assert.Equal(new[] { "hello world " }, rig.Inserter.Inserted);
            Assert.Equal("hello world", rig.Tray.LastDictation);
            Assert.False(rig.Hook.RecordingActive);

            rig.Scheduler.Advance(DictationCoordinator.PastedDisplay);
            Assert.Equal(DictationPhase.Idle, host.Coordinator.Phase);
            Assert.False(rig.Pill.Visible);
            Assert.DoesNotContain("hello world", rig.Log.Text); // never transcripts in the log
        });
    }

    [Fact]
    public void TimerAndScanner_FollowTheScheduler()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();

            rig.Hook.Fire(HotkeyEvent.Pressed);
            rig.Recorder.RaiseLevel(Speech);
            rig.Scheduler.Advance(TimeSpan.FromSeconds(4.2));

            Assert.Equal("0:04", host.Pill.TimerText);
        });
    }

    [Fact]
    public void NewRecording_WhileTheLastResultShows_MovesThePillToTheForegroundMonitor()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            Dictate(rig, host);
            Assert.True(rig.Pill.Visible); // "Pasted" still showing

            rig.Hook.Fire(HotkeyEvent.Pressed);

            Assert.Equal(1, rig.Pill.Retargets);
        });
    }

    [Fact]
    public void MicMissing_ShowsRedAlertAndYellowTrayRow()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Settings.Stored = rig.Settings.Stored with { MicrophoneId = "headset-gone" };
            var host = rig.Start();
            Assert.True(rig.Tray.MicMissing); // already yellow at launch

            rig.Hook.Fire(HotkeyEvent.Pressed);

            Assert.Equal(DictationPhase.MicMissing, host.Coordinator.Phase);
            Assert.Equal((PillMode.Alert, "Microphone not detected", true), rig.Pill.Shows[^1]);
            Assert.True(rig.Tray.MicMissing);
            Assert.Equal("headset-gone", rig.Tray.SavedMicId);
            Assert.Equal(0, rig.Recorder.Starts); // nothing recorded

            rig.Scheduler.Advance(DictationCoordinator.AlertDisplay);
            Assert.False(rig.Pill.Visible);

            // Plugging it back in clears the row.
            rig.Catalog.Endpoints.Add(new("headset-gone", "Headset (HyperX Cloud III)", "HyperX Cloud III",
                Akoya.Core.Mics.EndpointFormFactor.Headset, "USB", true, false));
            rig.Catalog.RaiseChanged();
            Assert.False(rig.Tray.MicMissing);
            Assert.Equal("HyperX Cloud III", rig.Tray.SavedMicName);
        });
    }

    [Fact]
    public void RealTray_FollowsDictation_AndItsControlsChangeTheApp()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Settings.Stored = rig.Settings.Stored with { MicrophoneId = "headset-gone" };
            rig.Updates.Next = () => (true, null);
            TrayPresenter? tray = null;
            rig.TrayFactory = commands => tray = new TrayPresenter(commands, rig.Scheduler);
            var host = rig.Start();
            var vm = tray!.Model;
            UiThread.PumpUntil(() => vm.UpdateReady);

            // The saved mic is gone: yellow row and status.
            Assert.True(vm.MicMissing);
            Assert.Equal("Microphone not detected", vm.StatusText);
            Assert.Equal("System default (This PC's microphone)", vm.MicOptions[0].Label);

            // Picking System default from the panel saves it and clears the row.
            vm.SelectMic.Execute(null);
            Assert.Null(rig.Settings.Stored.MicrophoneId);
            Assert.False(vm.MicMissing);
            Assert.Equal("Ready", vm.StatusText);

            rig.Hook.Fire(HotkeyEvent.Pressed);
            Assert.Equal("Listening", vm.StatusText);
            Assert.True(vm.IsListening);
            rig.Recorder.RaiseLevel(Speech);
            rig.Scheduler.Advance(TimeSpan.FromSeconds(0.5));
            Assert.True(vm.GlowStrength > 0);
            rig.Hook.Fire(HotkeyEvent.Released);
            UiThread.PumpUntil(() => host.Coordinator.Phase == DictationPhase.Pasted);
            Assert.Equal("hello world", vm.LastDictation);
            Assert.Equal("Ready", vm.StatusText);

            vm.Copy.Execute(null);
            Assert.Equal(new[] { "hello world" }, rig.Copied);

            vm.SelectHotkey.Execute(HotkeyKey.RightCtrl);
            Assert.Equal(HotkeyKey.RightCtrl, rig.Settings.Stored.Hotkey);
            Assert.Equal(HotkeyKey.RightCtrl, rig.Hook.Keys[^1]);
            Assert.Equal("Right Ctrl", vm.HotkeyLabel);

            vm.LaunchAtStartup = true;
            Assert.True(rig.Startup.Enabled);
            Assert.True(rig.Settings.Stored.LaunchAtStartup);

            host.ShowPanel();
            UiThread.Pump(TimeSpan.FromMilliseconds(200));
            Assert.True(tray.Icon.Panel.IsVisible);

            vm.Quit.Execute(null);
            Assert.Equal(1, rig.Shutdowns);
        });
    }

    [Fact]
    public void MicBlocked_ShowsTheRedPillAndTheTrayStatus()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.MicAllowed = false;
            rig.Start();
            Assert.True(rig.Tray.MicBlocked);
        });
    }

    [Fact]
    public void FirstRun_ShowsSetup_FinishMarksDoneAndPastesAgain()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Settings.Stored = new AkoyaSettings(); // SetupDone false
            var host = rig.Start();

            Assert.True(rig.Setup.Shown);
            Assert.Same(host.Coordinator, rig.Setup.Context!.Coordinator);
            Assert.Equal(HotkeyKey.RightAlt, rig.Setup.Hotkey);
            Assert.Equal(ModelState.Ready, rig.Setup.Model);

            // Try it: a practice dictation never shows the pill and is never pasted.
            var practice = new List<(string, bool)>();
            host.Coordinator.Destination = new Destination.Practice((text, final) => practice.Add((text, final)));
            rig.Hook.Fire(HotkeyEvent.Pressed);
            Assert.False(rig.Pill.Visible);
            rig.Recorder.RaiseLevel(Speech);
            rig.Scheduler.Advance(TimeSpan.FromSeconds(0.5));
            rig.Hook.Fire(HotkeyEvent.Released);
            UiThread.PumpUntil(() => practice.Any(p => p.Item2));
            Assert.Empty(rig.Inserter.Inserted);
            Assert.Contains(DictationPhase.Recording, rig.Setup.Phases);

            rig.Setup.ChooseMic("mic-1");
            Assert.Equal("mic-1", rig.Settings.Stored.MicrophoneId);

            // A practice recording still running when setup closes is dropped, not pasted.
            rig.Hook.Fire(HotkeyEvent.Pressed);
            rig.Setup.Finish();
            Assert.Equal(DictationPhase.Idle, host.Coordinator.Phase);
            Assert.IsType<Destination.Paste>(host.Coordinator.Destination);
            Assert.True(rig.Settings.Stored.SetupDone);
            Assert.True(rig.Setup.Disposed);
            Assert.False(host.SetupOpen);
        });
    }

    [Fact]
    public void SetupDone_WithTheModelInstalled_DoesNotShowSetup()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Start();
            Assert.Equal(0, rig.SetupsCreated);
        });
    }

    [Fact]
    public void SetupDone_ButTheModelMissing_ShowsSetup()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Model.IsInstalled = false;
            rig.Start();
            Assert.Equal(1, rig.SetupsCreated);
            Assert.True(rig.Setup.Shown);
        });
    }

    [Fact]
    public void SetupRetry_DownloadsTheModelAgain()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Settings.Stored = new AkoyaSettings();
            rig.Model.Ensure = _ => Task.FromException(new HttpRequestException("offline"));
            var host = rig.Start(waitForModel: false);
            UiThread.PumpUntil(() => rig.Setup.Model == ModelState.Failed);

            rig.Model.Ensure = _ => Task.CompletedTask;
            rig.Setup.RetryModel();

            UiThread.PumpUntil(() => host.ModelState == ModelState.Ready);
            Assert.Equal(ModelState.Ready, rig.Setup.Model);
        });
    }

    [Fact]
    public void ModelLoading_PressSaysNotReady_ProgressReachesTheTray_FailureOffersRetry()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var download = new TaskCompletionSource();
            IProgress<double>? reporter = null;
            rig.Model.Ensure = progress =>
            {
                reporter = progress;
                return download.Task;
            };
            var host = rig.Start(waitForModel: false);

            Assert.Equal(ModelState.Loading, rig.Tray.Model);
            rig.Hook.Fire(HotkeyEvent.Pressed);
            Assert.Equal((PillMode.Alert, "Getting the speech model ready…", false), rig.Pill.Shows[^1]);
            Assert.Equal(0, rig.Recorder.Starts);

            reporter!.Report(0.62);
            UiThread.PumpUntil(() => rig.Tray.Progress is not null);
            Assert.Equal(0.62, rig.Tray.Progress!.Value, 3);

            download.SetException(new ModelDownloadFailure());
            UiThread.PumpUntil(() => rig.Tray.Model == ModelState.Failed);
            Assert.Contains(rig.Log.Errors, l => l.Contains("Speech model failed", StringComparison.Ordinal));

            rig.Scheduler.Advance(DictationCoordinator.AlertDisplay);
            rig.Hook.Fire(HotkeyEvent.Pressed);
            Assert.Equal((PillMode.Alert, "Speech model failed", true), rig.Pill.Shows[^1]);

            rig.Model.Ensure = _ => Task.CompletedTask;
            rig.Tray.Commands!.Retry();
            UiThread.PumpUntil(() => host.ModelState == ModelState.Ready);
            Assert.Equal(2, rig.Model.Ensures);
            Assert.Equal(ModelState.Ready, rig.Tray.Model);
        });
    }

    [Fact]
    public void HotkeyUnavailable_ShowsStatusOnce_RetryTriesAgain()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var fail = true;
            rig.Hook.InstallError = () => fail ? new HotkeyUnavailableException() : null;
            var host = rig.Start();

            Assert.True(rig.Tray.HotkeyUnavailable);
            Assert.True(host.HotkeyUnavailable);
            rig.Scheduler.Advance(TimeSpan.FromMinutes(10));
            Assert.Equal(1, rig.Hook.Installs + rig.Hook.Reinstalls); // never a retry loop

            fail = false;
            rig.Tray.Commands!.Retry();
            Assert.False(rig.Tray.HotkeyUnavailable);
            Assert.Equal(HotkeyKey.RightAlt, rig.Hook.Keys[^1]);
        });
    }

    [Fact]
    public void Hook_IsInstalledThenGivenTheSavedKey()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Settings.Stored = rig.Settings.Stored with { Hotkey = HotkeyKey.Copilot };
            rig.Start();
            Assert.Equal(HotkeyKey.Copilot, rig.Hook.CreatedWith);
            Assert.Equal(1, rig.Hook.Installs);
            Assert.Equal(new[] { HotkeyKey.Copilot }, rig.Hook.Keys);
        });
    }

    [Fact]
    public void SessionLock_CancelsTheRecordingAndResetsTheHook_UnlockReinstalls()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            rig.Hook.Fire(HotkeyEvent.Pressed);
            rig.Recorder.RaiseLevel(Speech);

            rig.Session.Lock();

            Assert.Equal(DictationPhase.Idle, host.Coordinator.Phase);
            Assert.Equal(1, rig.Recorder.Cancels);
            Assert.Equal(1, rig.Hook.Resets);
            Assert.Empty(rig.Inserter.Inserted);

            rig.Session.Unlock();
            Assert.Equal(2, rig.Hook.Resets);
            Assert.Equal(1, rig.Hook.Reinstalls);
        });
    }

    [Fact]
    public void ChangingTheHotkeyWhileRecording_CancelsTheRecordingAndSaves()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            rig.Hook.Fire(HotkeyEvent.Pressed);
            rig.Recorder.RaiseLevel(Speech);

            rig.Tray.Commands!.SelectHotkey(HotkeyKey.RightCtrl);

            Assert.Equal(DictationPhase.Idle, host.Coordinator.Phase);
            Assert.Equal(1, rig.Recorder.Cancels);
            Assert.Equal(HotkeyKey.RightCtrl, rig.Hook.Keys[^1]);
            Assert.Equal(HotkeyKey.RightCtrl, rig.Settings.Stored.Hotkey);
            Assert.Equal(HotkeyKey.RightCtrl, rig.Tray.Settings!.Hotkey);
        });
    }

    [Fact]
    public void EverySettingsChange_IsSaved()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Start();
            var saves = rig.Settings.Saves;

            rig.Tray.Commands!.SelectMic("mic-1");
            rig.Tray.Commands.SetLaunchAtStartup(true);
            rig.Tray.Commands.SelectMic(null);

            Assert.Equal(saves + 3, rig.Settings.Saves);
            Assert.Null(rig.Settings.Stored.MicrophoneId);
            Assert.True(rig.Settings.Stored.LaunchAtStartup);
            Assert.True(rig.Startup.Enabled);
        });
    }

    [Fact]
    public void UnreadableSettings_UseDefaultsAndAreNeverOverwritten()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Settings.LoadError = new IOException("The process cannot access the file because it is being used.");
            var host = rig.Start();

            rig.Tray.Commands!.SelectMic("mic-1");
            rig.Tray.Commands.SelectHotkey(HotkeyKey.RightCtrl);

            Assert.Equal(0, rig.Settings.Saves);
            Assert.Equal(HotkeyKey.RightCtrl, host.Settings.Hotkey); // still applies for this session
        });
    }

    [Fact]
    public void LaunchAtStartup_FollowsTheRunEntry()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Startup.Enabled = true; // registered, but the settings say off
            rig.Start();
            Assert.True(rig.Settings.Stored.LaunchAtStartup);
        });
    }

    [Fact]
    public void Updates_ReadyShowsTheRow_FailuresAreLogged_CheckedEverySixHours()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Updates.Next = () => (false, new HttpRequestException("rate limited"));
            rig.Start();
            UiThread.PumpUntil(() => rig.Log.Errors.Any());
            Assert.Contains(rig.Log.Errors, l => l.Contains("Update check failed: HttpRequestException: rate limited", StringComparison.Ordinal));
            Assert.False(rig.Tray.UpdateReady);

            rig.Updates.Next = () => (true, null);
            rig.Scheduler.Advance(AppHost.UpdateInterval);
            UiThread.PumpUntil(() => rig.Tray.UpdateReady);
            Assert.Equal(2, rig.Updates.Checks);

            rig.Tray.Commands!.RestartToUpdate();
            Assert.Equal(1, rig.Updates.Applies);
            Assert.True(rig.Hook.Disposed); // the hook is released before restarting
        });
    }

    [Fact]
    public void RestartToUpdate_WaitsForAPasteAndItsClipboardRestoreToFinish()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var paste = new TaskCompletionSource<InsertResult>();
            rig.Inserter.Pending = paste.Task; // the paste (and the clipboard restore) is still running
            var host = rig.Start();
            Dictate(rig, host, until: DictationPhase.Transcribing);
            UiThread.PumpUntil(() => rig.Inserter.Inserted.Count == 1);

            rig.Tray.Commands!.RestartToUpdate();
            Assert.Equal(0, rig.Updates.Applies);

            paste.SetResult(InsertResult.Pasted);
            UiThread.PumpUntil(() => host.Coordinator.Phase == DictationPhase.Pasted);
            rig.Scheduler.Advance(TimeSpan.Zero);
            Assert.Equal(1, rig.Updates.Applies);
        });
    }

    [Fact]
    public void RestartToUpdate_WhileRecording_DropsTheRecordingFirst()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            rig.Hook.Fire(HotkeyEvent.Pressed);

            rig.Tray.Commands!.RestartToUpdate();

            Assert.Equal(1, rig.Recorder.Cancels);
            Assert.Empty(rig.Inserter.Inserted);
            Assert.Equal(1, rig.Updates.Applies);
            Assert.Equal(DictationPhase.Idle, host.Coordinator.Phase);
        });
    }

    [Fact]
    public void Faults_AreLogged_WithoutTranscripts()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            rig.Inserter.Result = _ => throw new InvalidOperationException("SendInput was blocked");
            var host = rig.Start();

            Dictate(rig, host, until: DictationPhase.PasteFailed);

            Assert.Contains(rig.Log.Errors, l => l.Contains("Dictation fault: InvalidOperationException: SendInput was blocked", StringComparison.Ordinal));
            Assert.Equal((PillMode.Alert, "Couldn't paste", true), rig.Pill.Shows[^1]);
            Assert.Equal("hello world", rig.Tray.LastDictation); // kept for Copy
            Assert.DoesNotContain("hello world", rig.Log.Text);

            rig.Tray.Commands!.Copy("hello world");
            Assert.Equal(new[] { "hello world" }, rig.Copied);
        });
    }

    [Fact]
    public void SecondLaunch_OpensTheTrayPanel_QuitShutsDown()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            host.ShowPanel();
            Assert.Equal(1, rig.Tray.PanelShows);
            rig.Tray.Commands!.Quit();
            Assert.Equal(1, rig.Shutdowns);
        });
    }

    [Fact]
    public void Dispose_ReleasesEverything_AndDropsARecording()
    {
        UiThread.Run(() =>
        {
            var rig = new HostRig();
            var host = rig.Start();
            rig.Hook.Fire(HotkeyEvent.Pressed);

            host.Dispose();

            Assert.Equal(1, rig.Recorder.Cancels);
            Assert.Empty(rig.Inserter.Inserted);
            Assert.True(rig.Hook.Disposed);
            Assert.True(rig.Tray.Disposed);
            Assert.True(rig.Pill.Disposed);
            Assert.True(rig.Session.Disposed);
            Assert.True(rig.Recorder.Disposed);
            Assert.True(rig.Model.Transcriber.Disposed);
            host.Dispose(); // twice is fine
        });
    }

    /// <summary>
    /// The real platform ports (keyboard hook, WASAPI recorder, microphone catalog, clipboard, pill window, tray icon
    /// and panel, session events, updater) on an STA thread with a fake model and a temp settings file: the app starts and shuts down
    /// cleanly. This is what <c>dotnet run</c> does, minus the model download.
    /// </summary>
    [Fact]
    public void RealPlatformPorts_StartAndShutDownCleanly()
    {
        var dir = TempDir.Create();
        try
        {
            UiThread.Run(() =>
            {
                var log = new MemoryLog();
                var real = AppComposition.CreatePorts(Dispatcher.CurrentDispatcher, () => { }, log);
                var model = new FakeModel();
                var ports = new AppPorts
                {
                    Recorder = real.Recorder,
                    Microphones = real.Microphones,
                    Inserter = real.Inserter,
                    Scheduler = real.Scheduler,
                    CreateHotkeyHook = real.CreateHotkeyHook,
                    Model = model,
                    Settings = new SettingsFile(Path.Combine(dir, "settings.json")),
                    Pill = real.Pill,
                    Session = real.Session,
                    Startup = new FakeStartup(),
                    Log = log,
                    Shutdown = real.Shutdown,
                    Updates = real.Updates,
                    MicAccessAllowed = real.MicAccessAllowed,
                    CopyToClipboard = real.CopyToClipboard,
                    CreateTray = real.CreateTray,
                    Owned = real.Owned,
                };

                var host = new AppHost(ports);
                host.Start();
                UiThread.PumpUntil(() => host.ModelState == ModelState.Ready);
                Assert.False(host.HotkeyUnavailable, log.Text);
                Assert.IsType<DispatcherScheduler>(real.Scheduler);
                UiThread.Pump(TimeSpan.FromMilliseconds(300));

                host.Dispose();
                UiThread.Pump(TimeSpan.FromMilliseconds(100));

                // Only the updater may complain (this copy isn't installed); nothing else may fail.
                Assert.DoesNotContain(log.Errors, l => !l.Contains("Update check failed", StringComparison.Ordinal));
                Assert.True(model.Transcriber.Disposed);
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static void Dictate(HostRig rig, AppHost host, DictationPhase until = DictationPhase.Pasted)
    {
        rig.Hook.Fire(HotkeyEvent.Pressed);
        rig.Recorder.RaiseLevel(Speech);
        rig.Scheduler.Advance(TimeSpan.FromSeconds(0.5));
        rig.Hook.Fire(HotkeyEvent.Released);
        UiThread.PumpUntil(() => host.Coordinator.Phase == until);
    }

    private sealed class ModelDownloadFailure() : Exception("Model download failed: 404");
}
