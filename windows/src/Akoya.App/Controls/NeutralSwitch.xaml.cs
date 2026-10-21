using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Akoya.App.Controls;

/// <summary>Akoya's neutral on/off switch (see NeutralSwitch.xaml). The thumb jumps into place until the switch is loaded, then slides.</summary>
public partial class NeutralSwitch : ToggleButton
{
    /// <summary>How far the thumb travels, DIP (34 track − 16 thumb − 2 × 2 inset).</summary>
    public const double Travel = 14;

    private static readonly Duration SlideDuration = new(TimeSpan.FromSeconds(0.28));

    private TranslateTransform? _shift;

    public NeutralSwitch()
    {
        InitializeComponent();
    }

    /// <summary>The thumb's current offset from the off position, DIP (for tests).</summary>
    public double ThumbOffset => _shift?.X ?? 0;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Freezables in a template are frozen, so the thumb gets its own (animatable) transform.
        if (GetTemplateChild("Thumb") is UIElement thumb)
        {
            _shift = new TranslateTransform();
            thumb.RenderTransform = _shift;
        }
        else
        {
            _shift = null;
        }
        Move(animate: false);
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        Move(animate: true);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        Move(animate: true);
    }

    protected override void OnIndeterminate(RoutedEventArgs e)
    {
        base.OnIndeterminate(e);
        Move(animate: true);
    }

    private void Move(bool animate)
    {
        if (_shift is null) return;
        var to = IsChecked == true ? Travel : 0;
        if (!animate || !IsLoaded || !SystemParameters.ClientAreaAnimation)
        {
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            _shift.X = to;
            return;
        }

        _shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, SlideDuration)
        {
            EasingFunction = new CubicBezierEase(),
            FillBehavior = FillBehavior.HoldEnd,
        });
    }
}
