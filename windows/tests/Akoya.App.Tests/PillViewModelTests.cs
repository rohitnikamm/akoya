using System.ComponentModel;
using Akoya.App.Pill;
using Akoya.Core.Dictation;
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
}
