using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Akoya.App.Controls;

/// <summary>
/// A <see cref="Border"/> that clips its background and child to its rounded corners (CSS <c>overflow: hidden</c> with
/// <c>border-radius</c>), so a child's background, e.g. a selected list row, reaches the curved edge instead of poking
/// out square. Uses <see cref="Border.CornerRadius"/>'s top-left value for every corner.
/// </summary>
public sealed class ClipBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        ArgumentNullException.ThrowIfNull(sizeInfo);
        base.OnRenderSizeChanged(sizeInfo);
        UpdateClip(sizeInfo.NewSize);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CornerRadiusProperty) UpdateClip(RenderSize);
    }

    private void UpdateClip(Size size)
    {
        var radius = Math.Min(CornerRadius.TopLeft, Math.Min(size.Width, size.Height) / 2);
        var clip = new RectangleGeometry(new Rect(size), radius, radius);
        clip.Freeze();
        Clip = clip;
    }
}
