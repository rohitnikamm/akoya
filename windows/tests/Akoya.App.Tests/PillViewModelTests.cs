using System.ComponentModel;
using Akoya.App.Pill;
using Akoya.Core.Dictation;
using Akoya.Core.Settings;
using Akoya.Core.Ui;
using Xunit;

namespace Akoya.App.Tests;

public class PillViewModelTests
{
    [Theory]
    [InlineData(DictationPhase.MicMissing, "Microphone not detected", true)]
    [InlineData(DictationPhase.MicBlocked, "Turn on your mic in Settings", true)]
    [InlineData(DictationPhase.ModelFailed, "Speech model failed", true)]
    [InlineData(DictationPhase.PasteFailed, "Couldn't paste", true)]
    [InlineData(DictationPhase.ModelNotReady, "Getting the speech model ready…", false)]
    public void Apply_Alerts(DictationPhase phase, string text, bool red)
    {
        var vm = new PillViewModel();
        vm.Apply(phase);

        Assert.Equal(PillMode.Alert, vm.Mode);
        Assert.Equal(text, vm.AlertText);
        Assert.Equal(red, vm.IsRed);
        Assert.True(vm.IsActive);
    }

    [Fact]
    public void Apply_Copied_IsBlueWithHint()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Copied);

        Assert.Equal(PillMode.Copied, vm.Mode);
        Assert.Equal("Copied. Press Ctrl+V", vm.AlertText);
        Assert.False(vm.IsRed);
    }

    [Fact]
    public void Apply_Pasted()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Pasted);

        Assert.Equal(PillMode.Pasted, vm.Mode);
        Assert.Equal("Pasted", vm.AlertText);
        Assert.False(vm.IsRed);
    }

    [Fact]
    public void Apply_Recording_StartsListeningAtZero()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.MicMissing);
        vm.Apply(DictationPhase.Recording);

        Assert.Equal(PillMode.Listening, vm.Mode);
        Assert.Equal("0:00", vm.TimerText);
        Assert.False(vm.IsRed);
        Assert.Equal(23 * PillDots.Rows, vm.Opacities.Length);
        Assert.All(vm.Opacities, o => Assert.True(o <= 0.35 + 1e-9)); // quiet until levels arrive
    }

    [Fact]
    public void Apply_Idle_IsInactive()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);
        vm.Apply(DictationPhase.Idle);

        Assert.False(vm.IsActive);
    }

    [Fact]
    public void SetLevels_WhileListening_UsesListeningDots()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);
        int[] steps = [1, 8, 2];
        vm.SetLevels(steps);

        Assert.Equal(PillDots.Listening(steps), vm.Opacities);
    }

    [Fact]
    public void SetLevels_CopiesTheArray()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);
        var steps = new[] { 8, 8 };
        vm.SetLevels(steps);
        steps[0] = 1;

        Assert.Equal(PillDots.Listening([8, 8]), vm.Opacities);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(3.9, "0:03")]
    [InlineData(65, "1:05")]
    [InlineData(300, "5:00")]
    public void SetElapsed_FormatsTimer(double seconds, string text)
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);
        vm.SetElapsed(TimeSpan.FromSeconds(seconds));

        Assert.Equal(text, vm.TimerText);
    }

    [Fact]
    public void Transcribing_FreezesTimerAndScans()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);
        vm.SetElapsed(TimeSpan.FromSeconds(4));
        vm.Apply(DictationPhase.Transcribing);
        vm.SetElapsed(TimeSpan.FromSeconds(9));
        vm.SetLevels([8, 8, 8]);
        vm.Tick(0.3);

        Assert.Equal(PillMode.Transcribing, vm.Mode);
        Assert.Equal("0:04", vm.TimerText);
        Assert.Equal(PillDots.Scanner(0.3), vm.Opacities);
    }

    [Fact]
    public void Changes_RaisePropertyChanged()
    {
        var vm = new PillViewModel();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Apply(DictationPhase.MicMissing);

        Assert.Contains(nameof(PillViewModel.Mode), changed);
        Assert.Contains(nameof(PillViewModel.AlertText), changed);
        Assert.Contains(nameof(PillViewModel.IsRed), changed);
    }

    // ---- Pill options ----

    private static readonly int[] Levels = [1, 3, 5, 8, 2];

    [Fact]
    public void Defaults_PixelWhite_NoBars()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);

        Assert.Equal(WaveLook.Pixel, vm.Look);
        Assert.False(vm.RedTint);
        Assert.Null(vm.Bars);
        Assert.Equal(PillTint.Normal, vm.Tint);
    }

    [Fact]
    public void Solid_Listening_BarsFromTheLevels()
    {
        var vm = new PillViewModel();
        vm.SetOptions(WaveLook.Solid, redTint: false);
        vm.Apply(DictationPhase.Recording);
        vm.SetLevels(Levels);

        Assert.Equal(PillDots.SolidListening(Levels), vm.Bars);
        Assert.Equal(PillTint.Normal, vm.Tint);
    }

    [Fact]
    public void Solid_Transcribing_ScannerBars()
    {
        var vm = new PillViewModel();
        vm.SetOptions(WaveLook.Solid, redTint: false);
        vm.Apply(DictationPhase.Recording);
        vm.Apply(DictationPhase.Transcribing);
        vm.Tick(0.3);

        Assert.Equal(PillDots.SolidScanner(0.3), vm.Bars);
    }

    [Fact]
    public void RedTint_RedWhileListening_AmberWhileTranscribing_NormalOtherwise()
    {
        var vm = new PillViewModel();
        vm.SetOptions(WaveLook.Pixel, redTint: true);

        vm.Apply(DictationPhase.Recording);
        Assert.Equal(PillTint.Red, vm.Tint);
        vm.Apply(DictationPhase.Transcribing);
        Assert.Equal(PillTint.Amber, vm.Tint);
        vm.Apply(DictationPhase.Pasted);
        Assert.Equal(PillTint.Normal, vm.Tint);
        vm.Apply(DictationPhase.MicMissing);
        Assert.Equal(PillTint.Normal, vm.Tint);
        Assert.True(vm.IsRed); // the alert is still the red alert
    }

    [Fact]
    public void RedTint_LeavesTheRimGlowBlue()
    {
        var vm = new PillViewModel();
        vm.SetOptions(WaveLook.Solid, redTint: true);
        vm.Apply(DictationPhase.Recording);

        Assert.False(vm.IsRed);
    }

    [Fact]
    public void SetOptions_WhileListening_RedrawsAtOnce_KeepingTheLevels()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);
        vm.SetLevels(Levels);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SetOptions(WaveLook.Solid, redTint: true);

        Assert.Equal(PillDots.SolidListening(Levels), vm.Bars);
        Assert.Equal(PillTint.Red, vm.Tint);
        Assert.Contains(nameof(PillViewModel.Bars), changed);
        Assert.Contains(nameof(PillViewModel.Tint), changed);

        vm.SetOptions(WaveLook.Pixel, redTint: false);
        Assert.Null(vm.Bars);
        Assert.Equal(PillDots.Listening(Levels), vm.Opacities);
        Assert.Equal(PillTint.Normal, vm.Tint);
    }

    [Fact]
    public void SetOptions_Unchanged_RaisesNothing()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);
        var changed = 0;
        vm.PropertyChanged += (_, _) => changed++;

        vm.SetOptions(WaveLook.Pixel, redTint: false);

        Assert.Equal(0, changed);
    }
}
