using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using H.NotifyIcon;

namespace Akoya.App.Tray;

/// <summary>
/// Akoya's notification-area icon (spec "Tray icon and panel"): the dot logo (Assets/akoya.ico, white dots at 16, 20,
/// 24 and 32 px), tooltip "Akoya". A left or right click opens the panel (<see cref="TrayPanelWindow"/>), or closes it
/// when it is open. Create it on the UI thread; dispose it on exit to remove the icon.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    public const string ToolTip = "Akoya";

    private static readonly Uri IconUri = new("pack://application:,,,/Akoya.App;component/Assets/akoya.ico");

    private readonly TaskbarIcon _icon;
    private readonly Icon _image;
    private bool _disposed;

    public TrayIconHost(TrayPanelViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        Panel = new TrayPanelWindow(vm);

        _image = LoadIcon();
        _icon = new TaskbarIcon
        {
            ToolTipText = ToolTip,
            Icon = _image,
            NoLeftClickDelay = true, // no double-click action, so don't wait to tell a click from a double-click
        };
        _icon.TrayLeftMouseUp += (_, _) => Panel.Toggle();
        _icon.TrayRightMouseUp += (_, _) => Panel.Toggle();
        // Efficiency Mode would lower the whole process's priority: never for a dictation app.
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>The panel the icon opens.</summary>
    public TrayPanelWindow Panel { get; }

    /// <summary>Opens the panel above the tray (e.g. when Akoya is launched again while running).</summary>
    public void ShowPanel() => Panel.ShowNearTray();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Dispose();
        _image.Dispose();
        Panel.Close();
    }

    /// <summary>The icon at the notification area's size for the primary monitor's DPI (16 px at 100%, 32 at 200%).</summary>
    internal static Icon LoadIcon()
    {
        var info = Application.GetResourceStream(IconUri)
            ?? throw new InvalidOperationException($"Missing resource {IconUri}");
        using var stream = info.Stream;
        var size = SmallIconSize();
        return new Icon(stream, size, size);
    }

    private static int SmallIconSize()
    {
        const int SmCxSmIcon = 49;
        var dpi = GetDpiForSystem();
        var size = dpi > 0 ? GetSystemMetricsForDpi(SmCxSmIcon, dpi) : 0;
        return size > 0 ? size : 16;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
