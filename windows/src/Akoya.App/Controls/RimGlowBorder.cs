using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Akoya.Core.Ui;

namespace Akoya.App.Controls;

/// <summary>
/// A rounded panel with Akoya's rim glow: the mockups' CSS box-shadows (outer glow and drop shadow below the
/// background, the inset rim and bottom-weighted glow above it) painted exactly as a browser does by
/// <see cref="ShadowRaster"/>, then the child on top. By default the shadows are the pill recipe for
/// <see cref="GlowColor"/> (<see cref="RimGlowRecipes.Pill"/>); set <see cref="Shadows"/> for another recipe.
/// Outer shadows draw outside the element's bounds, so leave room around it. Rasters come from <see cref="RimGlowCache"/>
/// (prewarm it to keep rasterising off the UI thread).
/// <para>
/// <see cref="ShapeWidth"/> draws the shape narrower or wider than the element, centred in it (the pill's pop-in):
/// the full-size rasters are cut at the centre and their halves slid together or apart, so no new raster is needed,
/// and the child is clipped to the shape.
/// </para>
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

    public static readonly DependencyProperty ShapeWidthProperty = DependencyProperty.Register(
        nameof(ShapeWidth), typeof(double), typeof(RimGlowBorder),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((RimGlowBorder)d).UpdateChildClip()));

    private RimGlowLayer? _outer;
    private RimGlowLayer? _inset;
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

    /// <summary>
    /// The drawn shape's width, DIP, centred in the element (at least the height, so the narrowest is a circle); NaN
    /// (the default) draws it at the element's width. Layout is unaffected.
    /// </summary>
    public double ShapeWidth
    {
        get => (double)GetValue(ShapeWidthProperty);
        set => SetValue(ShapeWidthProperty, value);
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

        if (ShapeRect(size) is { } shape)
        {
            RenderResized(drawingContext, size, shape);
            return;
        }

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

    /// <summary>The shape's rectangle when <see cref="ShapeWidth"/> differs from the element's width; null otherwise.</summary>
    private Rect? ShapeRect(Size size)
    {
        var width = ShapeWidth;
        if (double.IsNaN(width) || double.IsInfinity(width)) return null;
        width = Math.Max(width, size.Height);
        if (Math.Abs(width - size.Width) < 0.01) return null;
        return new Rect((size.Width - width) / 2, 0, width, size.Height);
    }

    private void RenderResized(DrawingContext drawingContext, Size size, Rect shape)
    {
        var radius = Math.Clamp(CornerRadius, 0, Math.Min(shape.Width, shape.Height) / 2);
        var slide = shape.X; // > 0 narrower, < 0 wider

        if (_outer is { } outer) DrawHalves(drawingContext, outer, size.Width / 2, slide);

        if (Background is { } background)
        {
            drawingContext.DrawRoundedRectangle(background, null, shape, radius, radius);
        }

        if (_inset is { } inset)
        {
            var strength = Math.Clamp(GlowStrength, 0, 1);
            if (strength > 0)
            {
                drawingContext.PushOpacity(strength);
                DrawHalves(drawingContext, inset, size.Width / 2, slide);
                drawingContext.Pop();
            }
        }
    }

    /// <summary>
    /// Draws <paramref name="layer"/>'s left half moved right by <paramref name="slide"/> and its right half moved left
    /// by it, each clipped at the centre. The recipes are symmetric left to right, so the halves meet seamlessly; for a
    /// wider shape (negative slide) each half shows a few more of the near-uniform middle columns.
    /// </summary>
    private static void DrawHalves(DrawingContext drawingContext, RimGlowLayer layer, double centre, double slide)
    {
        const double far = 1e5;
        drawingContext.PushClip(new RectangleGeometry(new Rect(-far, -far, far + centre, 2 * far)));
        drawingContext.PushTransform(new TranslateTransform(slide, 0));
        drawingContext.DrawImage(layer.Image, layer.Bounds);
        drawingContext.Pop();
        drawingContext.Pop();

        drawingContext.PushClip(new RectangleGeometry(new Rect(centre, -far, far, 2 * far)));
        drawingContext.PushTransform(new TranslateTransform(-slide, 0));
        drawingContext.DrawImage(layer.Image, layer.Bounds);
        drawingContext.Pop();
        drawingContext.Pop();
    }

    /// <summary>Clips the child to the shape while <see cref="ShapeWidth"/> is in effect (CSS <c>overflow: hidden</c>).</summary>
    private void UpdateChildClip()
    {
        if (Child is not { } child) return;
        if (ShapeRect(RenderSize) is not { } shape)
        {
            child.ClearValue(ClipProperty);
            return;
        }

        var radius = Math.Clamp(CornerRadius, 0, Math.Min(shape.Width, shape.Height) / 2);
        var padding = Padding;
        shape.Offset(-padding.Left, -padding.Top);
        var clip = new RectangleGeometry(shape, radius, radius);
        clip.Freeze();
        child.Clip = clip;
    }

    private void UpdateRasters(Size size, double radius)
    {
        var shadows = Shadows ?? PillRecipe(GlowColor);
        var images = RimGlowCache.Get(size.Width, size.Height, radius, RimGlowCache.ScaleFor(this), shadows);
        _outer = images.Outer;
        _inset = images.Inset;
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
}
