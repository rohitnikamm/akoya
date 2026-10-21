using System.Runtime.InteropServices;

namespace Akoya.App.Platform;

/// <summary>How long the session has had no keyboard or mouse input (GetLastInputInfo).</summary>
public static class InputIdle
{
    /// <summary>Time since the last input, or null when Windows can't say.</summary>
    public static TimeSpan? Time()
    {
        var info = new NativeMethods.LastInputInfo { Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>() };
        if (!NativeMethods.GetLastInputInfo(ref info)) return null;
        // Both are GetTickCount values: the unsigned difference survives the 49.7-day wrap.
        var idleMs = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
