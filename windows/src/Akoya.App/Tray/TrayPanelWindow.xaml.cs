using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Akoya.App.Controls;
using Akoya.App.Pill;
using Akoya.Core.Ui;

namespace Akoya.App.Tray;

/// <summary>
/// The window that shows <see cref="TrayPanelView"/> above the tray corner of the taskbar's monitor
/// (<see cref="TrayPanelPlacement"/>). It activates like the Windows flyouts and hides when it loses activation, on
/// Esc (which first closes an open menu), and on a click in its shadow. It keeps the panel's rim-glow rasters
/// prewarmed off the UI thread while hidden, so opening it never rasterises a shadow.
/// </summary>
public partial class TrayPanelWindow : Window
{
    /// <summary>Room around the panel for its drop shadow (0 30px 70px): the blur on every side, shifted 30 down.</summary>
    public static readonly Thickness ShadowPadding = new(70, 40, 70, 100);

    /// <summary>
    /// A tray click this soon after the panel hid on deactivation is the same click (it deactivated the panel on its
    /// way to the tray icon), so it closes the panel rather than reopening it.
    /// </summary>
    public static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);

    private readonly TrayPanelViewModel _model;
    private long _deactivatedAt = long.MinValue;
    private bool _prewarmQueued;

    public TrayPanelWindow(TrayPanelViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();
        DataContext = model;
        View.SizeChanged += (_, _) => { if (IsVisible) Place(); };
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(() => { if (IsVisible) Place(); });
        model.PropertyChanged += OnModelChanged;
        PrewarmTask = Prewarm();
    }

    /// <summary>The prewarm started by the constructor.</summary>
    public Task PrewarmTask { get; }

    /// <summary>The panel's size, DIP.</summary>
    public Size PanelSizeDip
    {
        get
        {
            if (View.ActualWidth > 0 && View.IsArrangeValid) return new Size(View.ActualWidth, View.ActualHeight);
            View.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return View.DesiredSize;
        }
    }

    /// <summary>The panel's top-left inside the window, DIP.</summary>
    public Point PanelOffsetDip => new(ShadowPadding.Left, ShadowPadding.Top);

    /// <summary>Shows the panel above the tray (activated) or, if it is showing, keeps it there.</summary>
    public void ShowNearTray()
    {
        if (!IsVisible)
        {
            _model.CloseMenus.Execute(null);
            new WindowInteropHelper(this).EnsureHandle();
            Place();
            Show();
            UpdateLayout();
            Place(); // now laid out at its real size
        }
        Activate();
        // Windows may refuse the foreground (the tray click's grant can lapse): try once more, then accept it; the
        // panel still shows and hides on the next outside click or tray click.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!IsActive && TrayNative.GetForegroundWindow() != hwnd)
        {
            ForegroundRetries++;
            TrayNative.SetForegroundWindow(hwnd);
        }
    }

    /// <summary>How many times <see cref="ShowNearTray"/> had to retry taking the foreground (diagnostics, tests).</summary>
    internal int ForegroundRetries { get; private set; }

    /// <summary>
    /// What a click on the tray icon does: opens the panel, or closes it when it is open. A click that just
    /// deactivated (and so hid) the panel counts as closing it (<see cref="ReopenGuard"/>).
    /// </summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            HidePanel();
            return;
        }
        if (Environment.TickCount64 - _deactivatedAt < ReopenGuard.TotalMilliseconds) return;
        ShowNearTray();
    }

    /// <summary>Hides the panel and closes its menus.</summary>
    public void HidePanel()
    {
        _model.CloseMenus.Execute(null);
        if (IsVisible) Hide();
    }

    /// <summary>The panel's rectangle on screen in physical pixels (for tests and diagnostics).</summary>
    public Rect PanelScreenRectPx()
    {
        PillNative.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = PanelSizeDip;
        return new Rect(rect.Left + PanelOffsetDip.X * scale, rect.Top + PanelOffsetDip.Y * scale, size.Width * scale, size.Height * scale);
    }

    /// <summary>
    /// Rasterises the rim glows of every element of the panel as it is now laid out (panel, status card, dots,
    /// menus) on worker threads; the task completes when they are cached. Runs again after the content changes.
    /// </summary>
    public Task Prewarm()
    {
        _prewarmQueued = false;
        var size = PanelSizeDip;
        View.Arrange(new Rect(size));
        var scale = RimGlowCache.ScaleFor(this);
        var work = new List<Task>();
        Collect(View, scale, work);
        return Task.WhenAll(work);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = PillNative.GetWindowLongPtr(hwnd, PillNative.GwlExStyle).ToInt64();
        style |= PillNative.WsExToolWindow; // no Alt+Tab entry
        PillNative.SetWindowLongPtr(hwnd, PillNative.GwlExStyle, new IntPtr(style));
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        HandleDeactivated();
    }

    /// <summary>Losing activation hides the panel (like the Windows flyouts).</summary>
    internal void HandleDeactivated()
    {
        if (!IsVisible) return;
        _deactivatedAt = Environment.TickCount64;
        HidePanel();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape)
        {
            if (_model.IsMenuOpen) _model.CloseMenus.Execute(null);
            else HidePanel();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        // A click in the shadow around the panel is a click outside it.
        var point = e.GetPosition(View);
        if (point.X < 0 || point.Y < 0 || point.X > View.ActualWidth || point.Y > View.ActualHeight)
        {
            HidePanel();
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.PropertyChanged -= OnModelChanged;
        base.OnClosed(e);
    }

    /// <summary>The view-model properties that change the panel's layout or which glow recipes it draws.</summary>
    private static readonly HashSet<string> LayoutProperties =
    [
        nameof(TrayPanelViewModel.StatusText), nameof(TrayPanelViewModel.StatusKind), nameof(TrayPanelViewModel.StatusAction),
        nameof(TrayPanelViewModel.StatusInHeader), nameof(TrayPanelViewModel.IsDownloading),
        nameof(TrayPanelViewModel.LastDictation), nameof(TrayPanelViewModel.HasLastDictation),
        nameof(TrayPanelViewModel.MicMissing), nameof(TrayPanelViewModel.UpdateReady),
    ];

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only content that changes the panel's size or recipes (not the 25 Hz glow), and only on the UI thread.
        if (e.PropertyName is not { } name || !LayoutProperties.Contains(name)) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnModelChanged(sender, e));
            return;
        }

        // Prewarm the new size while hidden (when showing, it's drawn anyway).
        if (IsVisible || _prewarmQueued) return;
        _prewarmQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { if (!IsVisible) _ = Prewarm(); else _prewarmQueued = false; });
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var target = TaskbarMonitor();

        // Twice: a move onto a monitor with another DPI resizes the window (WM_DPICHANGED) and may shift it.
        for (var i = 0; i < 2; i++)
        {
            var size = PanelSizeDip;
            var topLeft = TrayPanelPlacement.Compute(target.Monitor, target.WorkArea, size.Width, size.Height, target.Scale);
            var x = topLeft.X - (int)Math.Round(PanelOffsetDip.X * target.Scale, MidpointRounding.AwayFromZero);
            var y = topLeft.Y - (int)Math.Round(PanelOffsetDip.Y * target.Scale, MidpointRounding.AwayFromZero);
            // Never activates here (a re-place while showing must not steal focus back); ShowNearTray activates.
            PillNative.SetWindowPos(hwnd, PillNative.HwndTopmost, x, y, 0, 0,
                PillNative.SwpNoSize | PillNative.SwpNoActivate | PillNative.SwpNoOwnerZOrder);
        }
    }

    /// <summary>The monitor holding the taskbar (the primary one if it can't be found), physical pixels.</summary>
    private (RectPx Monitor, RectPx WorkArea, double Scale) TaskbarMonitor()
    {
        var taskbar = TrayNative.FindWindow("Shell_TrayWnd", null);
        var monitor = PillNative.MonitorFromWindow(taskbar, PillNative.MonitorDefaultToPrimary);
        var info = new PillNative.MonitorInfo { Size = Marshal.SizeOf<PillNative.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !PillNative.GetMonitorInfo(monitor, ref info))
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var work = SystemParameters.WorkArea;
            var area = new RectPx((int)(work.X * dpi), (int)(work.Y * dpi), (int)(work.Width * dpi), (int)(work.Height * dpi));
            var screen = new RectPx(0, 0, (int)(SystemParameters.PrimaryScreenWidth * dpi), (int)(SystemParameters.PrimaryScreenHeight * dpi));
            return (screen, area, dpi);
        }

        var scale = PillNative.GetDpiForMonitor(monitor, PillNative.MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96.0
            : VisualTreeHelper.GetDpi(this).DpiScaleX;
        return (ToRect(info.Monitor), ToRect(info.Work), scale);

        static RectPx ToRect(PillNative.Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static void Collect(DependencyObject element, double scale, List<Task> work)
    {
        // IsVisible is false throughout a hidden window, so collapsed parts are skipped by their own Visibility.
        if (element is UIElement { Visibility: not Visibility.Visible }) return;
        switch (element)
        {
            case RimGlowBorder { Shadows: { } shadows, ActualWidth: > 0, ActualHeight: > 0 } glow:
                var radius = Math.Clamp(glow.CornerRadius, 0, Math.Min(glow.ActualWidth, glow.ActualHeight) / 2);
                work.Add(RimGlowCache.PrewarmAsync(glow.ActualWidth, glow.ActualHeight, radius, scale, shadows));
                break;
            case DotLogo { Halo: { } halo } logo:
                work.Add(DotLogo.PrewarmAsync(logo.DotSize, scale, halo));
                break;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Collect(VisualTreeHelper.GetChild(element, i), scale, work);
    }
}

/// <summary>The Win32 calls the tray panel needs beyond <see cref="PillNative"/>.</summary>
internal static class TrayNative
{
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}
