using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Akoya.Core.Ui;

namespace Akoya.App.Controls;

/// <summary>
/// A rounded panel with Akoya's rim glow: the mockups' CSS box-shadows (outer glow and drop shadow below the
/// background, the inset rim and bottom-weighted glow above it) painted exactly as a browser does by
/// <see cref="ShadowRaster"/>, then the child on top. By default the shadows are the pill recipe for
/// <see cref="GlowColor"/> (<see cref="RimGlowRecipes.Pill"/>); set <see cref="Shadows"/> for another recipe.
/// Outer shadows draw outside the element's bounds, so leave room around it.
/// </summary>
public sealed class RimGlowBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(RimGlowBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(RimGlowBorder),
        new FrameworkPropertyMetadata(17.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowColorProperty = DependencyProperty.Register(
        nameof(GlowColor), typeof(Color), typeof(RimGlowBorder),
        new FrameworkPropertyMetadata(Color.FromRgb(0x2F, 0x6B, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowStrengthProperty = DependencyProperty.Register(
        nameof(GlowStrength), typeof(double), typeof(RimGlowBorder),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShadowsProperty = DependencyProperty.Register(
        nameof(Shadows), typeof(IReadOnlyList<BoxShadow>), typeof(RimGlowBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(RimGlowBorder),
        new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    private (double W, double H, double R, double Scale, IReadOnlyList<BoxShadow> Shadows)? _cacheKey;
    private (ImageSource Image, Rect Bounds)? _outer;
    private (ImageSource Image, Rect Bounds)? _inset;
    private Color _recipeColor;
    private IReadOnlyList<BoxShadow>? _recipe;

    public RimGlowBorder()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Uniform corner radius, DIP (clamped to half the shorter side).</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>The glow's colour; picks the pill recipe when <see cref="Shadows"/> is not set.</summary>
    public Color GlowColor
    {
        get => (Color)GetValue(GlowColorProperty);
        set => SetValue(GlowColorProperty, value);
    }

    /// <summary>0..1, scales the inset (rim) glow, e.g. from <see cref="GlowEnvelope.Opacity"/>.</summary>
    public double GlowStrength
    {
        get => (double)GetValue(GlowStrengthProperty);
        set => SetValue(GlowStrengthProperty, value);
    }

    /// <summary>The box-shadows to paint (first on top, as in CSS); null uses the pill recipe for <see cref="GlowColor"/>.</summary>
    public IReadOnlyList<BoxShadow>? Shadows
    {
        get => (IReadOnlyList<BoxShadow>?)GetValue(ShadowsProperty);
        set => SetValue(ShadowsProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var padding = Padding;
        var extra = new Size(padding.Left + padding.Right, padding.Top + padding.Bottom);
        if (Child is null) return extra;
        Child.Measure(new Size(Math.Max(0, constraint.Width - extra.Width), Math.Max(0, constraint.Height - extra.Height)));
        return new Size(Child.DesiredSize.Width + extra.Width, Child.DesiredSize.Height + extra.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var padding = Padding;
        Child?.Arrange(new Rect(
            padding.Left,
            padding.Top,
            Math.Max(0, arrangeSize.Width - padding.Left - padding.Right),
            Math.Max(0, arrangeSize.Height - padding.Top - padding.Bottom)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        var radius = Math.Clamp(CornerRadius, 0, Math.Min(size.Width, size.Height) / 2);
        UpdateRasters(size, radius);

        if (_outer is { } outer) drawingContext.DrawImage(outer.Image, outer.Bounds);

        if (Background is { } background)
        {
            drawingContext.DrawRoundedRectangle(background, null, new Rect(size), radius, radius);
        }

        if (_inset is { } inset)
        {
            var strength = Math.Clamp(GlowStrength, 0, 1);
            if (strength > 0)
            {
                drawingContext.PushOpacity(strength);
                drawingContext.DrawImage(inset.Image, inset.Bounds);
                drawingContext.Pop();
            }
        }
    }

    private void UpdateRasters(Size size, double radius)
    {
        var shadows = Shadows ?? PillRecipe(GlowColor);
        // Rasterise at 2x at least, so 2x snapshots and high-DPI screens stay crisp.
        var scale = Math.Max(2.0, VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (_cacheKey is { } cached && cached.W == size.Width && cached.H == size.Height && cached.R == radius
            && cached.Scale == scale && ReferenceEquals(cached.Shadows, shadows))
        {
            return;
        }

        _cacheKey = (size.Width, size.Height, radius, scale, shadows);
        _outer = ToImage(ShadowRaster.RenderOuter(size.Width, size.Height, radius, shadows, scale), scale);
        _inset = ToImage(ShadowRaster.RenderInset(size.Width, size.Height, radius, shadows, scale), scale);
    }

    private IReadOnlyList<BoxShadow> PillRecipe(Color color)
    {
        if (_recipe is null || _recipeColor != color)
        {
            _recipeColor = color;
            _recipe = RimGlowRecipes.Pill(new ShadowColor(color.R, color.G, color.B, color.A / 255.0));
        }
        return _recipe;
    }

    private static (ImageSource, Rect)? ToImage(ShadowImage? image, double scale)
    {
        if (image is null) return null;
        var bitmap = BitmapSource.Create(
            image.PixelWidth, image.PixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null,
            image.Pixels, image.PixelWidth * 4);
        bitmap.Freeze();
        return (bitmap, new Rect(image.Left, image.Top, image.Width, image.Height));
    }
}
