using System.ComponentModel;
using System.Diagnostics;

namespace Akoya.App.Platform;

/// <summary>
/// Decides whether the foreground window belongs to a process Akoya cannot send input to (UIPI):
/// an elevated process while Akoya is not elevated.
/// </summary>
public static class ElevationCheck
{
    private static readonly Lazy<bool> SelfElevated = new(() => IsProcessElevated(Environment.ProcessId) ?? false);

    /// <summary>True when Akoya itself runs elevated, in which case UIPI does not block its input.</summary>
    public static bool IsCurrentProcessElevated() => SelfElevated.Value;

    /// <summary>
    /// True when the foreground window's process is elevated and Akoya is not. A process whose token
    /// cannot be opened because access is denied counts as elevated (that is what a higher-integrity
    /// process looks like from here). No foreground window, or any other failure, counts as not elevated.
    /// </summary>
    public static bool ForegroundIsElevated()
    {
        try
        {
            if (IsCurrentProcessElevated())
            {
                return false;
            }

            var window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }

            if (NativeMethods.GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0)
            {
                return false;
            }

            return IsProcessElevated(checked((int)processId)) ?? false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the process's token is elevated. Null when it could not be determined for a reason
    /// other than access being denied; access denied returns true.
    /// </summary>
    public static bool? IsProcessElevated(int processId)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            return System.Runtime.InteropServices.Marshal.GetLastWin32Error() == NativeMethods.ErrorAccessDenied ? true : null;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TokenQuery, out var token))
            {
                return System.Runtime.InteropServices.Marshal.GetLastWin32Error() == NativeMethods.ErrorAccessDenied ? true : null;
            }

            try
            {
                if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevationClass, out var elevation, sizeof(int), out _))
                {
                    return null;
                }

                return elevation != 0;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
