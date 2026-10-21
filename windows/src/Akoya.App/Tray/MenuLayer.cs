using System.Windows;
using System.Windows.Controls;

namespace Akoya.App.Tray;

/// <summary>
/// The canvas the tray panel's menus are drawn on. It raises <see cref="Arranging"/> at the start of every arrange, so
/// its owner can place the menus next to their anchors: opening a menu re-arranges only this canvas (a canvas's
/// desired size never changes), so the owner's own arrange would not run again.
/// </summary>
public sealed class MenuLayer : Canvas
{
    public event EventHandler? Arranging;

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Arranging?.Invoke(this, EventArgs.Empty);
        return base.ArrangeOverride(arrangeSize);
    }
}
