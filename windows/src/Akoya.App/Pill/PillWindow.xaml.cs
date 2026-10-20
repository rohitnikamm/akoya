using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Akoya.App.Controls;

namespace Akoya.App.Pill;

/// <summary>
/// The window that floats the pill above the taskbar. <see cref="ShowFor"/> fades and rises it in on the foreground
/// window's monitor; <see cref="Hide"/> fades and sinks it out (0.28 s, cubic-bezier(.2,.8,.2,1), as in the mockup).
/// </summary>
public partial class PillWindow : Window
{
    /// <summary>Room around the pill for its outer glow (0 12px 40px) and drop shadow (0 8px 24px).</summary>
    public static readonly Thickness ShadowPadding = new(56, 36, 56, 72);

    private static readonly Duration FadeDuration = new(TimeSpan.FromSeconds(0.28));
    private const double HiddenOffset = 10;
    private const double HiddenScale = 0.94;

    private PillTarget? _target;
    private bool _hiding;
    private int _generation;

    public PillWindow()
    {
        InitializeComponent();
        View.SizeChanged += (_, _) => Recentre();
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Recentre);
    }

    /// <summary>The pill's size, DIP.</summary>
    public Size PillSizeDip
    {
        get
        {
            if (View.ActualWidth > 0) return new Size(View.ActualWidth, View.ActualHeight);
            View.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return View.DesiredSize;
        }
    }

    /// <summary>The pill's top-left inside the window, DIP.</summary>
    public Point PillOffsetDip => new(ShadowPadding.Left, ShadowPadding.Top);

    /// <summary>Shows <paramref name="model"/>; fades the pill in on the foreground window's monitor if it isn't showing.</summary>
    public void ShowFor(PillViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _generation++;
        DataContext = model;

        if (!IsVisible || _hiding)
        {
            _hiding = false;
            if (!IsVisible)
            {
                SetHiddenPose();
                new WindowInteropHelper(this).EnsureHandle();
            }
            _target = PillPlacer.PlaceOnForegroundMonitor(this);
            if (!IsVisible) Show();
            _target = PillPlacer.PlaceOnForegroundMonitor(this); // now laid out at its real size
            Animate(1, 0, 1, null);
        }
    }

    /// <summary>Fades the pill out, then hides the window. A <see cref="ShowFor"/> during the fade cancels it.</summary>
    public new void Hide()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        var generation = ++_generation;
        Animate(0, HiddenOffset, HiddenScale, () =>
        {
            if (generation != _generation) return;
            _hiding = false;
            base.Hide();
        });
    }

    /// <summary>The pill's rectangle on screen in physical pixels (for tests and diagnostics).</summary>
    public Rect PillScreenRectPx()
    {
        PillNative.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = PillSizeDip;
        return new Rect(rect.Left + PillOffsetDip.X * scale, rect.Top + PillOffsetDip.Y * scale, size.Width * scale, size.Height * scale);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = PillNative.GetWindowLongPtr(hwnd, PillNative.GwlExStyle).ToInt64();
        style |= PillNative.WsExNoActivate | PillNative.WsExToolWindow | PillNative.WsExTransparent;
        PillNative.SetWindowLongPtr(hwnd, PillNative.GwlExStyle, new IntPtr(style));
    }

    private void Recentre()
    {
        if (_target is { } target && IsVisible) PillPlacer.Place(this, target);
    }

    private void SetHiddenPose()
    {
        Root.BeginAnimation(OpacityProperty, null);
        RiseShift.BeginAnimation(TranslateTransform.YProperty, null);
        RiseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RiseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Root.Opacity = 0;
        RiseShift.Y = HiddenOffset;
        RiseScale.ScaleX = HiddenScale;
        RiseScale.ScaleY = HiddenScale;
    }

    private void Animate(double opacity, double offset, double scale, Action? completed)
    {
        // Respect "Animation effects" off in Windows settings: jump straight to the end.
        var duration = SystemParameters.ClientAreaAnimation ? FadeDuration : new Duration(TimeSpan.Zero);
        var ease = new CubicBezierEase();

        var fade = new DoubleAnimation(opacity, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        if (completed is not null) fade.Completed += (_, _) => completed();

        Root.BeginAnimation(OpacityProperty, fade);
        RiseShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, duration) { EasingFunction = ease });
        RiseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        RiseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
    }
}
