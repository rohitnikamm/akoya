using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Akoya.App.Tests.Snapshots;
using Akoya.App.Tray;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Xunit;

namespace Akoya.App.Tests;

/// <summary>Focus goes back to the field the user was typing in when the tray panel closes (Rohit's report).</summary>
[Collection(DesktopCollection.Name)]
public class TrayFocusTests
{
    private static readonly IntPtr Editor = new(0x1234);
    private static readonly IntPtr Taskbar = new(0x5678);

    /// <summary>Seams for <see cref="PanelFocus"/>: the foreground, the last outside window, and what got activated.</summary>
    private sealed class FocusRig
    {
        public IntPtr Foreground { get; set; } = Taskbar; // a tray click makes the taskbar the foreground
        public IntPtr LastOutside { get; set; } = Editor;
        public HashSet<IntPtr> Gone { get; } = [];
        public List<IntPtr> Activated { get; } = [];

        public PanelFocus Create() => new(
            () => Foreground,
            () => LastOutside,
            hwnd => hwnd != IntPtr.Zero && hwnd != Taskbar && !Gone.Contains(hwnd),
            Activated.Add);
    }

    [Fact]
    public void PanelFocus_OpenedFromTheTray_ReturnsToTheWindowBeforeTheTaskbar()
    {
        var rig = new FocusRig();
        var focus = rig.Create();

        focus.Opening();
        focus.Restore();

        Assert.Equal(new[] { Editor }, rig.Activated);
        focus.Restore(); // only once
        Assert.Single(rig.Activated);
    }

    [Fact]
    public void PanelFocus_OpenedWhileAnAppIsForeground_ReturnsToThatApp()
    {
        var other = new IntPtr(0x9999);
        var rig = new FocusRig { Foreground = other };
        var focus = rig.Create();

        focus.Opening();
        focus.Restore();

        Assert.Equal(new[] { other }, rig.Activated);
    }

    [Fact]
    public void PanelFocus_TheWindowClosedMeanwhile_ActivatesNothing()
    {
        var rig = new FocusRig();
        var focus = rig.Create();
        focus.Opening();
        rig.Gone.Add(Editor);

        focus.Restore();

        Assert.Empty(rig.Activated);
    }

    [Fact]
    public void Window_EscGivesFocusBack_ClickingAnotherWindowDoesNot()
    {
        SnapshotHarness.RunSta(() =>
        {
            var rig = new FocusRig();
            var window = new TrayPanelWindow(TrayPanelSnapshots.Model(_ => { }), rig.Create());

            window.ShowNearTray();
            PressEscape(window);
            Assert.False(window.IsVisible);
            Assert.Equal(new[] { Editor }, rig.Activated);

            window.ShowNearTray();
            window.HandleDeactivated(); // the user clicked another window: focus stays where they clicked
            Assert.False(window.IsVisible);
            Assert.Single(rig.Activated);

            window.Close();
        });
    }

    [Fact]
    public void Window_TrayClickThatClosesIt_GivesFocusBack()
    {
        SnapshotHarness.RunSta(() =>
        {
            var rig = new FocusRig();
            var window = new TrayPanelWindow(TrayPanelSnapshots.Model(_ => { }), rig.Create());

            window.Toggle();
            window.HandleDeactivated(); // the click on the tray icon deactivated it first…
            window.Toggle();            // …and then arrives at the icon
            Assert.False(window.IsVisible);
            Assert.Equal(new[] { Editor }, rig.Activated);

            window.Close();
        });
    }

    [Fact]
    public void Window_CloseForDictation_HidesAndGivesFocusBack_OnlyWhenOpen()
    {
        SnapshotHarness.RunSta(() =>
        {
            var rig = new FocusRig();
            var window = new TrayPanelWindow(TrayPanelSnapshots.Model(_ => { }), rig.Create());

            window.CloseAndReturnFocus();
            Assert.Empty(rig.Activated); // not open: nothing to give back

            window.ShowNearTray();
            window.CloseAndReturnFocus();
            Assert.False(window.IsVisible);
            Assert.Equal(new[] { Editor }, rig.Activated);

            window.Close();
        });
    }

    [Fact]
    public void Host_HotkeyWhileThePanelIsOpen_ClosesItBeforeTheRecordingStarts()
    {
        UiThread.Run(() =>
        {
            using var rig = new HostRig();
            var host = rig.Start();
            var events = new List<string>();
            rig.Tray.OnClosePanel = () => events.Add("close panel, phase " + host.Coordinator.Phase);
            rig.Pill.OnShow = () => events.Add("pill");

            rig.Hook.Fire(HotkeyEvent.Pressed);

            Assert.Equal(new[] { "close panel, phase Idle", "pill" }, events);
            Assert.Equal(DictationPhase.Recording, host.Coordinator.Phase);

            rig.Hook.Fire(HotkeyEvent.Released); // releases don't touch the panel
            Assert.Single(events, e => e.StartsWith("close panel", StringComparison.Ordinal));
        });
    }

    private static void PressEscape(TrayPanelWindow window)
    {
        var source = PresentationSource.FromVisual(window)!;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(args);
    }
}
