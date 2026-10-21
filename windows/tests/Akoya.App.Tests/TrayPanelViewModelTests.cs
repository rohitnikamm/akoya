using System.ComponentModel;
using Akoya.App.Tray;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Akoya.Core.Mics;
using Akoya.Core.Settings;
using Xunit;

namespace Akoya.App.Tests;

public class TrayPanelViewModelTests
{
    private static readonly NamedMic Laptop = new("{laptop}", "This PC's microphone", InMore: false, IsDefault: true);
    private static readonly NamedMic HyperX = new("{hyperx}", "HyperX Cloud III", InMore: false, IsDefault: false);
    private static readonly NamedMic Broadcast = new("{nvidia}", "NVIDIA Broadcast", InMore: true, IsDefault: false);
    private static readonly NamedMic[] Mics = [Laptop, HyperX, Broadcast];

    private static TrayPanelViewModel Ready(TrayPanelActions? actions = null, IScheduler? scheduler = null)
    {
        var vm = new TrayPanelViewModel(actions, scheduler);
        vm.SetMicrophones(Mics, savedId: null);
        return vm;
    }

    [Fact]
    public void Defaults_ReadyWithRightAlt()
    {
        var vm = Ready();

        Assert.Equal("Ready", vm.StatusText);
        Assert.Equal(TrayStatusKind.Ready, vm.StatusKind);
        Assert.Equal(TrayStatusAction.None, vm.StatusAction);
        Assert.Null(vm.ProgressPercent);
        Assert.True(vm.StatusInHeader);
        Assert.Equal("Right Alt", vm.HotkeyLabel);
        Assert.False(vm.HasLastDictation);
        Assert.False(vm.UpdateReady);
        Assert.False(vm.LaunchAtStartup);
        Assert.Equal(0, vm.GlowStrength);
    }

    [Fact]
    public void HotkeyOptions_RightAltRightCtrlCopilot_CheckOnCurrent()
    {
        var vm = Ready();

        Assert.Equal(["Right Alt", "Right Ctrl", "Copilot key"], vm.HotkeyOptions.Select(o => o.Label));
        Assert.Equal([true, false, false], vm.HotkeyOptions.Select(o => o.IsSelected));
    }

    [Fact]
    public void SelectHotkey_UpdatesKeycap_ClosesMenu_CallsAction()
    {
        HotkeyKey? chosen = null;
        var vm = Ready(new TrayPanelActions { SelectHotkey = k => chosen = k });
        vm.ToggleHotkeyMenu.Execute(null);
        Assert.True(vm.IsHotkeyMenuOpen);

        vm.SelectHotkey.Execute(vm.HotkeyOptions[2]);

        Assert.Equal(HotkeyKey.Copilot, chosen);
        Assert.Equal("Copilot key", vm.HotkeyLabel);
        Assert.Equal([false, false, true], vm.HotkeyOptions.Select(o => o.IsSelected));
        Assert.False(vm.IsHotkeyMenuOpen);
    }

    [Fact]
    public void SelectHotkey_SameKey_NoAction()
    {
        var calls = 0;
        var vm = Ready(new TrayPanelActions { SelectHotkey = _ => calls++ });

        vm.SelectHotkey.Execute(HotkeyKey.RightAlt);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Menus_AreExclusive_AndCloseMenusClosesBoth()
    {
        var vm = Ready();

        Assert.False(vm.IsMenuOpen);
        vm.ToggleHotkeyMenu.Execute(null);
        Assert.True(vm.IsMenuOpen);
        vm.ToggleMicMenu.Execute(null);
        Assert.False(vm.IsHotkeyMenuOpen);
        Assert.True(vm.IsMicMenuOpen);
        Assert.True(vm.IsMenuOpen);

        vm.CloseMenus.Execute(null);
        Assert.False(vm.IsMicMenuOpen);
        Assert.False(vm.IsHotkeyMenuOpen);
        Assert.False(vm.IsMenuOpen);
    }

    [Theory]
    [InlineData(DictationPhase.Recording, "Listening", TrayStatusKind.Busy)]
    [InlineData(DictationPhase.Transcribing, "Transcribing", TrayStatusKind.Busy)]
    [InlineData(DictationPhase.Pasted, "Ready", TrayStatusKind.Ready)]
    [InlineData(DictationPhase.MicMissing, "Ready", TrayStatusKind.Ready)]
    [InlineData(DictationPhase.Idle, "Ready", TrayStatusKind.Ready)]
    public void SetPhase_Status(DictationPhase phase, string text, TrayStatusKind kind)
    {
        var vm = Ready();
        vm.SetPhase(phase);

        Assert.Equal(text, vm.StatusText);
        Assert.Equal(kind, vm.StatusKind);
    }

    [Fact]
    public void Downloading_WithPercent_MovesStatusUnderTheHint()
    {
        var vm = Ready();
        vm.SetModel(ModelState.Loading, 0.62);

        Assert.Equal("Getting the speech model ready… 62%", vm.StatusText);
        Assert.Equal(TrayStatusKind.Busy, vm.StatusKind);
        Assert.Equal(62, vm.ProgressPercent);
        Assert.Equal(0.62, vm.ProgressFraction, 3);
        Assert.True(vm.IsDownloading);
        Assert.False(vm.StatusInHeader);
    }

    [Fact]
    public void Loading_WithoutProgress_NoPercent()
    {
        var vm = Ready();
        vm.SetModel(ModelState.Loading);

        Assert.Equal("Getting the speech model ready…", vm.StatusText);
        Assert.Null(vm.ProgressPercent);
        Assert.False(vm.IsDownloading);
        Assert.False(vm.StatusInHeader);
        Assert.True(vm.StatusUnderHint);

        vm.SetModel(ModelState.Ready);
        Assert.True(vm.StatusInHeader);
    }

    [Fact]
    public void ModelFailed_ErrorWithRetry_RetryCallsAction()
    {
        var retries = 0;
        var vm = Ready(new TrayPanelActions { Retry = () => retries++ });
        vm.SetModel(ModelState.Failed);

        Assert.Equal("Model download failed", vm.StatusText);
        Assert.Equal(TrayStatusKind.Error, vm.StatusKind);
        Assert.True(vm.ShowRetry);
        vm.Retry.Execute(null);
        Assert.Equal(1, retries);

        vm.SetModel(ModelState.Ready);
        Assert.Equal("Ready", vm.StatusText);
        Assert.False(vm.ShowRetry);
    }

    [Fact]
    public void MicBlocked_WarningWithOpenSettings()
    {
        var opened = 0;
        var vm = Ready(new TrayPanelActions { OpenMicSettings = () => opened++ });
        vm.SetMicBlocked(true);

        Assert.Equal("Microphone blocked", vm.StatusText);
        Assert.Equal(TrayStatusKind.Warning, vm.StatusKind);
        Assert.True(vm.ShowOpenSettings);
        vm.OpenMicSettings.Execute(null);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void HotkeyUnavailable_ErrorWithRetry()
    {
        var vm = Ready();
        vm.SetHotkeyUnavailable(true);

        Assert.Equal("Hotkey unavailable", vm.StatusText);
        Assert.Equal(TrayStatusKind.Error, vm.StatusKind);
        Assert.True(vm.ShowRetry);
    }

    [Fact]
    public void Precedence_ListeningOverEverything_ThenHotkey_Blocked_Failed_Loading_MicMissing()
    {
        var vm = new TrayPanelViewModel();
        vm.SetMicrophones(Mics, savedId: "{gone}", savedName: "Galaxy Buds");
        vm.SetModel(ModelState.Loading, 0.5);
        vm.SetMicBlocked(true);
        vm.SetHotkeyUnavailable(true);
        vm.SetPhase(DictationPhase.Recording);
        Assert.Equal("Listening", vm.StatusText);

        vm.SetPhase(DictationPhase.Idle);
        Assert.Equal("Hotkey unavailable", vm.StatusText);
        vm.SetHotkeyUnavailable(false);
        Assert.Equal("Microphone blocked", vm.StatusText);
        vm.SetMicBlocked(false);
        Assert.Equal("Getting the speech model ready… 50%", vm.StatusText);
        vm.SetModel(ModelState.Ready);
        Assert.Equal("Microphone not detected", vm.StatusText);
        Assert.Equal(TrayStatusKind.Warning, vm.StatusKind);
    }

    [Fact]
    public void MicOptions_SystemDefaultFirst_ThenEveryInput_CheckOnSelected()
    {
        var vm = Ready();

        Assert.Equal(
            ["System default (This PC's microphone)", "This PC's microphone", "HyperX Cloud III", "NVIDIA Broadcast"],
            vm.MicOptions.Select(o => o.Label));
        Assert.Equal([true, false, false, false], vm.MicOptions.Select(o => o.IsSelected));
        Assert.Null(vm.MicOptions[0].Id);
        Assert.Equal("This PC's microphone", vm.MicLabel);
        Assert.False(vm.MicMissing);
    }

    [Fact]
    public void SelectMic_ChecksIt_ShowsItsName_CallsAction()
    {
        var chosen = new List<string?>();
        var vm = Ready(new TrayPanelActions { SelectMic = chosen.Add });
        vm.ToggleMicMenu.Execute(null);

        vm.SelectMic.Execute(vm.MicOptions[2]);

        Assert.Equal(["{hyperx}"], chosen);
        Assert.Equal("HyperX Cloud III", vm.MicLabel);
        Assert.Equal([false, false, true, false], vm.MicOptions.Select(o => o.IsSelected));
        Assert.False(vm.IsMicMenuOpen);

        vm.SelectMic.Execute(vm.MicOptions[0]);
        Assert.Equal(["{hyperx}", null], chosen);
        Assert.Equal("This PC's microphone", vm.MicLabel);
    }

    [Fact]
    public void SavedMicMissing_RowYellow_ShowsSavedName_NothingChecked()
    {
        var vm = new TrayPanelViewModel();
        vm.SetMicrophones([Laptop], savedId: "{hyperx}", savedName: "HyperX Cloud III");

        Assert.True(vm.MicMissing);
        Assert.Equal("HyperX Cloud III", vm.MicLabel);
        Assert.DoesNotContain(vm.MicOptions, o => o.IsSelected);
        Assert.Equal("Microphone not detected", vm.StatusText);
    }

    [Fact]
    public void NoMicsAtAll_FollowingWindows_Missing()
    {
        var vm = new TrayPanelViewModel();
        vm.SetMicrophones([], savedId: null);

        Assert.True(vm.MicMissing);
        Assert.Equal("System default", vm.MicLabel);
        Assert.Equal("System default", Assert.Single(vm.MicOptions).Label);
    }

    [Fact]
    public void SetSettings_ShowsHotkeyAndStartup_WithoutCallingBack()
    {
        var calls = 0;
        var vm = Ready(new TrayPanelActions { SetLaunchAtStartup = _ => ++calls > 0, SelectHotkey = _ => calls++ });

        vm.SetSettings(new AkoyaSettings(Hotkey: HotkeyKey.RightCtrl, LaunchAtStartup: true));

        Assert.Equal("Right Ctrl", vm.HotkeyLabel);
        Assert.True(vm.LaunchAtStartup);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void LaunchAtStartup_Set_CallsAction()
    {
        var values = new List<bool>();
        var vm = Ready(new TrayPanelActions { SetLaunchAtStartup = v => { values.Add(v); return true; } });

        vm.LaunchAtStartup = true;
        vm.LaunchAtStartup = true;
        vm.LaunchAtStartup = false;

        Assert.Equal([true, false], values);
    }

    [Fact]
    public void LastDictation_Copy_CallsAction_FlashesCopied()
    {
        var copied = new List<string>();
        var scheduler = new ManualScheduler();
        var vm = Ready(new TrayPanelActions { Copy = t => { copied.Add(t); return true; } }, scheduler);

        vm.Copy.Execute(null); // nothing yet
        Assert.Empty(copied);

        vm.SetLastDictation("Ship the release notes after lunch, then ping the team.");
        Assert.True(vm.HasLastDictation);
        vm.Copy.Execute(null);

        Assert.Equal(["Ship the release notes after lunch, then ping the team."], copied);
        Assert.Equal("Copied", vm.CopyLabel);
        scheduler.Advance(TimeSpan.FromSeconds(1.4));
        Assert.Equal("Copied", vm.CopyLabel);
        scheduler.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Equal("Copy", vm.CopyLabel);
    }

    [Fact]
    public void LastDictation_CopyFailsOrThrows_NoCopiedFeedback()
    {
        var scheduler = new ManualScheduler();
        var fails = Ready(new TrayPanelActions { Copy = _ => false }, scheduler);
        fails.SetLastDictation("Hello.");
        fails.Copy.Execute(null);
        Assert.Equal("Copy", fails.CopyLabel);

        var throws = Ready(new TrayPanelActions { Copy = _ => throw new InvalidOperationException("clipboard busy") }, scheduler);
        throws.SetLastDictation("Hello.");
        throws.Copy.Execute(null);
        Assert.Equal("Copy", throws.CopyLabel);
    }

    [Fact]
    public void LaunchAtStartup_ActionFailsOrThrows_SwitchReverts()
    {
        var changed = new List<string?>();
        var vm = Ready(new TrayPanelActions { SetLaunchAtStartup = _ => false });
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.LaunchAtStartup = true;
        Assert.False(vm.LaunchAtStartup);
        // On, then back off (and once more posted to the thread's context, when it has one, as xUnit's does).
        Assert.True(changed.Count(n => n == nameof(TrayPanelViewModel.LaunchAtStartup)) >= 2);

        var throws = Ready(new TrayPanelActions { SetLaunchAtStartup = _ => throw new UnauthorizedAccessException() });
        throws.SetSettings(new AkoyaSettings(LaunchAtStartup: true));
        throws.LaunchAtStartup = false;
        Assert.True(throws.LaunchAtStartup);
    }

    [Fact]
    public void LastDictation_BlankIsHidden()
    {
        var vm = Ready();
        vm.SetLastDictation("   ");
        Assert.False(vm.HasLastDictation);
    }

    [Fact]
    public void UpdateReady_RestartCallsAction()
    {
        var restarts = 0;
        var vm = Ready(new TrayPanelActions { RestartToUpdate = () => restarts++ });
        vm.SetUpdateReady(true);

        Assert.True(vm.UpdateReady);
        vm.RestartToUpdate.Execute(null);
        Assert.Equal(1, restarts);
    }

    [Fact]
    public void Quit_ClosesMenus_CallsAction()
    {
        var quits = 0;
        var vm = Ready(new TrayPanelActions { Quit = () => quits++ });
        vm.ToggleMicMenu.Execute(null);

        vm.Quit.Execute(null);

        Assert.Equal(1, quits);
        Assert.False(vm.IsMicMenuOpen);
    }

    [Fact]
    public void Glow_FollowsVoiceWhileListening_ZeroAtRest()
    {
        var vm = Ready();
        vm.SetVoiceLevel(1);
        Assert.Equal(0, vm.GlowStrength); // not listening

        vm.SetPhase(DictationPhase.Recording);
        Assert.Equal(0.25, vm.GlowStrength, 3); // floor 0.2 / 0.8
        Assert.Equal(1, vm.GlowBaseOpacity, 3);
        vm.SetVoiceLevel(1);
        Assert.Equal(1, vm.GlowStrength, 3);

        vm.SetPhase(DictationPhase.Transcribing);
        Assert.Equal(0, vm.GlowStrength);
        Assert.Equal(0, vm.GlowBaseOpacity);
    }

    [Fact]
    public void PropertyChanged_RaisedForDerivedProperties()
    {
        var vm = Ready();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SetModel(ModelState.Loading, 0.1);
        vm.SetLastDictation("hi");

        Assert.Contains(nameof(TrayPanelViewModel.StatusText), changed);
        Assert.Contains(nameof(TrayPanelViewModel.ProgressFraction), changed);
        Assert.Contains(nameof(TrayPanelViewModel.StatusInHeader), changed);
        Assert.Contains(nameof(TrayPanelViewModel.HasLastDictation), changed);
    }

    private sealed class ManualScheduler : IScheduler
    {
        private readonly List<(DateTimeOffset Due, Action Action, Handle Handle)> _pending = [];

        public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

        public IDisposable After(TimeSpan delay, Action action)
        {
            var handle = new Handle();
            _pending.Add((Now + delay, action, handle));
            return handle;
        }

        public void Advance(TimeSpan by)
        {
            Now += by;
            foreach (var entry in _pending.Where(p => p.Due <= Now).ToArray())
            {
                _pending.Remove(entry);
                if (!entry.Handle.Disposed) entry.Action();
            }
        }

        private sealed class Handle : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }
    }

    // ---- Pill row ----

    [Fact]
    public void PillRow_CollapsedByDefault_ShowsTheLook()
    {
        var vm = Ready();

        Assert.False(vm.IsPillOpen);
        Assert.Equal(WaveLook.Pixel, vm.PillLook);
        Assert.True(vm.IsPixel);
        Assert.False(vm.IsSolid);
        Assert.False(vm.RedTint);
        Assert.Equal("Pixel", vm.PillValue);
    }

    [Fact]
    public void TogglePillOptions_OpensAndCloses_ValueHiddenWhileOpen()
    {
        var vm = Ready();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.TogglePillOptions.Execute(null);
        Assert.True(vm.IsPillOpen);
        Assert.Null(vm.PillValue);
        Assert.Contains(nameof(TrayPanelViewModel.PillValue), changed);

        vm.TogglePillOptions.Execute(null);
        Assert.False(vm.IsPillOpen);
        Assert.Equal("Pixel", vm.PillValue);
    }

    [Fact]
    public void ChoosingSolid_CallsTheAction_UpdatesTheValue()
    {
        var chosen = new List<WaveLook>();
        var vm = Ready(new TrayPanelActions { SelectLook = chosen.Add });

        vm.IsSolid = true;

        Assert.Equal([WaveLook.Solid], chosen);
        Assert.Equal(WaveLook.Solid, vm.PillLook);
        Assert.False(vm.IsPixel);
        Assert.Equal("Solid", vm.PillValue);

        vm.IsPixel = true;
        vm.IsPixel = true; // a radio button re-checked: no second call
        vm.IsSolid = false; // the radio that was unchecked: ignored
        Assert.Equal([WaveLook.Solid, WaveLook.Pixel], chosen);
        Assert.Equal(WaveLook.Pixel, vm.PillLook);
    }

    [Fact]
    public void RedTintSwitch_CallsTheAction()
    {
        var calls = new List<bool>();
        var vm = Ready(new TrayPanelActions { SetRedTint = calls.Add });

        vm.RedTint = true;
        vm.RedTint = true;
        vm.RedTint = false;

        Assert.Equal([true, false], calls);
    }

    [Fact]
    public void SetSettings_ShowsThePillOptions_WithoutCallingBack()
    {
        var calls = 0;
        var vm = Ready(new TrayPanelActions { SelectLook = _ => calls++, SetRedTint = _ => calls++ });

        vm.SetSettings(new AkoyaSettings(Look: WaveLook.Solid, RedTint: true));

        Assert.Equal(0, calls);
        Assert.Equal(WaveLook.Solid, vm.PillLook);
        Assert.True(vm.IsSolid);
        Assert.True(vm.RedTint);
        Assert.Equal("Solid", vm.PillValue);
    }
}
