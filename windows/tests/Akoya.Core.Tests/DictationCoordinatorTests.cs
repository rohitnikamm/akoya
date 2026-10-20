using Akoya.Core.Audio;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Xunit;
using static Akoya.Core.Dictation.DictationPhase;

namespace Akoya.Core.Tests;

/// <summary>
/// Ported from the Mac's AppCoordinatorTests.swift (every case; see the task report for the
/// mapping), plus the Windows-only phases (alerts, Pasted/Copied/PasteFailed, session lock).
/// Time only moves through <see cref="ManualScheduler"/>, and awaits resume inline through
/// <see cref="InlineSynchronizationContext"/>, so every test is synchronous and deterministic.
/// </summary>
public class DictationCoordinatorTests
{
    private sealed class Rig : IDisposable
    {
        private readonly IDisposable _context = InlineSynchronizationContext.Install();

        public Rig(string transcript = "hello")
        {
            Transcriber = new FakeTranscriber(transcript);
            Coordinator = new DictationCoordinator(
                Audio, Transcriber, Inserter, Scheduler, () => Model, () => DeviceId);
            Coordinator.PhaseChanged += Phases.Add;
            Coordinator.Level += Levels.Add;
            Coordinator.Faulted += Faults.Add;
        }

        public FakeRecorder Audio { get; } = new();
        public FakeTranscriber Transcriber { get; }
        public FakeInserter Inserter { get; } = new();
        public ManualScheduler Scheduler { get; } = new();
        public ModelState Model { get; set; } = ModelState.Ready;
        public string? DeviceId { get; set; } = "mic-1";
        public DictationCoordinator Coordinator { get; }
        public List<DictationPhase> Phases { get; } = [];
        public List<float> Levels { get; } = [];
        public List<Exception> Faults { get; } = [];
        public List<(string Text, bool IsFinal)> Practice { get; } = [];

        public DictationPhase Phase => Coordinator.Phase;

        public void Press() => Coordinator.Handle(HotkeyEvent.Pressed);
        public void Release() => Coordinator.Handle(HotkeyEvent.Released);
        public void Cancel() => Coordinator.Handle(HotkeyEvent.Cancel);

        /// <summary>Feeds one loud level, as if the user spoke.</summary>
        public void Speak() => Audio.RaiseLevel(1.0f);

        public void Advance(int ms) => Scheduler.Advance(TimeSpan.FromMilliseconds(ms));
        public void Advance(TimeSpan by) => Scheduler.Advance(by);

        /// <summary>One live-preview tick.</summary>
        public void Tick() => Scheduler.Advance(DictationCoordinator.PreviewTick);

        /// <summary>Practice destination with one second of captured audio.</summary>
        public void UsePractice()
        {
            Audio.Samples = new float[16_000];
            Coordinator.Destination = new Destination.Practice((text, isFinal) => Practice.Add((text, isFinal)));
        }

        public string[] PracticeTexts => Practice.Select(p => p.Text).ToArray();
        public bool[] PracticeFinals => Practice.Select(p => p.IsFinal).ToArray();

        /// <summary>Press, speak (past the silence gate), hold past the threshold, release.</summary>
        public void Dictate()
        {
            Press();
            Speak();
            Advance(1000);
            Release();
        }

        public void Dispose() => _context.Dispose();
    }

    private static float RmsAtDb(double db) => (float)Math.Pow(10, db / 20);

    // MARK: - Constants and defaults

    [Fact]
    public void Constants_MatchTheSpec()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(300), DictationCoordinator.HoldThreshold);
        Assert.Equal(TimeSpan.FromSeconds(300), DictationCoordinator.MaxRecording);
        Assert.Equal(2, DictationCoordinator.SilenceMaxStep);
        Assert.Equal(TimeSpan.FromMilliseconds(900), DictationCoordinator.PastedDisplay);
        Assert.Equal(TimeSpan.FromMilliseconds(1600), DictationCoordinator.AlertDisplay);
        Assert.Equal(TimeSpan.FromMilliseconds(100), DictationCoordinator.PreviewTick);
    }

    [Fact]
    public void StartsIdle_WithPasteDestination()
    {
        using var rig = new Rig();
        Assert.Equal(Idle, rig.Phase);
        Assert.IsType<Destination.Paste>(rig.Coordinator.Destination);
        Assert.Null(rig.Coordinator.LastDictation);
        Assert.Null(rig.Coordinator.RecordingStartedAt);
    }

    // MARK: - Hold, tap and Esc (ported)

    [Fact]
    public void HoldPressRelease_InsertsTrimmedTextWithSpace()
    {
        using var rig = new Rig("  hello world ");
        rig.Press();
        Assert.Equal(Recording, rig.Phase);
        rig.Speak();
        rig.Advance(1000);
        rig.Release();
        Assert.Equal(new[] { "hello world " }, rig.Inserter.Inserted);
        Assert.Equal(new[] { Recording, Transcribing, Pasted }, rig.Phases);
        rig.Advance(DictationCoordinator.PastedDisplay);
        Assert.Equal(Idle, rig.Phase);
    }

    [Fact]
    public void HoldThenRelease_Stops()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(500);
        rig.Release();
        Assert.Contains(Transcribing, rig.Phases);
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Equal(0, rig.Audio.CancelCount);
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
    }

    [Fact]
    public void QuickTap_KeepsRecording()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release();
        Assert.Equal(Recording, rig.Phase);
        Assert.Equal(0, rig.Audio.StopCount);
        Assert.Equal(0, rig.Audio.CancelCount);
        Assert.Empty(rig.Transcriber.Received);
        Assert.Empty(rig.Inserter.Inserted);
    }

    [Fact]
    public void ReleaseJustUnderThreshold_Latches_AndJustOver_Stops()
    {
        using var under = new Rig();
        under.Press();
        under.Speak();
        under.Advance(290);
        under.Release();
        Assert.Equal(Recording, under.Phase);
        under.Cancel();

        using var over = new Rig();
        over.Press();
        over.Speak();
        over.Advance(310);
        over.Release();
        Assert.Equal(new[] { Recording, Transcribing, Pasted }, over.Phases);
    }

    [Fact]
    public void ReleaseExactlyAtThreshold_Stops()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(DictationCoordinator.HoldThreshold);
        rig.Release();
        Assert.Equal(1, rig.Audio.StopCount);
    }

    [Fact]
    public void TapThenPress_Stops()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release();
        rig.Advance(2000); // talking, hands off the key
        rig.Press();
        Assert.Contains(Transcribing, rig.Phases);
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Single(rig.Transcriber.Received);
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
    }

    [Fact]
    public void ReleaseAfterTapStopPress_IsIgnored()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release();
        rig.Press(); // stops
        rig.Advance(DictationCoordinator.PastedDisplay);
        Assert.Equal(Idle, rig.Phase);
        rig.Release(); // the key coming back up must do nothing
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(1, rig.Audio.StartCount);
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Equal(0, rig.Audio.CancelCount);
        Assert.Single(rig.Transcriber.Received);
    }

    [Fact]
    public void ReleaseOfStopPress_WhileTranscribing_IsIgnored()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release();
        rig.Press(); // stops
        Assert.Equal(Transcribing, rig.Phase);
        rig.Release();
        Assert.Equal(Transcribing, rig.Phase);
        rig.Transcriber.Finish(0, "hello");
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
        Assert.Equal(Pasted, rig.Phase);
    }

    [Fact]
    public void Escape_CancelsAfterTap()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release();
        Assert.Equal(Recording, rig.Phase);
        rig.Cancel();
        Assert.Equal(1, rig.Audio.CancelCount);
        Assert.Equal(0, rig.Audio.StopCount);
        Assert.Empty(rig.Transcriber.Received);
        Assert.Equal(Idle, rig.Phase);
        // A new recording after the cancel starts unlatched: a long hold stops on release.
        rig.Press();
        rig.Speak();
        rig.Advance(1000);
        rig.Release();
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Equal(Pasted, rig.Phase);
    }

    [Fact]
    public void NextRecordingAfterTapStop_StartsUnlatched()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release();
        rig.Press(); // stops
        rig.Advance(DictationCoordinator.PastedDisplay);
        rig.Release();
        rig.Press(); // fresh recording
        rig.Speak();
        rig.Advance(1000);
        rig.Release(); // a real hold: must stop, not be treated as latched
        Assert.Equal(Pasted, rig.Phase);
        Assert.Equal(2, rig.Audio.StartCount);
        Assert.Equal(2, rig.Transcriber.CallCount);
    }

    [Fact]
    public void SilentTap_GoesIdle_WithoutTranscribing()
    {
        // Mac: silentTapStillErrorCue. Windows fades the pill instead of an error cue.
        using var rig = new Rig();
        rig.Press();
        rig.Advance(100);
        rig.Release(); // accidental tap, nothing said
        Assert.Equal(Recording, rig.Phase);
        rig.Advance(2000);
        rig.Press();
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(new[] { Recording, Idle }, rig.Phases);
        Assert.Empty(rig.Transcriber.Received);
    }

    [Fact]
    public void LatchedRecording_StopsAtMaxRecording()
    {
        // A latched (tap-started) recording is still bounded by MaxRecording.
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release();
        Assert.Equal(Recording, rig.Phase);
        rig.Advance(DictationCoordinator.MaxRecording);
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
    }

    [Fact]
    public void Escape_Cancels()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Advance(1000);
        rig.Cancel();
        Assert.Equal(1, rig.Audio.CancelCount);
        Assert.Empty(rig.Transcriber.Received);
        Assert.Empty(rig.Inserter.Inserted);
        Assert.Equal(Idle, rig.Phase);
    }

    [Fact]
    public void PressWhileTranscribing_IsIgnored()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.Dictate();
        Assert.Equal(Transcribing, rig.Phase);
        rig.Press();
        Assert.Equal(1, rig.Audio.StartCount);
        Assert.Equal(Transcribing, rig.Phase);
        rig.Transcriber.Finish(0, "hello");
        Assert.Single(rig.Transcriber.Received);
    }

    [Fact]
    public void ReleaseWithoutPress_IsIgnored()
    {
        using var rig = new Rig();
        rig.Release();
        Assert.Equal(0, rig.Audio.StartCount);
        Assert.Equal(0, rig.Audio.StopCount);
        Assert.Equal(0, rig.Audio.CancelCount);
        Assert.Equal(Idle, rig.Phase);
        Assert.Empty(rig.Phases);
    }

    // MARK: - Empty text and failures (ported)

    [Fact]
    public void EmptyTranscript_GoesIdle_NothingInserted()
    {
        using var rig = new Rig("   ");
        rig.Dictate();
        Assert.Equal(new[] { Recording, Transcribing, Idle }, rig.Phases);
        Assert.Empty(rig.Inserter.Inserted);
        Assert.Null(rig.Coordinator.LastDictation);
    }

    [Fact]
    public void TranscriberFailure_GoesIdle()
    {
        using var rig = new Rig();
        rig.Transcriber.Error = new InvalidOperationException("engine");
        rig.Dictate();
        Assert.Equal(new[] { Recording, Transcribing, Idle }, rig.Phases);
        Assert.Empty(rig.Inserter.Inserted);
    }

    [Fact]
    public void TranscriberFailingLater_GoesIdle()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.Dictate();
        Assert.Equal(Transcribing, rig.Phase);
        rig.Transcriber.Fail(0, new InvalidOperationException("engine"));
        Assert.Equal(Idle, rig.Phase);
        Assert.Empty(rig.Inserter.Inserted);
    }

    [Fact]
    public void InserterThrows_GoesToPasteFailed_ThenIdle()
    {
        using var rig = new Rig();
        rig.Inserter.Error = new InvalidOperationException("clipboard");
        rig.Dictate();
        Assert.Equal(new[] { Recording, Transcribing, PasteFailed }, rig.Phases);
        rig.Advance(DictationCoordinator.AlertDisplay);
        Assert.Equal(Idle, rig.Phase);
    }

    [Fact]
    public void UnknownStartFailure_ShowsMicMissing_ReleaseAndCancelIgnored()
    {
        using var rig = new Rig();
        rig.Audio.StartError = new InvalidOperationException("device busy");
        rig.Press();
        Assert.Equal(MicMissing, rig.Phase);
        // A release or Esc must not act on a recording that never started.
        rig.Release();
        rig.Cancel();
        Assert.Equal(0, rig.Audio.StopCount);
        Assert.Equal(0, rig.Audio.CancelCount);
        rig.Advance(DictationCoordinator.AlertDisplay);
        Assert.Equal(Idle, rig.Phase);
        Assert.Empty(rig.Transcriber.Received);
    }

    [Fact]
    public void Interruption_TranscribesWhatWasCaptured()
    {
        using var rig = new Rig();
        rig.Audio.Samples = [0.5f, 0.6f];
        rig.Press();
        rig.Speak();
        rig.Audio.RaiseInterrupted();
        Assert.Contains(Transcribing, rig.Phases);
        Assert.Equal(new[] { 0.5f, 0.6f }, Assert.Single(rig.Transcriber.Received));
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
    }

    // MARK: - Levels (ported; the Mac's waveform columns live in the Windows pill, not here)

    [Fact]
    public void Levels_ForwardedWhileRecording()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Audio.RaiseLevel(1.0f);
        rig.Audio.RaiseLevel(0.25f);
        Assert.Equal(new[] { 1.0f, 0.25f }, rig.Levels);
    }

    [Fact]
    public void Levels_IgnoredWhenNotRecording()
    {
        using var rig = new Rig();
        rig.Audio.RaiseLevel(1.0f);
        Assert.Empty(rig.Levels);
        rig.Press();
        rig.Audio.RaiseLevel(1.0f);
        rig.Advance(1000);
        rig.Cancel();
        rig.Audio.RaiseLevel(0.5f);
        Assert.Equal(new[] { 1.0f, 0f }, rig.Levels);
    }

    [Fact]
    public void MaxRecording_AutoStops()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(DictationCoordinator.MaxRecording);
        Assert.Single(rig.Transcriber.Received);
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
        rig.Advance(DictationCoordinator.PastedDisplay);
        Assert.Equal(Idle, rig.Phase);
    }

    [Fact]
    public void Cancel_CancelsMaxRecordingTimer()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Advance(1000);
        rig.Cancel();
        Assert.Equal(0, rig.Scheduler.PendingCount);
        rig.Advance(TimeSpan.FromSeconds(400));
        Assert.Empty(rig.Transcriber.Received);
        Assert.Equal(0, rig.Audio.StopCount);
    }

    // MARK: - Silence gate (ported)

    [Fact]
    public void SilentRecording_SkipsTranscription()
    {
        using var rig = new Rig();
        rig.Press();
        for (var i = 0; i < 10; i++)
            rig.Audio.RaiseLevel(0);
        rig.Advance(1000);
        rig.Release();
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Empty(rig.Transcriber.Received);
        Assert.Empty(rig.Inserter.Inserted);
    }

    [Fact]
    public void QuietLevelsAtTheThreshold_StillCountAsSilence()
    {
        using var rig = new Rig();
        // -40 dBFS maps to step 2: the loudest level that still counts as silence.
        var rms = RmsAtDb(-40);
        Assert.Equal(DictationCoordinator.SilenceMaxStep, LevelMeter.Step(rms));
        rig.Press();
        rig.Audio.RaiseLevel(rms);
        rig.Advance(1000);
        rig.Release();
        Assert.Empty(rig.Transcriber.Received);
    }

    [Fact]
    public void LevelJustAboveTheThreshold_IsTranscribed()
    {
        using var rig = new Rig();
        var rms = RmsAtDb(-35);
        Assert.Equal(DictationCoordinator.SilenceMaxStep + 1, LevelMeter.Step(rms));
        rig.Press();
        rig.Audio.RaiseLevel(rms);
        rig.Audio.RaiseLevel(0); // the loudest level counts, not the last one
        rig.Advance(1000);
        rig.Release();
        Assert.Single(rig.Transcriber.Received);
    }

    [Fact]
    public void SilentInterruption_SkipsTranscription()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Audio.RaiseLevel(0);
        rig.Audio.RaiseInterrupted();
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Empty(rig.Transcriber.Received);
    }

    [Fact]
    public void SilentMaxRecordingAutoStop_SkipsTranscription()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Advance(DictationCoordinator.MaxRecording);
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Empty(rig.Transcriber.Received);
        Assert.Equal(Idle, rig.Phase);
    }

    [Fact]
    public void LoudnessFromAnEarlierRecording_DoesNotCarryOver()
    {
        using var rig = new Rig();
        rig.Dictate();
        rig.Advance(DictationCoordinator.PastedDisplay);
        Assert.Single(rig.Transcriber.Received);
        rig.Press();
        rig.Advance(1000);
        rig.Release();
        Assert.Single(rig.Transcriber.Received);
    }

    // MARK: - Insert window (ported)

    [Fact]
    public void PhaseStaysBusyUntilInsertReturns()
    {
        using var rig = new Rig();
        rig.Inserter.Gated = true;
        rig.Dictate();
        Assert.True(rig.Inserter.Started);
        // Inside the paste/clipboard-restore window a new press must not start a recording.
        Assert.Equal(Transcribing, rig.Phase);
        rig.Press();
        Assert.Equal(1, rig.Audio.StartCount);
        rig.Inserter.Finish(InsertResult.Pasted);
        Assert.Equal(Pasted, rig.Phase);
    }

    // MARK: - Voice level and last dictation (ported)

    [Fact]
    public void Level_FollowsRms_AndDropsToZeroOnStop()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Audio.RaiseLevel(1.0f);
        rig.Audio.RaiseLevel(0);
        rig.Audio.RaiseLevel(1.0f);
        rig.Advance(1000);
        rig.Release();
        Assert.Equal(new[] { 1.0f, 0f, 1.0f, 0f }, rig.Levels);
    }

    [Fact]
    public void Level_DropsToZeroOnCancel()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Audio.RaiseLevel(1.0f);
        rig.Cancel();
        Assert.Equal(0f, rig.Levels[^1]);
    }

    [Fact]
    public void Level_DropsToZeroOnInterruption()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Audio.RaiseLevel(1.0f);
        rig.Audio.RaiseInterrupted();
        Assert.Equal(0f, rig.Levels[^1]);
    }

    [Fact]
    public void LastDictation_SetAfterInsert()
    {
        using var rig = new Rig("  hello world ");
        Assert.Null(rig.Coordinator.LastDictation);
        rig.Dictate();
        Assert.Equal("hello world", rig.Coordinator.LastDictation);
    }

    [Fact]
    public void LastDictation_TakesTheNewTextEvenWhenInsertThrows()
    {
        // Mac: lastDictationUnchangedWhenInsertFails. Windows keeps the text as Last dictation
        // when pasting fails (spec Errors table), so the tray panel can offer Copy.
        using var rig = new Rig();
        rig.Dictate();
        rig.Advance(DictationCoordinator.PastedDisplay);
        Assert.Equal("hello", rig.Coordinator.LastDictation);
        rig.Transcriber.Result = "second";
        rig.Inserter.Error = new InvalidOperationException("clipboard");
        rig.Dictate();
        Assert.Equal(PasteFailed, rig.Phase);
        Assert.Equal("second", rig.Coordinator.LastDictation);
    }

    [Fact]
    public void PasteMode_NeverPreviews()
    {
        using var rig = new Rig();
        rig.Audio.Samples = new float[32_000];
        rig.Press();
        rig.Speak();
        Assert.Equal(1, rig.Scheduler.PendingCount); // only the max-recording timer
        rig.Advance(1000);
        rig.Release();
        Assert.Single(rig.Transcriber.Received);
    }

    // MARK: - Practice mode (ported)

    [Fact]
    public void Practice_EmitsPartials()
    {
        using var rig = new Rig(" partial ");
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick();
        Assert.Equal(new[] { "partial" }, rig.PracticeTexts);
        Assert.Equal(new[] { false }, rig.PracticeFinals);
        Assert.Equal(16_000, rig.Transcriber.Received[0].Length);
        Assert.Equal(Recording, rig.Phase);
    }

    [Fact]
    public void Practice_WaitsForSpeechAndMinAudio()
    {
        using var rig = new Rig("partial");
        rig.UsePractice();
        rig.Press();
        rig.Tick(); // silent so far
        rig.Audio.RaiseLevel(0.001f); // still below the gate
        rig.Tick();
        rig.Speak();
        rig.Audio.Samples = new float[4_000]; // 0.25 s
        rig.Tick();
        Assert.Empty(rig.Transcriber.Received);
        Assert.Empty(rig.Practice);
    }

    [Fact]
    public void Practice_PartialsRespectTheInterval()
    {
        using var rig = new Rig("partial");
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick(); // runs at 0.1 s
        Assert.Single(rig.Practice);
        rig.Advance(300); // ticks at 0.2-0.4 s: too soon
        Assert.Single(rig.Practice);
        rig.Advance(400); // tick at 0.7 s is 0.6 s after the first pass
        Assert.Equal(2, rig.Practice.Count);
    }

    [Fact]
    public void Practice_SkipsEmptyPartials()
    {
        using var rig = new Rig("   ");
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick();
        Assert.Single(rig.Transcriber.Received);
        Assert.Empty(rig.Practice);
    }

    [Fact]
    public void Practice_FinalReplacesPartial()
    {
        using var rig = new Rig("earlier");
        rig.Dictate(); // a pasted dictation first, so LastDictation has a value
        rig.Advance(DictationCoordinator.PastedDisplay);
        rig.UsePractice();
        rig.Transcriber.Result = "par";
        rig.Press();
        rig.Speak();
        rig.Tick();
        rig.Transcriber.Result = " partial text ";
        rig.Advance(310); // past the hold threshold, before the next preview is due
        rig.Release();
        Assert.Equal(new[] { "par", "partial text" }, rig.PracticeTexts);
        Assert.Equal(new[] { false, true }, rig.PracticeFinals);
        Assert.Equal(new[] { "earlier " }, rig.Inserter.Inserted);
        Assert.Equal("earlier", rig.Coordinator.LastDictation);
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(0, rig.Scheduler.PendingCount); // preview loop and max timer are gone
    }

    [Fact]
    public void Practice_NoOverlap()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick();
        Assert.Equal(1, rig.Transcriber.CallCount);
        rig.Advance(1000); // ten ticks while the first pass is in flight
        Assert.Equal(1, rig.Transcriber.CallCount);
        rig.Transcriber.Finish(0, "one");
        Assert.Equal(new[] { "one" }, rig.PracticeTexts);
        // With the first pass done, the next tick may run again.
        rig.Tick();
        Assert.Equal(2, rig.Transcriber.CallCount);
        rig.Transcriber.Finish(1, "two");
        Assert.Equal(new[] { "one", "two" }, rig.PracticeTexts);
        Assert.Equal(1, rig.Transcriber.MaxConcurrent);
    }

    [Fact]
    public void Practice_PartialFinishingAfterRelease_IsDropped()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick();
        rig.Advance(310);
        rig.Release();
        // The final pass waits for the partial (see Practice_FinalWaitsForTheInFlightPartial).
        rig.Transcriber.Finish(0, "late partial");
        Assert.Equal(2, rig.Transcriber.CallCount);
        rig.Transcriber.Finish(1, "final");
        Assert.Equal(new[] { "final" }, rig.PracticeTexts);
        Assert.Equal(new[] { true }, rig.PracticeFinals);
    }

    [Fact]
    public void Practice_FinalWaitsForTheInFlightPartial()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick();
        rig.Advance(310);
        rig.Release();
        // Never two transcriptions at once: the final must not start while the partial runs.
        rig.Advance(5000);
        Assert.Equal(1, rig.Transcriber.CallCount);
        Assert.Equal(Transcribing, rig.Phase);
        rig.Transcriber.Finish(0, "partial");
        Assert.Equal(2, rig.Transcriber.CallCount);
        rig.Transcriber.Finish(1, "final");
        Assert.Equal(new[] { "final" }, rig.PracticeTexts);
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(1, rig.Transcriber.MaxConcurrent);
    }

    [Fact]
    public void Practice_EmptyFinal_ReportsEmpty_ThenIdle()
    {
        using var rig = new Rig("  ");
        rig.UsePractice();
        rig.Dictate();
        Assert.Equal(new[] { "" }, rig.PracticeTexts);
        Assert.Equal(new[] { true }, rig.PracticeFinals);
        Assert.Empty(rig.Inserter.Inserted);
        Assert.Equal(new[] { Recording, Transcribing, Idle }, rig.Phases);
    }

    [Fact]
    public void Practice_SilentRecording_GoesIdle_WithoutText()
    {
        using var rig = new Rig();
        rig.UsePractice();
        rig.Press();
        rig.Advance(1000);
        rig.Release();
        Assert.Equal(Idle, rig.Phase);
        Assert.Empty(rig.Transcriber.Received);
        Assert.Empty(rig.Practice);
    }

    [Fact]
    public void ResetToPasteAfterPractice()
    {
        using var rig = new Rig("hello");
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Coordinator.Destination = new Destination.Paste();
        rig.Advance(1000); // ticks no longer preview
        rig.Release();
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
        Assert.Equal("hello", rig.Coordinator.LastDictation);
        Assert.Empty(rig.Practice);
        Assert.Single(rig.Transcriber.Received);
    }

    [Fact]
    public void Practice_LoopTicksWhileRecording_AndStopsAfter()
    {
        using var rig = new Rig("live");
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Advance(1000); // the loop delivers a partial without any help
        Assert.Equal("live", rig.PracticeTexts.First());
        rig.Cancel();
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    // MARK: - Practice mode (added)

    [Fact]
    public void Practice_PartialFailure_IsSkipped_AndTheNextPassRuns()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick();
        rig.Transcriber.Fail(0, new InvalidOperationException("engine"));
        Assert.Empty(rig.Practice);
        Assert.Equal(Recording, rig.Phase);
        rig.Advance(600);
        Assert.Equal(2, rig.Transcriber.CallCount);
    }

    [Fact]
    public void Practice_TranscriberFailure_ReportsEmptyFinal_ThenIdle()
    {
        using var rig = new Rig();
        rig.UsePractice();
        var error = new InvalidOperationException("engine");
        rig.Transcriber.Error = error;
        rig.Press();
        rig.Speak();
        rig.Advance(310); // the preview at 0.1 s fails too
        rig.Release();
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(new[] { "" }, rig.PracticeTexts);
        Assert.Equal(new[] { true }, rig.PracticeFinals);
        Assert.Equal(new[] { error, error }, rig.Faults);
    }

    [Fact]
    public void CancelWithPreviewInFlight_NextRecordingNeverOverlaps()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick();
        rig.Cancel(); // the partial is still running
        rig.Press();
        rig.Speak();
        rig.Advance(1000);
        Assert.Equal(1, rig.Transcriber.CallCount);
        rig.Transcriber.Finish(0, "stale");
        Assert.Empty(rig.Practice); // belongs to the cancelled recording
        rig.Tick();
        Assert.Equal(2, rig.Transcriber.CallCount);
        rig.Transcriber.Finish(1, "fresh");
        Assert.Equal(new[] { "fresh" }, rig.PracticeTexts);
        Assert.Equal(1, rig.Transcriber.MaxConcurrent);
    }

    // MARK: - Windows phases (added)

    [Fact]
    public void ModelNotReady_ShowsAlertThenIdle()
    {
        // Also covers Mac's notReadyFlashesErrorCueWithoutRecording.
        using var rig = new Rig();
        rig.Model = ModelState.Loading;
        rig.Press();
        Assert.Equal(ModelNotReady, rig.Phase);
        Assert.Equal(0, rig.Audio.StartCount);
        rig.Release();
        rig.Cancel();
        rig.Advance(DictationCoordinator.AlertDisplay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(ModelNotReady, rig.Phase);
        rig.Advance(1);
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(0, rig.Audio.StartCount);
        Assert.Empty(rig.Transcriber.Received);
    }

    [Fact]
    public void ModelFailed_ShowsAlertThenIdle()
    {
        using var rig = new Rig();
        rig.Model = ModelState.Failed;
        rig.Press();
        Assert.Equal(ModelFailed, rig.Phase);
        Assert.Equal(0, rig.Audio.StartCount);
        rig.Advance(DictationCoordinator.AlertDisplay);
        Assert.Equal(new[] { ModelFailed, Idle }, rig.Phases);
    }

    [Fact]
    public void ModelReadyLater_NextPressRecords()
    {
        using var rig = new Rig();
        rig.Model = ModelState.Loading;
        rig.Press();
        rig.Advance(DictationCoordinator.AlertDisplay);
        rig.Model = ModelState.Ready;
        rig.Press();
        Assert.Equal(Recording, rig.Phase);
    }

    [Fact]
    public void MicMissing_NothingRecorded_AlertThenIdle()
    {
        using var rig = new Rig();
        rig.Audio.StartError = new MicrophoneMissingException();
        rig.Press();
        Assert.Equal(MicMissing, rig.Phase);
        rig.Speak();
        rig.Release();
        Assert.Equal(0, rig.Audio.StopCount);
        Assert.Equal(0, rig.Audio.CancelCount);
        Assert.Empty(rig.Levels);
        rig.Advance(DictationCoordinator.AlertDisplay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(MicMissing, rig.Phase);
        rig.Advance(1);
        Assert.Equal(new[] { MicMissing, Idle }, rig.Phases);
        Assert.Empty(rig.Transcriber.Received);
    }

    [Fact]
    public void MicBlocked_NothingRecorded_AlertThenIdle()
    {
        using var rig = new Rig();
        rig.Audio.StartError = new MicrophoneBlockedException();
        rig.Press();
        Assert.Equal(MicBlocked, rig.Phase);
        rig.Release();
        Assert.Equal(0, rig.Audio.StopCount);
        rig.Advance(DictationCoordinator.AlertDisplay);
        Assert.Equal(new[] { MicBlocked, Idle }, rig.Phases);
        Assert.Empty(rig.Transcriber.Received);
    }

    [Fact]
    public void PressDuringAlert_IsIgnored()
    {
        using var rig = new Rig();
        rig.Audio.StartError = new MicrophoneMissingException();
        rig.Press();
        rig.Advance(500);
        rig.Press();
        Assert.Equal(1, rig.Audio.StartCount);
        Assert.Equal(MicMissing, rig.Phase);
        rig.Advance(1100); // still 1.6 s from the first press
        Assert.Equal(Idle, rig.Phase);
    }

    [Fact]
    public void Pasted_ShownForPointNineSeconds()
    {
        using var rig = new Rig();
        rig.Dictate();
        Assert.Equal(Pasted, rig.Phase);
        rig.Advance(899);
        Assert.Equal(Pasted, rig.Phase);
        rig.Advance(1);
        Assert.Equal(new[] { Recording, Transcribing, Pasted, Idle }, rig.Phases);
    }

    [Fact]
    public void CopiedOnly_GoesToCopied_AndSetsLastDictation()
    {
        using var rig = new Rig();
        rig.Inserter.Result = InsertResult.CopiedOnly;
        rig.Dictate();
        Assert.Equal(Copied, rig.Phase);
        Assert.Equal("hello", rig.Coordinator.LastDictation);
        rig.Advance(899);
        Assert.Equal(Copied, rig.Phase);
        rig.Advance(1);
        Assert.Equal(Idle, rig.Phase);
    }

    [Fact]
    public void InsertFailed_GoesToPasteFailed_AndKeepsLastDictation()
    {
        using var rig = new Rig();
        rig.Inserter.Result = InsertResult.Failed;
        rig.Dictate();
        Assert.Equal(PasteFailed, rig.Phase);
        Assert.Equal("hello", rig.Coordinator.LastDictation);
        rig.Advance(1599);
        Assert.Equal(PasteFailed, rig.Phase);
        rig.Advance(1);
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal("hello", rig.Coordinator.LastDictation);
    }

    [Fact]
    public void PressDuringPasted_StartsNewRecording()
    {
        using var rig = new Rig();
        rig.Dictate();
        Assert.Equal(Pasted, rig.Phase);
        rig.Press();
        Assert.Equal(Recording, rig.Phase);
        Assert.Equal(2, rig.Audio.StartCount);
        rig.Advance(DictationCoordinator.PastedDisplay); // the old display timer must not end it
        Assert.Equal(Recording, rig.Phase);
    }

    [Fact]
    public void Interrupted_TranscribesCaptured()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release(); // latched, hands-free
        rig.Audio.Samples = [0.7f, 0.8f];
        rig.Advance(2000);
        rig.Audio.RaiseInterrupted();
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Equal(0, rig.Audio.CancelCount);
        Assert.Equal(new[] { 0.7f, 0.8f }, Assert.Single(rig.Transcriber.Received));
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
        rig.Audio.RaiseInterrupted(); // not recording any more: ignored
        Assert.Equal(1, rig.Audio.StopCount);
    }

    [Fact]
    public void SessionLocked_CancelsRecording()
    {
        using var rig = new Rig();
        rig.Coordinator.SessionLocked(); // idle: nothing to cancel
        Assert.Equal(0, rig.Audio.CancelCount);

        rig.Press();
        rig.Speak();
        rig.Advance(100);
        rig.Release(); // latched
        rig.Coordinator.SessionLocked();
        Assert.Equal(1, rig.Audio.CancelCount);
        Assert.Equal(0, rig.Audio.StopCount);
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(0, rig.Scheduler.PendingCount);
        Assert.Empty(rig.Transcriber.Received);

        rig.Transcriber.Gated = true;
        rig.Dictate();
        rig.Coordinator.SessionLocked(); // transcribing: finishes, but is not pasted
        Assert.Equal(1, rig.Audio.CancelCount);
        rig.Transcriber.Finish(0, "hello");
        Assert.Equal(Idle, rig.Phase);
        Assert.Empty(rig.Inserter.Inserted);
        Assert.Equal("hello", rig.Coordinator.LastDictation);
    }

    [Fact]
    public void SessionLockedWhileTranscribing_EmptyText_KeepsTheEarlierLastDictation()
    {
        using var rig = new Rig("first");
        rig.Dictate();
        rig.Advance(DictationCoordinator.PastedDisplay);
        rig.Transcriber.Gated = true;
        rig.Dictate();
        rig.Coordinator.SessionLocked();
        rig.Transcriber.Finish(1, "  ");
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal("first", rig.Coordinator.LastDictation);
    }

    [Fact]
    public void SessionLock_OnlyAffectsTheDictationItHappenedDuring()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.Dictate();
        rig.Coordinator.SessionLocked();
        rig.Transcriber.Finish(0, "locked");
        rig.Dictate(); // after unlock
        rig.Transcriber.Finish(1, "unlocked");
        Assert.Equal(new[] { "unlocked " }, rig.Inserter.Inserted);
        Assert.Equal(Pasted, rig.Phase);
    }

    [Fact]
    public void MaxRecording_StopsAt300s()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Speak();
        rig.Advance(DictationCoordinator.MaxRecording - TimeSpan.FromMilliseconds(1));
        Assert.Equal(Recording, rig.Phase);
        Assert.Equal(0, rig.Audio.StopCount);
        rig.Advance(1);
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Single(rig.Transcriber.Received);
        Assert.Equal(Pasted, rig.Phase);
    }

    [Fact]
    public void Silent_GoesIdle_NoTranscription()
    {
        using var rig = new Rig();
        rig.Press();
        rig.Advance(1000);
        rig.Release();
        Assert.Equal(new[] { Recording, Idle }, rig.Phases);
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Empty(rig.Transcriber.Received);
        Assert.Empty(rig.Inserter.Inserted);
        Assert.Null(rig.Coordinator.LastDictation);
    }

    [Fact]
    public void TrailingSpaceAppended()
    {
        using var rig = new Rig("Hello there.");
        rig.Dictate();
        Assert.Equal(new[] { "Hello there. " }, rig.Inserter.Inserted);
        Assert.Equal("Hello there.", rig.Coordinator.LastDictation);
    }

    // MARK: - Ports plumbing (added)

    [Fact]
    public void DeviceId_IsReadAtEachStart()
    {
        using var rig = new Rig();
        rig.DeviceId = "usb-headset";
        rig.Press();
        rig.Cancel();
        rig.DeviceId = null; // System default
        rig.Press();
        Assert.Equal(new string?[] { "usb-headset", null }, rig.Audio.StartedWith);
    }

    [Fact]
    public void RecordingStartedAt_SetFromNow_KeptWhileTranscribing_ClearedAfter()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.Advance(5000);
        var start = rig.Scheduler.Now;
        rig.Press();
        Assert.Equal(start, rig.Coordinator.RecordingStartedAt);
        rig.Speak();
        rig.Advance(1000);
        rig.Release();
        Assert.Equal(Transcribing, rig.Phase);
        Assert.Equal(start, rig.Coordinator.RecordingStartedAt);
        rig.Transcriber.Finish(0, "hello");
        Assert.Null(rig.Coordinator.RecordingStartedAt);

        rig.Advance(DictationCoordinator.PastedDisplay);
        rig.Press();
        Assert.NotNull(rig.Coordinator.RecordingStartedAt);
        rig.Cancel();
        Assert.Null(rig.Coordinator.RecordingStartedAt);
    }

    // MARK: - Fix round 1: more phase cases

    [Fact]
    public void PressDuringCopied_StartsNewRecording()
    {
        using var rig = new Rig();
        rig.Inserter.Result = InsertResult.CopiedOnly;
        rig.Dictate();
        Assert.Equal(Copied, rig.Phase);
        rig.Press();
        Assert.Equal(Recording, rig.Phase);
        Assert.Equal(2, rig.Audio.StartCount);
        rig.Advance(DictationCoordinator.PastedDisplay);
        Assert.Equal(Recording, rig.Phase);
    }

    [Fact]
    public void PressDuringPasteFailed_StartsNewRecording()
    {
        using var rig = new Rig();
        rig.Inserter.Result = InsertResult.Failed;
        rig.Dictate();
        Assert.Equal(PasteFailed, rig.Phase);
        rig.Press();
        Assert.Equal(Recording, rig.Phase);
        Assert.Equal(2, rig.Audio.StartCount);
        rig.Advance(DictationCoordinator.AlertDisplay);
        Assert.Equal(Recording, rig.Phase);
    }

    [Fact]
    public void EscDuringTranscribing_IsIgnored()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.Dictate();
        rig.Cancel();
        Assert.Equal(Transcribing, rig.Phase);
        Assert.Equal(0, rig.Audio.CancelCount);
        rig.Transcriber.Finish(0, "hello");
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
        Assert.Equal(Pasted, rig.Phase);
    }

    [Fact]
    public void InterruptedDuringTranscribing_IsIgnored()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.Dictate();
        rig.Audio.RaiseInterrupted();
        Assert.Equal(Transcribing, rig.Phase);
        Assert.Equal(1, rig.Audio.StopCount);
        rig.Transcriber.Finish(0, "hello");
        Assert.Equal(1, rig.Transcriber.CallCount);
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
    }

    // MARK: - Fix round 1: hold time counts from the press

    [Fact]
    public void SlowStart_DoesNotEatTheHoldTime()
    {
        using var rig = new Rig();
        var pressedAt = rig.Scheduler.Now;
        rig.Audio.DuringStart = () => rig.Advance(500); // the device takes 0.5 s to open
        rig.Press();
        Assert.Equal(pressedAt, rig.Coordinator.RecordingStartedAt);
        rig.Speak();
        rig.Release(); // released right after Start returned: held 0.5 s since the press
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
    }

    // MARK: - Fix round 1: re-entrant adapter events

    [Fact]
    public void InterruptedRaisedInsideCancel_PastesNothing()
    {
        using var rig = new Rig();
        rig.Audio.DuringCancel = () =>
        {
            rig.Audio.RaiseLevel(1.0f);
            rig.Audio.RaiseInterrupted();
        };
        rig.Press();
        rig.Speak();
        rig.Advance(1000);
        rig.Cancel();
        Assert.Equal(Idle, rig.Phase);
        Assert.Equal(0, rig.Audio.StopCount);
        Assert.Empty(rig.Transcriber.Received);
        Assert.Empty(rig.Inserter.Inserted);
        Assert.Equal(new[] { 1.0f, 0f }, rig.Levels); // the re-entrant level was not forwarded
    }

    [Fact]
    public void InterruptedRaisedInsideStop_PastesOnce()
    {
        using var rig = new Rig();
        rig.Audio.DuringStop = () =>
        {
            rig.Audio.RaiseLevel(1.0f);
            rig.Audio.RaiseInterrupted();
            rig.Coordinator.Handle(HotkeyEvent.Pressed);
            rig.Coordinator.Handle(HotkeyEvent.Cancel);
        };
        rig.Dictate();
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Equal(0, rig.Audio.CancelCount);
        Assert.Equal(1, rig.Audio.StartCount);
        Assert.Single(rig.Transcriber.Received);
        Assert.Equal(new[] { "hello " }, rig.Inserter.Inserted);
        Assert.Equal(Pasted, rig.Phase);
    }

    [Fact]
    public void InterruptedRaisedInsideSilentStop_IsIgnored()
    {
        using var rig = new Rig();
        rig.Audio.DuringStop = () => rig.Audio.RaiseInterrupted();
        rig.Press();
        rig.Advance(1000);
        rig.Release();
        Assert.Equal(1, rig.Audio.StopCount);
        Assert.Equal(new[] { Recording, Idle }, rig.Phases);
    }

    // MARK: - Fix round 1: Faulted, one test per catch site

    [Fact]
    public void Faulted_UnknownStartFailure()
    {
        using var rig = new Rig();
        var error = new InvalidOperationException("device busy");
        rig.Audio.StartError = error;
        rig.Press();
        Assert.Equal(MicMissing, rig.Phase);
        Assert.Same(error, Assert.Single(rig.Faults));
    }

    [Fact]
    public void Faulted_NotRaisedForTheTwoMicrophoneExceptions()
    {
        // These are expected states with their own alerts, not faults.
        using var rig = new Rig();
        rig.Audio.StartError = new MicrophoneMissingException();
        rig.Press();
        rig.Advance(DictationCoordinator.AlertDisplay);
        rig.Audio.StartError = new MicrophoneBlockedException();
        rig.Press();
        Assert.Empty(rig.Faults);
    }

    [Fact]
    public void Faulted_TranscriberFailure()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.Dictate();
        var error = new InvalidOperationException("engine");
        rig.Transcriber.Fail(0, error);
        Assert.Equal(Idle, rig.Phase);
        Assert.Same(error, Assert.Single(rig.Faults));
    }

    [Fact]
    public void Faulted_TranscriberThrowingSynchronously()
    {
        using var rig = new Rig();
        var error = new InvalidOperationException("engine");
        var coordinator = new DictationCoordinator(rig.Audio, new ThrowingTranscriber(error),
            rig.Inserter, rig.Scheduler, () => ModelState.Ready, () => null);
        var faults = new List<Exception>();
        coordinator.Faulted += faults.Add;
        coordinator.Handle(HotkeyEvent.Pressed);
        rig.Speak();
        rig.Advance(1000);
        coordinator.Handle(HotkeyEvent.Released);
        Assert.Equal(Idle, coordinator.Phase);
        Assert.Same(error, Assert.Single(faults));
    }

    [Fact]
    public void Faulted_InserterThrows()
    {
        using var rig = new Rig();
        var error = new InvalidOperationException("clipboard");
        rig.Inserter.Error = error;
        rig.Dictate();
        Assert.Equal(PasteFailed, rig.Phase);
        Assert.Same(error, Assert.Single(rig.Faults));
    }

    [Fact]
    public void Faulted_PreviewFailure_ReportedOnce()
    {
        using var rig = new Rig();
        rig.Transcriber.Gated = true;
        rig.UsePractice();
        rig.Press();
        rig.Speak();
        rig.Tick();
        rig.Advance(310);
        rig.Release(); // the final pass waits on the failing partial
        var error = new InvalidOperationException("engine");
        rig.Transcriber.Fail(0, error);
        rig.Transcriber.Finish(1, "final");
        Assert.Same(error, Assert.Single(rig.Faults));
        Assert.Equal(new[] { "final" }, rig.PracticeTexts);
    }

    [Fact]
    public void Faulted_FinalOnTextThrows_StillGoesIdle()
    {
        using var rig = new Rig();
        var error = new InvalidOperationException("card");
        rig.Coordinator.Destination = new Destination.Practice((_, isFinal) =>
        {
            if (isFinal)
                throw error;
        });
        rig.Dictate();
        Assert.Equal(Idle, rig.Phase);
        Assert.Same(error, Assert.Single(rig.Faults));
    }

    [Fact]
    public void Faulted_PartialOnTextThrows_RecordingCarriesOn()
    {
        using var rig = new Rig("words");
        var error = new InvalidOperationException("card");
        var finals = new List<string>();
        rig.Audio.Samples = new float[16_000];
        rig.Coordinator.Destination = new Destination.Practice((text, isFinal) =>
        {
            if (!isFinal)
                throw error;
            finals.Add(text);
        });
        rig.Press();
        rig.Speak();
        rig.Tick();
        Assert.Equal(Recording, rig.Phase);
        Assert.Same(error, Assert.Single(rig.Faults));
        rig.Advance(310);
        rig.Release();
        Assert.Equal(new[] { "words" }, finals);
        Assert.Equal(Idle, rig.Phase);
    }

    private sealed class ThrowingTranscriber(Exception error) : ITranscriber
    {
        public Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct = default) => throw error;
    }
}
