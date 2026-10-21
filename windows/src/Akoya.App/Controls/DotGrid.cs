using System.Windows;
using System.Windows.Media;
using Akoya.Core.Ui;

namespace Akoya.App.Controls;

/// <summary>
/// A grid of round dots, each with its own opacity: the pill's waveform and the tray and setup meters.
/// <see cref="Opacities"/> is column-major (index = column * <see cref="Rows"/> + row, row 0 at the top), as produced
/// by <c>Akoya.Core.Ui.PillDots</c>; missing entries draw at <see cref="DefaultOpacity"/>. Assign a new array to redraw.
/// With <see cref="Bars"/> set it draws the Solid look instead: per column one bar <see cref="DotSize"/> wide with
/// round ends, vertically centred on the rows. Dots or bars brighter than <see cref="HighlightAbove"/> use
/// <see cref="HighlightBrush"/> (the amber scanner).
/// </summary>
public sealed class DotGrid : FrameworkElement
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(DotGrid),
        new FrameworkPropertyMetadata(23, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(DotGrid),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotSizeProperty = DependencyProperty.Register(
        nameof(DotSize), typeof(double), typeof(DotGrid),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(DotGrid),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColumnGapProperty = DependencyProperty.Register(
        nameof(ColumnGap), typeof(double), typeof(DotGrid),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OpacitiesProperty = DependencyProperty.Register(
        nameof(Opacities), typeof(double[]), typeof(DotGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(DotGrid),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DefaultOpacityProperty = DependencyProperty.Register(
        nameof(DefaultOpacity), typeof(double), typeof(DotGrid),
        new FrameworkPropertyMetadata(0.12, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HaloProperty = DependencyProperty.Register(
        nameof(Halo), typeof(IReadOnlyList<BoxShadow>), typeof(DotGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(PillBar[]), typeof(DotGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush), typeof(Brush), typeof(DotGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightAboveProperty = DependencyProperty.Register(
        nameof(HighlightAbove), typeof(double), typeof(DotGrid),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly FadeCache _faded = new();
    private readonly FadeCache _highlightFaded = new();

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>Dot diameter, DIP.</summary>
    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    /// <summary>Space between neighbouring dots, DIP.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Space between neighbouring columns, DIP; NaN (the default) uses <see cref="Gap"/>.</summary>
    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public double[]? Opacities
    {
        get => (double[]?)GetValue(OpacitiesProperty);
        set => SetValue(OpacitiesProperty, value);
    }

    public Brush? DotBrush
    {
        get => (Brush?)GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    /// <summary>Opacity of dots without an entry in <see cref="Opacities"/>.</summary>
    public double DefaultOpacity
    {
        get => (double)GetValue(DefaultOpacityProperty);
        set => SetValue(DefaultOpacityProperty, value);
    }

    /// <summary>
    /// An outer box-shadow painted under every dot at the dot's opacity, as CSS does for a dot with a
    /// <c>box-shadow</c> (e.g. <c>0 0 4px rgba(96,165,250,.7)</c>); null for none. Use a static recipe: rasters are
    /// cached by reference in <see cref="RimGlowCache"/>. The halo draws outside the grid's bounds.
    /// </summary>
    public IReadOnlyList<BoxShadow>? Halo
    {
        get => (IReadOnlyList<BoxShadow>?)GetValue(HaloProperty);
        set => SetValue(HaloProperty, value);
    }

    /// <summary>The Solid look's bars, one per column; null (the default) draws dots.</summary>
    public PillBar[]? Bars
    {
        get => (PillBar[]?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    /// <summary>The brush for dots and bars brighter than <see cref="HighlightAbove"/>; null for none.</summary>
    public Brush? HighlightBrush
    {
        get => (Brush?)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    /// <summary>The opacity above which <see cref="HighlightBrush"/> is used; +∞ (the default) for never.</summary>
    public double HighlightAbove
    {
        get => (double)GetValue(HighlightAboveProperty);
        set => SetValue(HighlightAboveProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Extent(Columns, HorizontalGap), Extent(Rows, Gap));

    private double HorizontalGap => double.IsNaN(ColumnGap) ? Gap : ColumnGap;

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var brush = DotBrush;
        if (brush is null || Columns <= 0 || Rows <= 0) return;

        if (Bars is { } bars)
        {
            RenderBars(drawingContext, bars, brush);
            return;
        }

        var opacities = Opacities;
        var size = DotSize;
        var pitch = size + Gap;
        var columnPitch = size + HorizontalGap;
        var radius = size / 2;
        var halo = Halo is { Count: > 0 } recipe ? RimGlowCache.Get(size, size, radius, RimGlowCache.ScaleFor(this), recipe).Outer : null;
        for (var column = 0; column < Columns; column++)
        {
            for (var row = 0; row < Rows; row++)
            {
                var index = column * Rows + row;
                var opacity = opacities is not null && index < opacities.Length ? opacities[index] : DefaultOpacity;
                opacity = Math.Clamp(opacity, 0, 1);
                if (opacity <= 0) continue;

                var center = new Point(column * columnPitch + radius, row * pitch + radius);
                if (halo is not null)
                {
                    // Each dot's shadow, then the dot, in order, as a browser paints them.
                    var bounds = halo.Bounds;
                    bounds.Offset(center.X - radius, center.Y - radius);
                    drawingContext.PushOpacity(opacity);
                    drawingContext.DrawImage(halo.Image, bounds);
                    drawingContext.Pop();
                }
                var (fill, cache) = BrushFor(brush, opacity);
                if (fill is SolidColorBrush solid)
                {
                    drawingContext.DrawEllipse(cache.Get(solid, opacity), null, center, radius, radius);
                }
                else
                {
                    drawingContext.PushOpacity(opacity);
                    drawingContext.DrawEllipse(fill, null, center, radius, radius);
                    drawingContext.Pop();
                }
            }
        }
    }

    private void RenderBars(DrawingContext drawingContext, PillBar[] bars, Brush brush)
    {
        var width = DotSize;
        var columnPitch = width + HorizontalGap;
        var middle = Extent(Rows, Gap) / 2;
        var count = Math.Min(Columns, bars.Length);
        for (var column = 0; column < count; column++)
        {
            var bar = bars[column];
            var opacity = Math.Clamp(bar.Opacity, 0, 1);
            var height = Math.Max(0, bar.Height);
            if (opacity <= 0 || height <= 0) continue;

            var rect = new Rect(column * columnPitch, middle - height / 2, width, height);
            var radius = Math.Min(width, height) / 2;
            var (fill, cache) = BrushFor(brush, opacity);
            if (fill is SolidColorBrush solid)
            {
                drawingContext.DrawRoundedRectangle(cache.Get(solid, opacity), null, rect, radius, radius);
            }
            else
            {
                drawingContext.PushOpacity(opacity);
                drawingContext.DrawRoundedRectangle(fill, null, rect, radius, radius);
                drawingContext.Pop();
            }
        }
    }

    /// <summary>The brush for a dot or bar at <paramref name="opacity"/>, with the fade cache that goes with it.</summary>
    private (Brush Fill, FadeCache Cache) BrushFor(Brush brush, double opacity) =>
        HighlightBrush is { } highlight && opacity > HighlightAbove ? (highlight, _highlightFaded) : (brush, _faded);

    /// <summary>
    /// Frozen copies of a brush at each opacity, quantised to 1/255 (the alpha precision anyway) and cached, so a frame
    /// of dots allocates nothing and needs no PushOpacity per dot.
    /// </summary>
    private sealed class FadeCache
    {
        private readonly SolidColorBrush?[] _faded = new SolidColorBrush?[256];
        private Color _color;
        private double _opacity = double.NaN;

        public SolidColorBrush Get(SolidColorBrush brush, double opacity)
        {
            if (_color != brush.Color || _opacity != brush.Opacity)
            {
                Array.Clear(_faded);
                _color = brush.Color;
                _opacity = brush.Opacity;
            }

            var level = (int)Math.Round(opacity * 255);
            if (_faded[level] is { } cached) return cached;
            var faded = new SolidColorBrush(brush.Color) { Opacity = brush.Opacity * level / 255.0 };
            faded.Freeze();
            return _faded[level] = faded;
        }
    }

    private double Extent(int count, double gap) => count <= 0 ? 0 : count * DotSize + (count - 1) * gap;
}
