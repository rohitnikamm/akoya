using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Akoya.App.Platform;

/// <summary>Windows' microphone privacy switches, which silently block capture for desktop apps.</summary>
public static class MicPrivacy
{
    private const string ConsentKey =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    /// <summary>
    /// True unless "Microphone access" or "Let desktop apps access your microphone" is set to Deny.
    /// A missing key or value counts as allowed (that is the Windows default).
    /// </summary>
    public static bool IsDesktopAccessAllowed()
    {
        string? global = null;
        string? nonPackaged = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ConsentKey);
            global = key?.GetValue("Value") as string;
            using var desktop = key?.OpenSubKey("NonPackaged");
            nonPackaged = desktop?.GetValue("Value") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Unreadable settings: do not block on a guess; a real block surfaces as E_ACCESSDENIED.
        }
        return IsAllowed(global, nonPackaged);
    }

    /// <summary>Only an explicit "Deny" (any case) at either level blocks.</summary>
    public static bool IsAllowed(string? globalValue, string? nonPackagedValue) =>
        !IsDeny(globalValue) && !IsDeny(nonPackagedValue);

    /// <summary>Opens Settings > Privacy > Microphone.</summary>
    public static void OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing sensible to do if the shell can't open Settings.
        }
    }

    private static bool IsDeny(string? value) =>
        string.Equals(value?.Trim(), "Deny", StringComparison.OrdinalIgnoreCase);
}
