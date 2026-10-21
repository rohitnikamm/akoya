using System.Windows;
using System.Windows.Media;
using Akoya.Core.Ui;

namespace Akoya.App.Controls;

/// <summary>
/// Akoya's dot mark: five columns of 2-4-6-4-2 dots, vertically centred (the Mac's <c>DotLogo</c>). Each dot has an
/// optional CSS halo (<see cref="Halo"/>, e.g. <see cref="RimGlowRecipes.LogoDot"/>), painted from
/// <see cref="RimGlowCache"/> so it matches the mockups exactly. Defaults are the tray panel's mark: 5 DIP dots, 6 DIP
/// between columns, 3 DIP between rows, #BFDBFE.
/// </summary>
public sealed class DotLogo : FrameworkElement
{
    /// <summary>Dots per column, left to right.</summary>
    public static readonly IReadOnlyList<int> Columns = [2, 4, 6, 4, 2];

    public static readonly DependencyProperty DotSizeProperty = DependencyProperty.Register(
        nameof(DotSize), typeof(double), typeof(DotLogo),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColumnGapProperty = DependencyProperty.Register(
        nameof(ColumnGap), typeof(double), typeof(DotLogo),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap), typeof(double), typeof(DotLogo),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(DotLogo),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HaloProperty = DependencyProperty.Register(
        nameof(Halo), typeof(IReadOnlyList<BoxShadow>), typeof(DotLogo),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public DotLogo()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    /// <summary>Dot diameter, DIP.</summary>
    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    /// <summary>Space between columns, DIP.</summary>
    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    /// <summary>Space between dots in a column, DIP.</summary>
    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    public Brush? DotBrush
    {
        get => (Brush?)GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    /// <summary>Box-shadows painted under every dot (outer shadows only); null for none.</summary>
    public IReadOnlyList<BoxShadow>? Halo
    {
        get => (IReadOnlyList<BoxShadow>?)GetValue(HaloProperty);
        set => SetValue(HaloProperty, value);
    }

    /// <summary>Rasterises the halo for this logo's dot size on a worker thread (see <see cref="RimGlowCache"/>).</summary>
    public static Task PrewarmAsync(double dotSize, double scale, IReadOnlyList<BoxShadow> halo) =>
        RimGlowCache.PrewarmAsync(dotSize, dotSize, dotSize / 2, scale, halo);

    protected override Size MeasureOverride(Size availableSize)
    {
        var tallest = Columns.Max();
        return new Size(
            Columns.Count * DotSize + (Columns.Count - 1) * ColumnGap,
            tallest * DotSize + (tallest - 1) * RowGap);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var size = DotSize;
        if (size <= 0) return;

        var radius = size / 2;
        var tallest = Columns.Max();
        var halo = Halo is { } recipe
            ? RimGlowCache.Get(size, size, radius, RimGlowCache.ScaleFor(this), recipe).Outer
            : null;
        var brush = DotBrush;

        for (var column = 0; column < Columns.Count; column++)
        {
            var count = Columns[column];
            var x = column * (size + ColumnGap);
            var top = (tallest - count) * (size + RowGap) / 2;
            for (var row = 0; row < count; row++)
            {
                var y = top + row * (size + RowGap);
                if (halo is not null)
                {
                    var bounds = halo.Bounds;
                    bounds.Offset(x, y);
                    drawingContext.DrawImage(halo.Image, bounds);
                }
                if (brush is not null) drawingContext.DrawEllipse(brush, null, new Point(x + radius, y + radius), radius, radius);
            }
        }
    }
}
