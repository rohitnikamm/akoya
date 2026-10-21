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
            Assert.InRange(pill.Bottom, work.Bottom * scale - 18 * scale - 1, work.Bottom * scale - 18 * scale + 1);
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
}
