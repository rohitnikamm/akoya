using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Akoya.Core.Ui;

namespace Akoya.App.Pill;

/// <summary>The monitor the pill is placed on: its work area (physical pixels) and scale.</summary>
public readonly record struct PillTarget(RectPx WorkArea, double Scale);

/// <summary>
/// Puts the pill on the monitor holding the foreground window (the primary monitor if there is none), centred on its
/// work area, 18 DIP above the bottom (<see cref="PillPlacement"/>). Positions are set in physical pixels with
/// <c>SetWindowPos</c>, so mixed-DPI setups and negative monitor coordinates work.
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
            var topLeft = PillPlacement.Compute(target.WorkArea, size.Width, size.Height, target.Scale);
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
            return new PillTarget(
                new RectPx((int)(work.X * dpi), (int)(work.Y * dpi), (int)(work.Width * dpi), (int)(work.Height * dpi)), dpi);
        }

        var scale = PillNative.GetDpiForMonitor(monitor, PillNative.MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96.0
            : VisualTreeHelper.GetDpi(pill).DpiScaleX;
        var w = info.Work;
        return new PillTarget(new RectPx(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top), scale);
    }

    /// <summary>The pill's size and its offset inside the window, DIP (the whole window for other windows).</summary>
    private static (Size Size, Point Offset) PillBounds(Window pill) =>
        pill is PillWindow pw ? (pw.PillSizeDip, pw.PillOffsetDip) : (new Size(pill.ActualWidth, pill.ActualHeight), default);
}
