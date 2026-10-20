using System.Windows;
using System.Windows.Media;

namespace Akoya.App.Controls;

/// <summary>
/// A grid of round dots, each with its own opacity: the pill's waveform and the tray and setup meters.
/// <see cref="Opacities"/> is column-major (index = column * <see cref="Rows"/> + row, row 0 at the top), as produced
/// by <c>Akoya.Core.Ui.PillDots</c>; missing entries draw at <see cref="DefaultOpacity"/>. Assign a new array to redraw.
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

    public static readonly DependencyProperty OpacitiesProperty = DependencyProperty.Register(
        nameof(Opacities), typeof(double[]), typeof(DotGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(DotGrid),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DefaultOpacityProperty = DependencyProperty.Register(
        nameof(DefaultOpacity), typeof(double), typeof(DotGrid),
        new FrameworkPropertyMetadata(0.12, FrameworkPropertyMetadataOptions.AffectsRender));

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

    protected override Size MeasureOverride(Size availableSize) => new(Extent(Columns), Extent(Rows));

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var brush = DotBrush;
        if (brush is null || Columns <= 0 || Rows <= 0) return;

        var opacities = Opacities;
        var size = DotSize;
        var pitch = size + Gap;
        var radius = size / 2;
        for (var column = 0; column < Columns; column++)
        {
            for (var row = 0; row < Rows; row++)
            {
                var index = column * Rows + row;
                var opacity = opacities is not null && index < opacities.Length ? opacities[index] : DefaultOpacity;
                opacity = Math.Clamp(opacity, 0, 1);
                if (opacity <= 0) continue;

                drawingContext.PushOpacity(opacity);
                drawingContext.DrawEllipse(brush, null, new Point(column * pitch + radius, row * pitch + radius), radius, radius);
                drawingContext.Pop();
            }
        }
    }

    private double Extent(int count) => count <= 0 ? 0 : count * DotSize + (count - 1) * Gap;
}
