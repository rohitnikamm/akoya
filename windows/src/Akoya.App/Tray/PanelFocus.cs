using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Akoya.App.Tray;

/// <summary>
/// Gives focus back to the window the user was typing in when the tray panel closes by Esc, a click in its shadow,
/// the tray icon or the dictation hotkey. <see cref="Opening"/> remembers it: the foreground window, or, when a tray
/// click made the taskbar the foreground, the last outside window before it. <see cref="Restore"/> activates it once.
/// A close because the user clicked another window calls neither (focus stays where they clicked).
/// </summary>
public sealed class PanelFocus : IDisposable
{
    private readonly Func<IntPtr> _foreground;
    private readonly Func<IntPtr> _lastOutside;
    private readonly Func<IntPtr, bool> _canRestore;
    private readonly Action<IntPtr> _activate;
    private readonly IDisposable? _owned;
    private IntPtr _target;

    /// <param name="foreground">The foreground window now.</param>
    /// <param name="lastOutside">The last foreground window that was not the taskbar or Akoya.</param>
    /// <param name="canRestore">Whether a window can take focus back (exists, visible, not the taskbar).</param>
    /// <param name="activate">Gives a window the foreground.</param>
    public PanelFocus(Func<IntPtr> foreground, Func<IntPtr> lastOutside, Func<IntPtr, bool> canRestore, Action<IntPtr> activate)
        : this(foreground, lastOutside, canRestore, activate, null)
    {
    }

    private PanelFocus(Func<IntPtr> foreground, Func<IntPtr> lastOutside, Func<IntPtr, bool> canRestore, Action<IntPtr> activate, IDisposable? owned)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(lastOutside);
        ArgumentNullException.ThrowIfNull(canRestore);
        ArgumentNullException.ThrowIfNull(activate);
        _foreground = foreground;
        _lastOutside = lastOutside;
        _canRestore = canRestore;
        _activate = activate;
        _owned = owned;
    }

    /// <summary>The window focus will go back to (zero when there is none).</summary>
    public IntPtr Target => _target;

    /// <summary>The real desktop: tracks the foreground with a WinEvent hook. Create it on the UI thread.</summary>
    public static PanelFocus ForDesktop()
    {
        var tracker = new ForegroundTracker();
        return new PanelFocus(FocusNative.GetForegroundWindow, () => tracker.LastOutside, ForegroundTracker.CanRestore,
            hwnd => FocusNative.SetForegroundWindow(hwnd), tracker);
    }

    /// <summary>The panel is about to show: remember where focus goes back to.</summary>
    public void Opening()
    {
        var foreground = SafeCall(_foreground);
        _target = Restorable(foreground) ? foreground : SafeCall(_lastOutside);
        if (!Restorable(_target)) _target = IntPtr.Zero;
    }

    /// <summary>Gives focus back to the remembered window, once. Nothing when it has closed meanwhile.</summary>
    public void Restore()
    {
        var target = _target;
        _target = IntPtr.Zero;
        if (!Restorable(target)) return;
        try
        {
            _activate(target);
        }
        catch (Exception e) when (e is Win32Exception or ExternalException or InvalidOperationException)
        {
            // Focus stays where Windows put it.
        }
    }

    /// <summary>Drops the remembered window (the user clicked another one).</summary>
    public void Forget() => _target = IntPtr.Zero;

    public void Dispose() => _owned?.Dispose();

    private bool Restorable(IntPtr hwnd) => hwnd != IntPtr.Zero && _canRestore(hwnd);

    private static IntPtr SafeCall(Func<IntPtr> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is ExternalException or InvalidOperationException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Remembers the last foreground window outside Akoya and the taskbar (EVENT_SYSTEM_FOREGROUND, out of context,
    /// skipping Akoya's own process). Its callbacks arrive on the creating thread's message loop.
    /// </summary>
    private sealed class ForegroundTracker : IDisposable
    {
        private const uint EventSystemForeground = 0x0003;
        private const uint WinEventOutOfContext = 0x0000;
        private const uint WinEventSkipOwnProcess = 0x0002;

        private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
            "XamlExplorerHostIslandWindow",
        };

        private readonly FocusNative.WinEventProc _callback; // kept alive while the hook is set
        private IntPtr _hook;

        public ForegroundTracker()
        {
            var current = FocusNative.GetForegroundWindow();
            if (IsOutside(current)) LastOutside = current;
            _callback = OnForeground;
            _hook = FocusNative.SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0,
                WinEventOutOfContext | WinEventSkipOwnProcess);
        }

        public IntPtr LastOutside { get; private set; }

        /// <summary>A window that exists, is visible and is not the taskbar or its tray overflow.</summary>
        public static bool CanRestore(IntPtr hwnd) =>
            hwnd != IntPtr.Zero && FocusNative.IsWindow(hwnd) && FocusNative.IsWindowVisible(hwnd) && !IsShell(hwnd);

        public void Dispose()
        {
            if (_hook == IntPtr.Zero) return;
            FocusNative.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        private void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (IsOutside(hwnd)) LastOutside = hwnd;
        }

        private static bool IsOutside(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || IsShell(hwnd)) return false;
            FocusNative.GetWindowThreadProcessId(hwnd, out var pid);
            return pid != (uint)Environment.ProcessId;
        }

        private static bool IsShell(IntPtr hwnd)
        {
            var name = new StringBuilder(64);
            return FocusNative.GetClassName(hwnd, name, name.Capacity) > 0 && ShellClasses.Contains(name.ToString());
        }
    }
}

/// <summary>The Win32 calls <see cref="PanelFocus"/> needs.</summary>
internal static class FocusNative
{
    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);
}
