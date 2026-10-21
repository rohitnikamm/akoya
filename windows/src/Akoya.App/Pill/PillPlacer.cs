using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Akoya.Core.Ui;

namespace Akoya.App.Pill;

/// <summary>
/// The monitor the pill is placed on: its work area and the bottom the gap is measured from
/// (<see cref="PillPlacement.ReservedBottom"/>), physical pixels, and its scale.
/// </summary>
public readonly record struct PillTarget(RectPx WorkArea, int ReservedBottom, double Scale);

/// <summary>
/// Puts the pill on the monitor holding the foreground window (the primary monitor if there is none), centred on its
/// work area, 52 DIP above the top of that monitor's taskbar as if it were showing (<see cref="PillPlacement"/>), so
/// an auto-hidden taskbar never covers it. Positions are set in physical pixels with <c>SetWindowPos</c>, so
/// mixed-DPI setups and negative monitor coordinates work.
/// </summary>
public static class PillPlacer
{
    /// <summary>Places <paramref name="pill"/> on the foreground window's monitor and returns that monitor.</summary>
    public static PillTarget PlaceOnForegroundMonitor(Window pill)
    {
        ArgumentNullException.ThrowIfNull(pill);
        var target = ForegroundTarget(pill);
        Place(pill, target);
        return target;
    }

    /// <summary>Re-centres <paramref name="pill"/> on an earlier target, e.g. after its size changed.</summary>
    public static void Place(Window pill, PillTarget target)
    {
        ArgumentNullException.ThrowIfNull(pill);
        var hwnd = new WindowInteropHelper(pill).EnsureHandle();

        // Twice: if the move lands on a monitor with another DPI, WPF resizes the window for it (WM_DPICHANGED) and
        // may shift it, so the second call puts it back where the new size needs it.
        for (var i = 0; i < 2; i++)
        {
            var (size, offset) = PillBounds(pill);
            var topLeft = PillPlacement.Compute(target.WorkArea, target.ReservedBottom, size.Width, size.Height, target.Scale);
            var x = topLeft.X - (int)Math.Round(offset.X * target.Scale, MidpointRounding.AwayFromZero);
            var y = topLeft.Y - (int)Math.Round(offset.Y * target.Scale, MidpointRounding.AwayFromZero);
            PillNative.SetWindowPos(hwnd, PillNative.HwndTopmost, x, y, 0, 0,
                PillNative.SwpNoSize | PillNative.SwpNoActivate | PillNative.SwpNoOwnerZOrder);
        }
    }

    private static PillTarget ForegroundTarget(Window pill)
    {
        var monitor = PillNative.MonitorFromWindow(PillNative.GetForegroundWindow(), PillNative.MonitorDefaultToPrimary);
        var info = new PillNative.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<PillNative.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !PillNative.GetMonitorInfo(monitor, ref info))
        {
            // No monitor information: fall back to WPF's primary work area.
            var dpi = VisualTreeHelper.GetDpi(pill).DpiScaleX;
            var work = SystemParameters.WorkArea;
            var area = new RectPx((int)(work.X * dpi), (int)(work.Y * dpi), (int)(work.Width * dpi), (int)(work.Height * dpi));
            return new PillTarget(area, area.Bottom, dpi);
        }

        var scale = PillNative.GetDpiForMonitor(monitor, PillNative.MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96.0
            : VisualTreeHelper.GetDpi(pill).DpiScaleX;
        var workArea = ToRect(info.Work);
        var monitorRect = ToRect(info.Monitor);
        return new PillTarget(workArea, PillPlacement.ReservedBottom(monitorRect, workArea, TaskbarOn(monitor)), scale);
    }

    /// <summary>
    /// The taskbar on <paramref name="monitor"/>: the primary <c>Shell_TrayWnd</c> or a <c>Shell_SecondaryTrayWnd</c>
    /// (one per monitor with "Show my taskbar on all displays"); null if that monitor has none. Auto-hide is one
    /// setting for all of them (<c>ABM_GETSTATE</c>).
    /// </summary>
    internal static TaskbarInfo? TaskbarOn(IntPtr monitor)
    {
        foreach (var bar in Taskbars())
        {
            if (PillNative.MonitorFromWindow(bar, PillNative.MonitorDefaultToNull) != monitor) continue;
            if (!PillNative.GetWindowRect(bar, out var rect)) continue;
            return new TaskbarInfo(ToRect(rect), AutoHide());
        }
        return null;
    }

    private static IEnumerable<IntPtr> Taskbars()
    {
        var primary = PillNative.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) yield return primary;
        var secondary = IntPtr.Zero;
        // Bounded, in case the shell is restarting while we walk the list.
        for (var i = 0; i < 16; i++)
        {
            secondary = PillNative.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null);
            if (secondary == IntPtr.Zero) yield break;
            yield return secondary;
        }
    }

    private static bool AutoHide()
    {
        var data = new PillNative.AppBarData { Size = System.Runtime.InteropServices.Marshal.SizeOf<PillNative.AppBarData>() };
        var state = PillNative.SHAppBarMessage(PillNative.AbmGetState, ref data).ToUInt64();
        return (state & PillNative.AbsAutoHide) != 0;
    }

    private static RectPx ToRect(PillNative.Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    /// <summary>The pill's size and its offset inside the window, DIP (the whole window for other windows).</summary>
    private static (Size Size, Point Offset) PillBounds(Window pill) =>
        pill is PillWindow pw ? (pw.PillSizeDip, pw.PillOffsetDip) : (new Size(pill.ActualWidth, pill.ActualHeight), default);
}
