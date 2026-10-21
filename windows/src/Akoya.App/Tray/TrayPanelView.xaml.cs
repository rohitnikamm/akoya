using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Akoya.App.Controls;

namespace Akoya.App.Tray;

/// <summary>
/// The tray panel's content (see TrayPanelView.xaml). Places the open menu next to its anchor: the hotkey menu under
/// the keycap, left edges aligned, as in the mockup; the microphone menu above the Microphone row, right-aligned with
/// it, so it stays inside the panel (which sits on the taskbar). A click on the panel outside an open menu closes it.
/// The Pill row's › turns and its options fade down into place as it opens (at once with animation effects off), and
/// it is collapsed again whenever the panel hides, as the Mac's starts collapsed each time its dropdown opens.
/// </summary>
public partial class TrayPanelView : UserControl
{
    /// <summary>The panel's width, DIP.</summary>
    public const double PanelWidth = 300;

    /// <summary>Gap between the keycap's bottom and the hotkey menu (the mockup's 14 px).</summary>
    public const double HotkeyMenuGap = 14;

    /// <summary>Gap between the microphone menu's bottom and the Microphone row.</summary>
    public const double MicMenuGap = 4;

    /// <summary>Menus keep this far inside the panel's edges (its padding).</summary>
    public const double MenuInset = 14;

    /// <summary>How long the Pill row's › turns and its options fade in.</summary>
    public static readonly Duration DisclosureDuration = new(TimeSpan.FromSeconds(0.22));

    private TrayPanelViewModel? _model;

    public TrayPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as TrayPanelViewModel);
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false && _model is not null) _model.IsPillOpen = false;
        };
        // The anchors are arranged before the menu layer (it comes after the panel in the grid, and an opening menu
        // re-arranges only the layer), so the menus can be placed as the layer arranges them.
        MenuLayer.Arranging += (_, _) => PlaceMenus();
        MenuDismiss.MouseLeftButtonDown += (_, e) =>
        {
            if (DataContext is TrayPanelViewModel vm) vm.CloseMenus.Execute(null);
            e.Handled = true;
        };
    }

    /// <summary>The Pill row's › angle, degrees (for tests).</summary>
    internal double PillChevronAngle => PillChevronTurn.Angle;

    private void Attach(TrayPanelViewModel? model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        ShowDisclosure(animate: false);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayPanelViewModel.IsPillOpen)) ShowDisclosure(animate: IsVisible);
    }

    private void ShowDisclosure(bool animate)
    {
        var open = _model?.IsPillOpen ?? false;
        var angle = open ? 90 : 0;
        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            PillChevronTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            PillChevronTurn.Angle = angle;
            PillOptions.BeginAnimation(OpacityProperty, null);
            PillOptionsShift.BeginAnimation(TranslateTransform.YProperty, null);
            PillOptions.Opacity = 1;
            PillOptionsShift.Y = 0;
            return;
        }

        var ease = new CubicBezierEase();
        PillChevronTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, DisclosureDuration) { EasingFunction = ease });
        if (open)
        {
            // As the Mac's .opacity.combined(with: .offset(y: -6)).
            PillOptions.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, DisclosureDuration) { EasingFunction = ease });
            PillOptionsShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, DisclosureDuration) { EasingFunction = ease });
        }
    }

    /// <summary>The hotkey menu's top-left relative to the panel, DIP (for tests).</summary>
    internal Point HotkeyMenuOffset => new(Canvas.GetLeft(HotkeyMenu), Canvas.GetTop(HotkeyMenu));

    /// <summary>The microphone menu's top-left relative to the panel, DIP (for tests).</summary>
    internal Point MicMenuOffset => new(Canvas.GetLeft(MicMenu), Canvas.GetTop(MicMenu));

    private void PlaceMenus()
    {
        if (HotkeyMenu.Visibility == Visibility.Visible)
        {
            // Left edges aligned with the keycap, as in the mockup, but kept inside the panel's padding (at 300 DIP
            // the keycap sits too far right for the 188-wide menu).
            var anchor = Keycap.TranslatePoint(new Point(0, Keycap.ActualHeight), MenuLayer);
            var left = Math.Min(anchor.X, PanelWidth - MenuInset - HotkeyMenu.DesiredSize.Width);
            Move(HotkeyMenu, left, anchor.Y + HotkeyMenuGap);
        }

        if (MicMenu.Visibility == Visibility.Visible)
        {
            var rowTopRight = MicRow.TranslatePoint(new Point(MicRow.ActualWidth, 0), MenuLayer);
            var left = Math.Max(MenuInset, rowTopRight.X - MicMenu.DesiredSize.Width);
            var top = Math.Max(MenuInset, rowTopRight.Y - MicMenuGap - MicMenu.DesiredSize.Height);
            Move(MicMenu, left, top);
        }
    }

    private static void Move(UIElement element, double left, double top)
    {
        // Only on change: setting a Canvas position invalidates the layer's arrange again.
        if (!Close(Canvas.GetLeft(element), left)) Canvas.SetLeft(element, left);
        if (!Close(Canvas.GetTop(element), top)) Canvas.SetTop(element, top);
    }

    private static bool Close(double a, double b) => !double.IsNaN(a) && Math.Abs(a - b) < 0.01;
}
