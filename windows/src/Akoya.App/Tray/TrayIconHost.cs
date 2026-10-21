using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon;
using Microsoft.Win32;

namespace Akoya.App.Tray;

/// <summary>
/// Akoya's notification-area icon (spec "Tray icon and panel"): the dot logo at 16, 20, 24 and 32 px, white dots on the
/// dark taskbar (Assets/akoya.ico) and near-black dots on the light one (Assets/akoya-dark.ico), re-picked when the
/// Windows theme changes. Tooltip "Akoya". A left or right click opens the panel (<see cref="TrayPanelWindow"/>), or
/// closes it when it is open. Create it on the UI thread; dispose it on exit to remove the icon.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    public const string ToolTip = "Akoya";

    internal static readonly Uri LightIconUri = new("pack://application:,,,/Akoya.App;component/Assets/akoya.ico");
    internal static readonly Uri DarkIconUri = new("pack://application:,,,/Akoya.App;component/Assets/akoya-dark.ico");

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly TaskbarIcon _icon;
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _lightTaskbar;
    private Icon? _image;
    private bool _disposed;

    public TrayIconHost(TrayPanelViewModel vm)
        : this(vm, SystemUsesLightTheme)
    {
    }

    /// <param name="vm">The panel's state.</param>
    /// <param name="lightTaskbar">Whether the taskbar is light (tests substitute the registry read).</param>
    internal TrayIconHost(TrayPanelViewModel vm, Func<bool> lightTaskbar)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(lightTaskbar);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _lightTaskbar = lightTaskbar;
        Panel = new TrayPanelWindow(vm);

        _icon = new TaskbarIcon
        {
            ToolTipText = ToolTip,
            NoLeftClickDelay = true, // no double-click action, so don't wait to tell a click from a double-click
        };
        RefreshIcon();
        _icon.TrayLeftMouseUp += (_, _) => OnTrayLeftClick();
        _icon.TrayRightMouseUp += (_, _) => OnTrayRightClick();
        // Efficiency Mode would lower the whole process's priority: never for a dictation app.
        _icon.ForceCreate(enablesEfficiencyMode: false);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>The panel the icon opens.</summary>
    public TrayPanelWindow Panel { get; }

    /// <summary>The icon is in the notification area.</summary>
    internal bool IsCreated => !_icon.IsDisposed && _icon.IsCreated;

    /// <summary>The icon file now shown: <see cref="LightIconUri"/> (white dots) or <see cref="DarkIconUri"/>.</summary>
    internal Uri CurrentIconUri { get; private set; } = LightIconUri;

    /// <summary>Opens the panel above the tray (e.g. when Akoya is launched again while running).</summary>
    public void ShowPanel() => Panel.ShowNearTray();

    /// <summary>A left click on the icon: toggles the panel.</summary>
    internal void OnTrayLeftClick() => Panel.Toggle();

    /// <summary>A right click on the icon: the same as a left click (there is no context menu).</summary>
    internal void OnTrayRightClick() => Panel.Toggle();

    /// <summary>Shows the icon matching the taskbar's theme now (no-op when it already does).</summary>
    internal void RefreshIcon()
    {
        if (_disposed) return;
        var uri = IconFor(_lightTaskbar());
        if (_image is not null && uri == CurrentIconUri) return;

        var previous = _image;
        _image = LoadIcon(uri);
        CurrentIconUri = uri;
        _icon.Icon = _image;
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Dispose();
        _image?.Dispose();
        Panel.Close();
    }

    /// <summary>Dark dots on a light taskbar, white dots on a dark one.</summary>
    internal static Uri IconFor(bool lightTaskbar) => lightTaskbar ? DarkIconUri : LightIconUri;

    /// <summary>
    /// Windows' "default app mode" for the taskbar and tray: HKCU\…\Themes\Personalize SystemUsesLightTheme = 1 is
    /// light. Missing (older Windows) or unreadable counts as the dark taskbar.
    /// </summary>
    internal static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>The icon at the notification area's size for the primary monitor's DPI (16 px at 100%, 32 at 200%).</summary>
    internal static Icon LoadIcon(Uri uri)
    {
        var info = Application.GetResourceStream(uri)
            ?? throw new InvalidOperationException($"Missing resource {uri}");
        using var stream = info.Stream;
        var size = SmallIconSize();
        return new Icon(stream, size, size);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Raised on SystemEvents' own thread; a theme switch arrives as General (WM_SETTINGCHANGE "ImmersiveColorSet").
        _dispatcher.BeginInvoke(RefreshIcon);
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
