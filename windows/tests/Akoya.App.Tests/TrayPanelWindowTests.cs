using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Akoya.App.Controls;
using Akoya.App.Tests.Snapshots;
using Akoya.App.Tray;
using Akoya.Core.Dictation;
using Akoya.Core.Ui;
using Xunit;

namespace Akoya.App.Tests;

[Collection(DesktopCollection.Name)]
public class TrayPanelWindowTests
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;

    [Fact]
    public void ShowNearTray_TopmostToolWindow_InTheTrayCornerOfTheWorkArea()
    {
        SnapshotHarness.RunSta(() =>
        {
            var window = new TrayPanelWindow(TrayPanelSnapshots.Model(_ => { }));
            window.ShowNearTray();

            // Checked straight away, without pumping: the panel hides itself as soon as anything else on the runner
            // takes the foreground (tests in other classes run in parallel). ShowNearTray has laid it out already.
            Assert.True(window.IsVisible);
            Assert.True(window.Topmost);
            Assert.False(window.ShowInTaskbar);
            var hwnd = new WindowInteropHelper(window).Handle;
            Assert.Equal(WsExToolWindow, GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExToolWindow);

            var panel = window.PanelScreenRectPx();
            var scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            Assert.Equal(TrayPanelView.PanelWidth * scale, panel.Width, 0.5);

            // The runner's taskbar is at the bottom: the panel sits 12 DIP inside the bottom-right of the work area.
            var work = SystemParameters.WorkArea;
            var gap = TrayPanelPlacement.GapDip * scale;
            Assert.InRange(panel.Right, work.Right * scale - gap - 1.5, work.Right * scale - gap + 1.5);
            Assert.InRange(panel.Bottom, work.Bottom * scale - gap - 1.5, work.Bottom * scale - gap + 1.5);

            window.Close();
        });
    }

    [Fact]
    public void Esc_ClosesAnOpenMenuFirst_ThenHidesThePanel()
    {
        SnapshotHarness.RunSta(() =>
        {
            var vm = TrayPanelSnapshots.Model(_ => { });
            var window = new TrayPanelWindow(vm);
            window.ShowNearTray();
            vm.ToggleHotkeyMenu.Execute(null);
            Pump(TimeSpan.FromMilliseconds(100));

            PressEscape(window);
            Assert.False(vm.IsHotkeyMenuOpen);
            Assert.True(window.IsVisible);

            PressEscape(window);
            Assert.False(window.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void PillRow_OpensItsOptions_PanelGrows_CollapsedAgainWhenThePanelHides()
    {
        SnapshotHarness.RunSta(() =>
        {
            var vm = TrayPanelSnapshots.Model(_ => { });
            var window = new TrayPanelWindow(vm);
            window.ShowNearTray();
            var view = (TrayPanelView)window.FindName("View")!;
            var options = (FrameworkElement)view.FindName("PillOptions")!;
            var collapsedHeight = view.ActualHeight;
            Assert.Equal(Visibility.Collapsed, options.Visibility);
            Assert.Equal(0, view.PillChevronAngle);

            ((Button)view.FindName("PillRow")!).Command.Execute(null);
            window.UpdateLayout();
            Assert.True(vm.IsPillOpen);
            Assert.Equal(Visibility.Visible, options.Visibility);
            Assert.True(view.ActualHeight > collapsedHeight + 60, $"{view.ActualHeight} vs {collapsedHeight}");

            // Choosing Solid and turning on Red while recording through the controls reaches the model.
            ((System.Windows.Controls.Primitives.ToggleButton)view.FindName("SolidSegment")!).IsChecked = true;
            ((System.Windows.Controls.Primitives.ToggleButton)view.FindName("RedTintSwitch")!).IsChecked = true;
            Assert.Equal(Akoya.Core.Settings.WaveLook.Solid, vm.PillLook);
            Assert.True(vm.RedTint);

            window.HidePanel();
            Assert.False(vm.IsPillOpen);
            Assert.Equal(0, view.PillChevronAngle);

            window.Close();
        });
    }

    [Fact]
    public void Deactivated_Hides_AndTheTrayClickThatCausedItDoesNotReopen()
    {
        SnapshotHarness.RunSta(() =>
        {
            var vm = TrayPanelSnapshots.Model(_ => { });
            var window = new TrayPanelWindow(vm);
            window.Toggle(); // the very first click opens it (no earlier deactivation to guard against)
            Assert.True(window.IsVisible);
            vm.ToggleMicMenu.Execute(null);

            window.HandleDeactivated();
            Assert.False(window.IsVisible);
            Assert.False(vm.IsMicMenuOpen);

            window.Toggle(); // the same click arriving at the tray icon
            Assert.False(window.IsVisible);

            Pump(TrayPanelWindow.ReopenGuard + TimeSpan.FromMilliseconds(100));
            window.Toggle();
            Assert.True(window.IsVisible);
            window.Toggle();
            Assert.False(window.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void Constructor_PrewarmsThePanelsGlows()
    {
        SnapshotHarness.RunSta(() =>
        {
            var window = new TrayPanelWindow(TrayPanelSnapshots.Model(vm => vm.SetLastDictation("Hello there.")));
            Assert.True(window.PrewarmTask.Wait(TimeSpan.FromSeconds(10)), "prewarm did not finish");

            var size = window.PanelSizeDip;
            var scale = RimGlowCache.ScaleFor(window);
            Assert.True(RimGlowCache.Contains(size.Width, size.Height, 30, scale, RimGlowRecipes.TrayPanel));
            Assert.True(RimGlowCache.Contains(5, 5, 2.5, scale, RimGlowRecipes.LogoDot));
            Assert.True(RimGlowCache.Contains(7, 7, 3.5, scale, RimGlowRecipes.StatusDotBlue));

            window.Close();
        });
    }

    [Fact]
    public void Menus_HotkeyUnderTheKeycap_MicAboveItsRow_BothInsideThePanel()
    {
        SnapshotHarness.RunSta(() =>
        {
            var vm = TrayPanelSnapshots.Model(_ => { });
            var view = Laid(new TrayPanelView { DataContext = vm });

            vm.ToggleHotkeyMenu.Execute(null);
            view.UpdateLayout();
            var keycap = (Button)view.FindName("Keycap");
            var keycapBottomLeft = keycap.TranslatePoint(new Point(0, keycap.ActualHeight), view);
            var hotkeyMenu = (FrameworkElement)view.FindName("HotkeyMenu");
            var expectedLeft = Math.Min(keycapBottomLeft.X, TrayPanelView.PanelWidth - TrayPanelView.MenuInset - hotkeyMenu.ActualWidth);
            Assert.Equal(expectedLeft, view.HotkeyMenuOffset.X, 0.5);
            Assert.True(view.HotkeyMenuOffset.X + hotkeyMenu.ActualWidth <= TrayPanelView.PanelWidth - TrayPanelView.MenuInset + 0.5);
            Assert.Equal(keycapBottomLeft.Y + TrayPanelView.HotkeyMenuGap, view.HotkeyMenuOffset.Y, 0.5);

            vm.ToggleMicMenu.Execute(null);
            view.UpdateLayout();
            var menu = (FrameworkElement)view.FindName("MicMenu");
            var row = (FrameworkElement)view.FindName("MicRow");
            var rowTop = row.TranslatePoint(default, view).Y;
            Assert.Equal(rowTop - TrayPanelView.MicMenuGap, view.MicMenuOffset.Y + menu.ActualHeight, 0.5);
            Assert.InRange(view.MicMenuOffset.X, 0, TrayPanelView.PanelWidth - menu.ActualWidth);
            Assert.True(view.MicMenuOffset.Y >= 0);
            Assert.Equal(4, vm.MicOptions.Count);
        });
    }

    [Theory]
    [InlineData("akoya.ico", true)]
    [InlineData("akoya-dark.ico", false)]
    public void Icons_HaveTheFourTraySizes_WhiteOrDarkDots(string file, bool white)
    {
        SnapshotHarness.RunSta(() =>
        {
            var uri = new Uri($"pack://application:,,,/Akoya.App;component/Assets/{file}");
            var info = Application.GetResourceStream(uri);
            Assert.NotNull(info);
            using (var stream = info.Stream)
            {
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.Equal([16, 20, 24, 32], decoder.Frames.Select(f => f.PixelWidth).Order());

                // The brightest opaque pixel of the 32 px frame: white dots or near-black ones.
                var frame = new FormatConvertedBitmap(decoder.Frames.Single(f => f.PixelWidth == 32), PixelFormats.Bgra32, null, 0);
                var pixels = new byte[32 * 32 * 4];
                frame.CopyPixels(pixels, 32 * 4, 0);
                var opaque = Enumerable.Range(0, 32 * 32).Where(i => pixels[i * 4 + 3] == 255).ToArray();
                Assert.NotEmpty(opaque);
                var red = opaque.Max(i => pixels[i * 4 + 2]);
                if (white) Assert.Equal(255, red);
                else Assert.InRange(red, 0, 0x40);
            }

            using var icon = TrayIconHost.LoadIcon(uri);
            Assert.InRange(icon.Width, 16, 32);
        });
    }

    [Fact]
    public void IconFor_DarkDotsOnTheLightTaskbar()
    {
        Assert.Equal(TrayIconHost.DarkIconUri, TrayIconHost.IconFor(lightTaskbar: true));
        Assert.Equal(TrayIconHost.LightIconUri, TrayIconHost.IconFor(lightTaskbar: false));
        _ = TrayIconHost.SystemUsesLightTheme(); // reads the registry without throwing
    }

    [Fact]
    public void TrayIconHost_CreatesTheIcon_ClicksToggleThePanel_DisposeRemovesIt()
    {
        SnapshotHarness.RunSta(() =>
        {
            var light = true;
            var host = new TrayIconHost(TrayPanelSnapshots.Model(_ => { }), () => light);
            Assert.True(host.IsCreated);
            Assert.Equal("Akoya", TrayIconHost.ToolTip);
            Assert.Equal(TrayIconHost.DarkIconUri, host.CurrentIconUri);

            // The theme switches to dark: the white icon replaces the dark one.
            light = false;
            host.RefreshIcon();
            Assert.Equal(TrayIconHost.LightIconUri, host.CurrentIconUri);

            host.OnTrayLeftClick();
            Assert.True(host.Panel.IsVisible);
            host.OnTrayLeftClick();
            Assert.False(host.Panel.IsVisible);
            host.OnTrayRightClick();
            Assert.True(host.Panel.IsVisible);
            host.OnTrayRightClick();
            Assert.False(host.Panel.IsVisible);

            host.ShowPanel();
            Assert.True(host.Panel.IsVisible);

            host.Dispose();
            Assert.False(host.IsCreated);
            host.Dispose(); // idempotent
        });
    }

    [Fact]
    public void ViewModel_UpdatedFromWorkerThreads_WithAWindowAttached_ChangesArriveOnTheUiThread()
    {
        SnapshotHarness.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var ui = Environment.CurrentManagedThreadId;
            var vm = TrayPanelSnapshots.Model(_ => { });
            var window = new TrayPanelWindow(vm);
            var threads = new HashSet<int>();
            vm.PropertyChanged += (_, _) => threads.Add(Environment.CurrentManagedThreadId);

            // Throws on the worker if anything there touches the window (DependencyObjects are thread-bound).
            Task.Run(() =>
            {
                vm.SetModel(ModelState.Loading, 0.5);
                vm.SetLastDictation("From a worker.");
            }).Wait();
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal("Getting the speech model ready… 50%", vm.StatusText);
            Assert.True(vm.HasLastDictation);

            Task.Run(() =>
            {
                vm.SetPhase(DictationPhase.Recording);
                vm.SetVoiceLevel(1.0);
            }).Wait();
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal("Listening", vm.StatusText);
            Assert.Equal(1.0, vm.GlowStrength, 3);

            Assert.Equal([ui], threads);
            window.Close();
        });
    }

    [Fact]
    public void LaunchAtStartup_Fails_TheBoundSwitchFlipsBack()
    {
        SnapshotHarness.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var calls = new List<bool>();
            var vm = new TrayPanelViewModel(new TrayPanelActions { SetLaunchAtStartup = v => { calls.Add(v); return false; } });
            var view = Laid(new TrayPanelView { DataContext = vm });
            var toggle = (NeutralSwitch)view.FindName("StartupSwitch");

            toggle.IsChecked = true; // the user flips it on
            Pump(TimeSpan.FromMilliseconds(100));

            Assert.Equal([true], calls);
            Assert.False(vm.LaunchAtStartup);
            Assert.False(toggle.IsChecked);
        });
    }

    [Fact]
    public void NeutralSwitch_ThumbFollowsIsChecked()
    {
        SnapshotHarness.RunSta(() =>
        {
            var toggle = Laid(new NeutralSwitch());
            Assert.Equal(0, toggle.ThumbOffset);
            Assert.Equal(new Size(34, 20), toggle.RenderSize);

            var on = Laid(new NeutralSwitch { IsChecked = true });
            Assert.Equal(NeutralSwitch.Travel, on.ThumbOffset);
        });
    }

    private static T Laid<T>(T element) where T : FrameworkElement
    {
        element.Resources.MergedDictionaries.Add(PillSnapshots.Theme());
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        return element;
    }

    private static void PressEscape(Window window)
    {
        var source = PresentationSource.FromVisual(window)!;
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

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
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
