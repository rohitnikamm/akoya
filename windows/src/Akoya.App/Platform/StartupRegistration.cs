using System.IO;
using Microsoft.Win32;
using Velopack.Locators;

namespace Akoya.App.Platform;

/// <summary>
/// Launch at startup: the value <c>Akoya</c> under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Its path
/// is Velopack's root stub (<c>%LocalAppData%\Akoya\Akoya.exe</c>) when installed, so it survives updates, and the
/// running exe otherwise (a source checkout).
/// </summary>
public static class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Akoya";

    /// <summary>The exe the Run entry should start (see the class remarks).</summary>
    public static string LaunchPath => InstalledStubPath() ?? Environment.ProcessPath ?? "Akoya.exe";

    /// <summary>True when this copy was installed by Velopack's Setup.exe.</summary>
    public static bool IsInstalledCopy => InstalledStubPath() is not null;

    public static void Set(bool enabled, string exePath) => Set(enabled, exePath, RunKeyPath);

    public static bool IsEnabled() => IsEnabled(RunKeyPath);

    /// <summary>Removes the entry (also run by Velopack's uninstall hook). Never throws.</summary>
    public static void Disable()
    {
        try
        {
            Set(false, "", RunKeyPath);
        }
        catch (Exception)
        {
            // Uninstalling must not fail over a registry value.
        }
    }

    /// <summary>For tests: the same under another HKCU key.</summary>
    internal static void Set(bool enabled, string exePath, string keyPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        if (enabled)
        {
            ArgumentException.ThrowIfNullOrEmpty(exePath);
            key.SetValue(ValueName, Quote(exePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    internal static bool IsEnabled(string keyPath) => RegisteredCommand(keyPath) is { Length: > 0 };

    /// <summary>The raw command line registered, or null.</summary>
    internal static string? RegisteredCommand(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) as string;
    }

    internal static string Quote(string path) => "\"" + path.Trim('"') + "\"";

    private static string? InstalledStubPath()
    {
        try
        {
            if (!VelopackLocator.IsCurrentSet) return null;
            var locator = VelopackLocator.Current;
            if (locator.CurrentlyInstalledVersion is null || locator.IsPortable || string.IsNullOrEmpty(locator.RootAppDir))
                return null;
            var stub = Path.Combine(locator.RootAppDir, "Akoya.exe");
            return File.Exists(stub) ? stub : null;
        }
        catch (Exception)
        {
            return null; // not installed, or Velopack could not tell: use the running exe
        }
    }
}
