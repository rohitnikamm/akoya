using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Akoya.App.Controls;
using Akoya.App.Pill;
using Akoya.App.Tests.Snapshots;
using Akoya.Core.Dictation;
using Akoya.Core.Ui;
using Xunit;

namespace Akoya.App.Tests;

public class PillWindowTests
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;

    [Fact]
    public void ShowFor_ClickThroughToolWindowThatNeverActivates_ThenFadeOutHides()
    {
        SnapshotHarness.RunSta(() =>
        {
            var window = new PillWindow();
            var vm = new PillViewModel();
            vm.Apply(DictationPhase.Recording);

            window.ShowFor(vm);

            Assert.True(window.IsVisible);
            Assert.False(window.ShowActivated);
            Assert.True(window.Topmost);
            Assert.False(window.ShowInTaskbar);
            Assert.Same(vm, window.DataContext);

            var hwnd = new WindowInteropHelper(window).Handle;
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            Assert.Equal(WsExNoActivate, style & WsExNoActivate);
            Assert.Equal(WsExToolWindow, style & WsExToolWindow);
            Assert.Equal(WsExTransparent, style & WsExTransparent);
            Assert.NotEqual(hwnd, GetForegroundWindow());

            // The pill sits fully inside the work area of its monitor (one monitor on the runner).
            Pump(TimeSpan.FromMilliseconds(400));
            var pill = window.PillScreenRectPx();
            var work = SystemParameters.WorkArea; // primary monitor, DIP
            var scale = VisualScale(window);
            // 52 DIP above the top of the bottom taskbar measured as if shown: the work area's bottom on the runner
            // (shown), the screen's bottom minus its thickness when it auto-hides.
            var taskbar = PillPlacer.TaskbarOn(MonitorFromWindow(IntPtr.Zero, 1));
            var taskbarTop = taskbar is { AutoHide: true } bar
                ? SystemParameters.PrimaryScreenHeight * scale - bar.Bounds.Height
                : work.Bottom * scale;
            Assert.InRange(pill.Bottom, taskbarTop - 52 * scale - 1, taskbarTop - 52 * scale + 1);
            Assert.InRange(pill.Left + pill.Width / 2, work.Left * scale + work.Width * scale / 2 - 1.5, work.Left * scale + work.Width * scale / 2 + 1.5);

            window.FadeOut();
            Assert.True(window.IsVisible); // still fading out
            Pump(TimeSpan.FromMilliseconds(600));
            Assert.False(window.IsVisible);

            // Showing again during or after the fade brings it back.
            window.ShowFor(vm);
            window.FadeOut();
            window.ShowFor(vm);
            Pump(TimeSpan.FromMilliseconds(600));
            Assert.True(window.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void TaskbarOn_PrimaryMonitor_FindsTheBottomTaskbarAtTheWorkAreasBottom()
    {
        SnapshotHarness.RunSta(() =>
        {
            var monitor = MonitorFromWindow(IntPtr.Zero, 1); // primary
            var taskbar = PillPlacer.TaskbarOn(monitor);

            Assert.NotNull(taskbar);
            var bounds = taskbar.Value.Bounds;
            Assert.True(bounds.Width > bounds.Height, $"not horizontal: {bounds}");
            // Shown at the bottom (the runner): its top is the work area's bottom. Auto-hidden (some PCs): its top is
            // somewhere between its shown place and just above the screen's bottom edge. SystemParameters is DIP.
            var scale = System.Windows.Media.VisualTreeHelper.GetDpi(new System.Windows.Controls.Border()).DpiScaleX;
            var screenBottom = SystemParameters.PrimaryScreenHeight * scale;
            if (taskbar.Value.AutoHide)
                Assert.InRange(bounds.Y, screenBottom - bounds.Height - 1, screenBottom);
            else
                Assert.InRange(bounds.Y, SystemParameters.WorkArea.Bottom * scale - 1, SystemParameters.WorkArea.Bottom * scale + 1);
        });
    }

    [Fact]
    public void Constructor_PrewarmsTheGlowsOfTheSizesItShows()
    {
        SnapshotHarness.RunSta(() =>
        {
            var window = new PillWindow();
            Assert.True(window.PrewarmTask.Wait(TimeSpan.FromSeconds(10)), "prewarm did not finish");
            var scale = RimGlowCache.ScaleFor(window);
            var misses = RimGlowCache.MissesOnCurrentThread;

            foreach (var phase in new[] { DictationPhase.Recording, DictationPhase.Pasted, DictationPhase.MicMissing })
            {
                var vm = new PillViewModel();
                vm.Apply(phase);
                window.ShowFor(vm);
                Pump(TimeSpan.FromMilliseconds(50));
                var size = window.PillSizeDip;
                Assert.True(RimGlowCache.Contains(size.Width, size.Height, 17, scale, RimGlowRecipes.PillBlue), $"{phase} blue not cached ({size})");
                Assert.True(RimGlowCache.Contains(size.Width, size.Height, 17, scale, RimGlowRecipes.PillRed), $"{phase} red not cached ({size})");
            }
            Assert.True(RimGlowCache.Contains(18, 18, 9, scale, PillView.CheckHalo));
            Assert.True(RimGlowCache.Contains(18, 18, 9, scale, PillView.AlertHalo));
            Assert.Equal(misses, RimGlowCache.MissesOnCurrentThread); // nothing was rasterised while rendering

            window.Close();
        });
    }

    [Fact]
    public void ShowFor_FromHidden_PopsIn_ThenSettles_WhileTheModelUpdatesAtOnce()
    {
        SnapshotHarness.RunSta(() =>
        {
            // The CI runner has Windows' animation effects off.
            var previous = PillWindow.AnimationsEnabled;
            PillWindow.AnimationsEnabled = () => true;
            using var restore = new Restore(() => PillWindow.AnimationsEnabled = previous);
            var window = new PillWindow();
            var vm = new PillViewModel();
            vm.Apply(DictationPhase.Recording);

            window.ShowFor(vm);
            Assert.True(window.IsPoppingIn);
            var view = window.PillVisual;
            Assert.True(view.PopInProgress < 0.2);

            // The dictation isn't held up: the timer moves while the pill is still popping in.
            vm.SetElapsed(TimeSpan.FromSeconds(1));
            Assert.Equal("0:01", vm.TimerText);

            Pump(TimeSpan.FromMilliseconds(200));
            Assert.True(window.IsPoppingIn);
            Assert.InRange(view.PopInProgress, 0.2, 0.999);

            Pump(TimeSpan.FromMilliseconds(500));
            Assert.False(window.IsPoppingIn);
            Assert.Equal(1, view.PopInProgress);
            Assert.Equal(new PillPopInFrame(window.PillSizeDip.Width, 1, 1, 1, 1), view.PopInFrame);

            // Showing again while it is up doesn't pop it in again.
            window.ShowFor(vm);
            Assert.False(window.IsPoppingIn);

            window.Close();
        });
    }

    [Fact]
    public void ShowFor_AnimationEffectsOff_PlainFade_NoPopIn()
    {
        SnapshotHarness.RunSta(() =>
        {
            var previous = PillWindow.AnimationsEnabled;
            PillWindow.AnimationsEnabled = () => false;
            try
            {
                var window = new PillWindow();
                var vm = new PillViewModel();
                vm.Apply(DictationPhase.Recording);

                window.ShowFor(vm);
                Assert.False(window.IsPoppingIn);
                var root = (FrameworkElement)window.Content;
                Assert.True(root.Opacity < 0.5);

                Pump(TimeSpan.FromMilliseconds(500));
                Assert.Equal(1, root.Opacity, 3);
                Assert.Equal(1, window.PillVisual.PopInProgress);
                window.Close();
            }
            finally
            {
                PillWindow.AnimationsEnabled = previous;
            }
        });
    }

    [Fact]
    public void PillView_PopInPose_DrawsNarrowerScaledAndFaded_LayoutUnchanged()
    {
        SnapshotHarness.RunSta(() =>
        {
            var view = new PillView { DataContext = Listening() };
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            view.Arrange(new Rect(view.DesiredSize));
            var width = view.ActualWidth;

            view.PopInProgress = 0.24;
            Assert.Equal(PillPopIn.At(0.24, width, 34), view.PopInFrame);
            Assert.Equal(34, view.PopInFrame.Width, 6);
            Assert.Equal(width, view.ActualWidth); // the layout keeps the settled size

            view.PopInProgress = 0.64;
            Assert.Equal(1.07 * width, view.PopInFrame.Width, 6);

            view.PopInProgress = 1;
            Assert.Equal(new PillPopInFrame(width, 1, 1, 1, 1), view.PopInFrame);
        });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private static PillViewModel Listening()
    {
        var vm = new PillViewModel();
        vm.Apply(DictationPhase.Recording);
        return vm;
    }

    private static double VisualScale(System.Windows.Media.Visual visual) => System.Windows.Media.VisualTreeHelper.GetDpi(visual).DpiScaleX;

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
}
