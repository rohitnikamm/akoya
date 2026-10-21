using Akoya.App.Setup;
using Akoya.Core.Dictation;
using Akoya.Core.Mics;
using Xunit;

namespace Akoya.App.Tests.Setup;

public class SetupViewModelTests
{
    private static SetupRig Laptop3() => new(Devices.Laptop(), Devices.Buds(), Devices.Webcam());

    private static SetupRig AtMicrophone(SetupRig rig)
    {
        rig.Model.GetStarted();
        return rig;
    }

    private static SetupRig AtTryIt(SetupRig rig)
    {
        rig.Model.GetStarted();
        rig.Model.Continue();
        return rig;
    }

    [Fact]
    public void StartsOnWelcome_WithoutMetering()
    {
        var rig = Laptop3();

        Assert.Equal(SetupStep.Welcome, rig.Model.Step);
        Assert.Equal(0, rig.Model.PageIndex);
        Assert.False(rig.Model.ShowsSkip);
        Assert.False(rig.Levels.Running);
    }

    [Fact]
    public void GetStarted_ShowsEveryMicByPlainName_WindowsDefaultHighlighted_MetersRunning()
    {
        var rig = AtMicrophone(Laptop3());
        var vm = rig.Model;

        Assert.Equal(SetupStep.Microphone, vm.Step);
        Assert.Equal(1, vm.PageIndex);
        Assert.True(vm.ShowsSkip);
        Assert.Equal(["This PC's microphone", "Galaxy Buds", "Webcam mic"], vm.Mics.Select(m => m.Name));
        Assert.Empty(vm.MoreMics);
        Assert.Equal("laptop", vm.HighlightedId);
        Assert.True(vm.Mics[0].IsHighlighted);
        Assert.Equal(["laptop", "buds", "cam"], rig.Levels.Started);
        Assert.Equal("Which mic do you talk into?", vm.MicTitle);
        Assert.Equal("Say something. Pick the one that moves.", vm.MicBody);
        Assert.Equal(MicStatusKind.None, vm.Status);
    }

    [Fact]
    public void Meter_ShowsEachMicsLevel()
    {
        var rig = AtMicrophone(Laptop3());

        rig.Levels40ms(("laptop", 8), ("buds", 1), ("cam", 3));

        Assert.Equal([10, 0, 3], rig.Model.Mics.Select(m => m.LitDots));
        Assert.Equal(10, rig.Model.Mics[0].MeterOpacities.Length);
        Assert.Equal(0.95, rig.Model.Mics[2].MeterOpacities[2]);
        Assert.Equal(0.12, rig.Model.Mics[2].MeterOpacities[3]);
    }

    [Fact]
    public void HighlightedMicHearsSpeech_HearingYou()
    {
        var rig = AtMicrophone(Laptop3());

        rig.Talk(0.4, ("laptop", 6), ("buds", 1), ("cam", 3));

        Assert.Equal(MicStatusKind.HearingYou, rig.Model.Status);
        Assert.Equal("Hearing you", rig.Model.StatusText);
    }

    [Fact]
    public void NothingSaid_NoStatus_EvenAfterAWhile()
    {
        var rig = AtMicrophone(Laptop3());

        rig.Talk(5, ("laptop", 1), ("buds", 1), ("cam", 2));

        Assert.Equal(MicStatusKind.None, rig.Model.Status);
        Assert.Equal("", rig.Model.StatusText);
    }

    [Fact]
    public void DefaultIsQuietWhileAnotherMicHears_AmberAfterThreeSeconds_HighlightNeverMoves()
    {
        var rig = AtMicrophone(new SetupRig(Devices.Laptop(isDefault: false), Devices.Buds(isDefault: true), Devices.Webcam()));
        Assert.Equal("buds", rig.Model.HighlightedId);

        rig.Talk(2.8, ("laptop", 7), ("buds", 1), ("cam", 3));
        Assert.Equal(MicStatusKind.None, rig.Model.Status);

        rig.Talk(0.4, ("laptop", 7), ("buds", 1), ("cam", 3));
        Assert.Equal(MicStatusKind.NotHearing, rig.Model.Status);
        Assert.Equal("Galaxy Buds isn't hearing you", rig.Model.StatusText);

        rig.Talk(10, ("laptop", 7), ("buds", 1), ("cam", 3));
        Assert.Equal("buds", rig.Model.HighlightedId);
        Assert.True(rig.Model.Mics.Single(m => m.Id == "buds").IsHighlighted);
    }

    [Fact]
    public void ClickingTheRowThatMoves_MovesTheHighlight_AndClearsTheStatus()
    {
        var rig = AtMicrophone(new SetupRig(Devices.Laptop(isDefault: false), Devices.Buds(isDefault: true), Devices.Webcam()));
        rig.Talk(3.2, ("laptop", 7), ("buds", 1));
        Assert.Equal(MicStatusKind.NotHearing, rig.Model.Status);

        rig.Model.Mics[0].SelectCommand.Execute(null);

        Assert.Equal("laptop", rig.Model.HighlightedId);
        Assert.True(rig.Model.Mics[0].IsHighlighted);
        Assert.False(rig.Model.Mics[1].IsHighlighted);
        Assert.Equal(MicStatusKind.None, rig.Model.Status);

        rig.Levels40ms(("laptop", 7), ("buds", 1));
        Assert.Equal(MicStatusKind.HearingYou, rig.Model.Status);
    }

    [Fact]
    public void HearingYou_StaysBetweenWords()
    {
        var rig = AtMicrophone(Laptop3());
        rig.Talk(0.4, ("laptop", 6));

        rig.Talk(1.5, ("laptop", 1));

        Assert.Equal(MicStatusKind.HearingYou, rig.Model.Status);
    }

    [Fact]
    public void OneMic_SameScreenOneRow()
    {
        var rig = AtMicrophone(new SetupRig(Devices.Laptop()));

        Assert.Single(rig.Model.Mics);
        Assert.Equal("Is this the mic you want?", rig.Model.MicTitle);
        Assert.Equal("Say something. The dots should move.", rig.Model.MicBody);
        Assert.Single(rig.Model.ListItems);
    }

    [Fact]
    public void VirtualAndLoopbackInputs_FoldIntoMoreAtTheBottom_AndExpandInPlace()
    {
        var rig = AtMicrophone(new SetupRig(Devices.HyperX(), Devices.Webcam("brio"), Devices.Nvidia(), Devices.StereoMix()));
        var vm = rig.Model;

        Assert.Equal(["HyperX Cloud III", "Webcam mic"], vm.Mics.Select(m => m.Name));
        Assert.Equal(2, vm.MoreMics.Count);
        Assert.False(vm.MoreExpanded);
        Assert.Equal(3, vm.ListItems.Count);
        var more = Assert.IsType<MoreRow>(vm.ListItems[^1]);
        Assert.Equal("More (2)", more.Label);
        Assert.Equal(4, rig.Levels.Started!.Count); // virtual inputs still show a live meter

        more.ToggleCommand.Execute(null);

        Assert.True(vm.MoreExpanded);
        Assert.Equal(4, vm.ListItems.Count);
        Assert.DoesNotContain(vm.ListItems, i => i is MoreRow);
        Assert.Equal(vm.MoreMics, vm.ListItems.Skip(2));

        vm.ToggleMore();
        Assert.False(vm.MoreExpanded);
        Assert.Equal(3, vm.ListItems.Count);
    }

    [Fact]
    public void ListRows_HaveSeparatorsBetweenThem_NotAboveTheFirst()
    {
        var rig = AtMicrophone(new SetupRig(Devices.HyperX(), Devices.Webcam("brio"), Devices.Nvidia()));

        Assert.Equal([false, true, true], rig.Model.ListItems.Select(i => ((ListRow)i).ShowsSeparator));
    }

    [Fact]
    public void WindowsDefaultInMore_StartsExpanded()
    {
        var rig = AtMicrophone(new SetupRig(Devices.HyperX(isDefault: false), Devices.Nvidia(isDefault: true)));

        Assert.True(rig.Model.MoreExpanded);
        Assert.Equal("nvidia", rig.Model.HighlightedId);
    }

    [Fact]
    public void Blocked_ShowsTurnOn_WaitsForTheSwitch_ThenMovesOnByItself()
    {
        var rig = new SetupRig(Devices.Laptop(), Devices.Buds());
        rig.Privacy.Allowed = false;
        rig.Model.GetStarted();

        Assert.True(rig.Model.IsBlocked);
        Assert.False(rig.Levels.Running);

        rig.Model.OpenSettings();
        Assert.Equal(1, rig.Privacy.Opened);

        rig.Clock.Advance(2);
        Assert.True(rig.Model.IsBlocked);

        rig.Privacy.Allowed = true;
        rig.Clock.Advance(0.5);

        Assert.False(rig.Model.IsBlocked);
        Assert.Equal(SetupStep.Microphone, rig.Model.Step);
        Assert.True(rig.Levels.Running);
    }

    [Fact]
    public void PrimaryAction_WhileBlocked_OpensSettings()
    {
        var rig = new SetupRig(Devices.Laptop());
        rig.Privacy.Allowed = false;
        rig.Model.GetStarted();

        rig.Model.PrimaryAction();

        Assert.Equal(1, rig.Privacy.Opened);
        Assert.Equal(SetupStep.Microphone, rig.Model.Step);
    }

    [Fact]
    public void Continue_SavesTheHighlightedMic_StopsMeters_StartsPractice()
    {
        var rig = AtMicrophone(Laptop3());
        rig.Model.SelectMic("cam");
        string? chosen = null;
        rig.Model.MicChosen += id => chosen = id;

        rig.Model.Continue();

        Assert.Equal("cam", chosen);
        Assert.False(rig.Levels.Running);
        Assert.Equal(SetupStep.TryIt, rig.Model.Step);
        Assert.Equal(2, rig.Model.PageIndex);
        Assert.NotNull(rig.Practice.OnText);
    }

    [Fact]
    public void Continue_EvenWithTheDefaultUntouched_SavesItsId()
    {
        var rig = AtMicrophone(Laptop3());
        string? chosen = null;
        rig.Model.MicChosen += id => chosen = id;

        rig.Model.Continue();

        Assert.Equal("laptop", chosen);
    }

    [Fact]
    public void CatalogChange_KeepsTheHighlight_AndRestartsMeters()
    {
        var rig = AtMicrophone(Laptop3());
        rig.Model.SelectMic("buds");
        var starts = rig.Levels.Starts;

        rig.Catalog.Change(Devices.Laptop(), Devices.Buds(), Devices.Webcam(), Devices.HyperX(isDefault: false));

        Assert.Equal("buds", rig.Model.HighlightedId);
        Assert.Equal(4, rig.Model.Mics.Count);
        Assert.Equal(starts + 1, rig.Levels.Starts);
        Assert.Equal(4, rig.Levels.Started!.Count);
    }

    [Fact]
    public void CatalogChange_HighlightedMicUnplugged_HighlightsTheNewDefault()
    {
        var rig = AtMicrophone(Laptop3());
        rig.Model.SelectMic("buds");

        rig.Catalog.Change(Devices.Laptop(), Devices.Webcam());

        Assert.Equal("laptop", rig.Model.HighlightedId);
    }

    [Fact]
    public void TryIt_LivePartialsThenFinal_FillTheCard_DoneBecomesReady()
    {
        var rig = AtTryIt(Laptop3());
        var vm = rig.Model;
        Assert.Equal(PracticeCardKind.Placeholder, vm.CardKind);
        Assert.Equal("Your words will appear here.", vm.CardText);
        Assert.False(vm.DoneReady);

        rig.Practice.Phase(DictationPhase.Recording);
        Assert.True(vm.IsRecording);
        rig.Practice.Text("Okay, this is", false);
        Assert.Equal(PracticeCardKind.Text, vm.CardKind);
        Assert.Equal("Okay, this is", vm.CardText);
        Assert.False(vm.DoneReady);

        rig.Practice.Phase(DictationPhase.Transcribing);
        rig.Practice.Text("Okay, this is actually really smooth.", true);
        rig.Practice.Phase(DictationPhase.Idle);

        Assert.Equal("Okay, this is actually really smooth.", vm.PracticeText);
        Assert.True(vm.DoneReady);
        Assert.False(vm.IsRecording);
    }

    [Fact]
    public void TryIt_WaveFollowsTheVoiceWhileRecording()
    {
        var rig = AtTryIt(Laptop3());
        var rest = rig.Model.WaveOpacities;
        Assert.Equal(23 * 9, rest.Length);

        rig.Practice.Phase(DictationPhase.Recording);
        rig.Practice.Rms(0.2f);

        var live = rig.Model.WaveOpacities;
        Assert.Equal(1.0, live[22 * 9 + 4]);
        Assert.True(live.Skip(22 * 9).Count(o => o > 0) > 1);
    }

    [Fact]
    public void TryIt_TakeLostWithoutAFinal_FallsBackToIdle()
    {
        // The recorder's Stop threw: the coordinator goes back to Idle and no final text arrives.
        var rig = AtTryIt(Laptop3());
        rig.Practice.Phase(DictationPhase.Recording);
        rig.Practice.Text("Okay this", false);

        rig.Practice.Phase(DictationPhase.Idle);

        Assert.False(rig.Model.IsRecording);
        Assert.Equal(PracticeCardKind.RetryHint, rig.Model.CardKind);
        Assert.Equal("Didn't catch that. Try again.", rig.Model.CardText);
        Assert.False(rig.Model.DoneReady);
    }

    [Fact]
    public void TryIt_SilentTake_ChangesNothing()
    {
        var rig = AtTryIt(Laptop3());
        rig.Practice.Phase(DictationPhase.Recording);
        rig.Practice.Phase(DictationPhase.Idle);

        Assert.Equal(PracticeCardKind.Placeholder, rig.Model.CardKind);
    }

    [Fact]
    public void TryIt_ModelStillDownloading_CardShowsProgress()
    {
        var rig = AtTryIt(Laptop3());

        rig.Model.SetModel(ModelState.Loading, 0.624);

        Assert.Equal(0.624, rig.Model.ModelProgress);
        Assert.Equal(PracticeCardKind.ModelLoading, rig.Model.CardKind);
        Assert.Equal("Getting the speech model ready… 62%", rig.Model.CardText);
        Assert.True(rig.Model.IsHintDimmed);

        rig.Model.SetModel(ModelState.Ready);
        Assert.Null(rig.Model.ModelProgress);
        Assert.Equal(PracticeCardKind.Placeholder, rig.Model.CardKind);
        Assert.False(rig.Model.IsHintDimmed);
    }

    [Fact]
    public void TryIt_ModelFailed_OffersRetry()
    {
        var rig = AtTryIt(Laptop3());
        var retries = 0;
        rig.Model.RetryModelRequested += () => retries++;

        rig.Model.SetModel(ModelState.Failed);
        Assert.Equal(PracticeCardKind.ModelFailed, rig.Model.CardKind);
        Assert.Equal("Model download failed", rig.Model.CardText);

        rig.Model.RetryModelCommand.Execute(null);
        Assert.Equal(1, retries);
    }

    [Fact]
    public void Done_BeforeTheFirstTranscript_DoesNothing()
    {
        var rig = AtTryIt(Laptop3());
        var finished = 0;
        rig.Model.Finished += () => finished++;

        rig.Model.Done();

        Assert.Equal(0, finished);
        Assert.Equal(SetupStep.TryIt, rig.Model.Step);
    }

    [Fact]
    public void Done_AfterATranscript_FinishesAndStopsPractice()
    {
        var rig = AtTryIt(Laptop3());
        var finished = 0;
        rig.Model.Finished += () => finished++;
        rig.Practice.Phase(DictationPhase.Recording);
        rig.Practice.Text("Hello there.", true);
        rig.Practice.Phase(DictationPhase.Idle);

        rig.Model.PrimaryAction();

        Assert.Equal(1, finished);
        Assert.Equal(1, rig.Practice.Ends);
    }

    [Theory]
    [InlineData(SetupStep.Welcome)]
    [InlineData(SetupStep.Microphone)]
    [InlineData(SetupStep.TryIt)]
    public void Skip_OnAnyStep_FinishesAndTearsDown(SetupStep step)
    {
        var rig = Laptop3();
        if (step >= SetupStep.Microphone) rig.Model.GetStarted();
        if (step >= SetupStep.TryIt) rig.Model.Continue();
        var finished = 0;
        rig.Model.Finished += () => finished++;

        rig.Model.Skip();
        rig.Model.Skip();

        Assert.Equal(1, finished);
        Assert.False(rig.Levels.Running);
        Assert.Equal(0, rig.Clock.Pending);
        Assert.Equal(step == SetupStep.TryIt ? 1 : 0, rig.Practice.Ends);
    }

    [Fact]
    public void Dispose_TearsDownWithoutFinishing()
    {
        var rig = AtMicrophone(Laptop3());
        var finished = 0;
        rig.Model.Finished += () => finished++;

        rig.Model.Dispose();

        Assert.Equal(0, finished);
        Assert.False(rig.Levels.Running);
        Assert.Equal(0, rig.Clock.Pending);
    }

    [Fact]
    public void PrimaryAction_WalksTheSteps()
    {
        var rig = Laptop3();
        rig.Model.PrimaryAction();
        Assert.Equal(SetupStep.Microphone, rig.Model.Step);
        rig.Model.PrimaryAction();
        Assert.Equal(SetupStep.TryIt, rig.Model.Step);
    }

    [Fact]
    public void HotkeyLabel_DefaultsToRightAlt()
    {
        Assert.Equal("Right Alt", Laptop3().Model.HotkeyLabel);
    }

    [Fact]
    public void SpeechOnset_AfterLongSilence_OtherMicFirstBy200ms_NeverAmber()
    {
        var rig = AtMicrophone(Laptop3());
        var seen = new List<MicStatusKind>();
        rig.Model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SetupViewModel.Status)) seen.Add(rig.Model.Status); };

        rig.Talk(10, ("laptop", 1), ("buds", 1), ("cam", 1));
        rig.Talk(0.2, ("laptop", 1), ("buds", 1), ("cam", 6));
        rig.Talk(5, ("laptop", 6), ("buds", 1), ("cam", 6));

        Assert.DoesNotContain(MicStatusKind.NotHearing, seen);
        Assert.Equal(MicStatusKind.HearingYou, rig.Model.Status);
    }

    [Fact]
    public void LoopbackInputHearingSound_NobodySpeaking_NoChip()
    {
        var rig = AtMicrophone(new SetupRig(Devices.Laptop(), Devices.StereoMix()));
        var seen = new List<MicStatusKind>();
        rig.Model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SetupViewModel.Status)) seen.Add(rig.Model.Status); };

        rig.Talk(10, ("laptop", 1), ("mix", 6));

        Assert.Empty(seen);
        Assert.Equal(MicStatusKind.None, rig.Model.Status);
        Assert.Equal(7, rig.Model.MoreMics[0].LitDots); // still metered
    }

    [Fact]
    public void Status_ClearsAfterThreeSecondsWithNoSpeechAnywhere()
    {
        var rig = AtMicrophone(Laptop3());
        rig.Talk(0.5, ("laptop", 6));
        Assert.Equal(MicStatusKind.HearingYou, rig.Model.Status);

        rig.Talk(3.2, ("laptop", 1), ("buds", 1), ("cam", 1));

        Assert.Equal(MicStatusKind.None, rig.Model.Status);
    }

    [Fact]
    public void Status_ClearsWhenMetersRestart()
    {
        var rig = AtMicrophone(Laptop3());
        rig.Talk(0.5, ("laptop", 6));
        Assert.Equal(MicStatusKind.HearingYou, rig.Model.Status);

        rig.Catalog.Change(Devices.Laptop(), Devices.Buds(), Devices.Webcam(), Devices.HyperX(isDefault: false));

        Assert.Equal(MicStatusKind.None, rig.Model.Status);
    }

    [Fact]
    public void NoMics_ContinueIsInert()
    {
        var rig = AtMicrophone(new SetupRig());
        var chosen = 0;
        rig.Model.MicChosen += _ => chosen++;

        Assert.False(rig.Model.CanContinue);
        Assert.Equal("No mic found", rig.Model.MicTitle);
        rig.Model.Continue();
        rig.Model.PrimaryAction();

        Assert.Equal(SetupStep.Microphone, rig.Model.Step);
        Assert.Equal(0, chosen);

        rig.Catalog.Change(Devices.Laptop());
        Assert.True(rig.Model.CanContinue);
    }

    [Fact]
    public void TryIt_LostSecondTake_KeepsThePreviousGoodText()
    {
        var rig = AtTryIt(Laptop3());
        rig.Practice.Phase(DictationPhase.Recording);
        rig.Practice.Text("First take.", true);
        rig.Practice.Phase(DictationPhase.Idle);

        rig.Practice.Phase(DictationPhase.Recording);
        rig.Practice.Text("Second", false);
        rig.Practice.Phase(DictationPhase.Idle); // Stop threw: no final
        Assert.Equal("First take.", rig.Model.CardText);
        Assert.Equal(PracticeCardKind.Text, rig.Model.CardKind);

        rig.Practice.Phase(DictationPhase.Recording);
        rig.Practice.Text("", true); // empty final
        rig.Practice.Phase(DictationPhase.Idle);
        Assert.Equal("First take.", rig.Model.CardText);
        Assert.True(rig.Model.DoneReady);
    }
}
