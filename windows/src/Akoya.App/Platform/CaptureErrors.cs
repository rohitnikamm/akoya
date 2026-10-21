using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Akoya.App.Platform;

/// <summary>Recognises the failures that mean "privacy switch" or "microphone gone" in WASAPI exceptions.</summary>
public static class CaptureErrors
{
    private const int AccessDenied = unchecked((int)0x80070005);          // E_ACCESSDENIED
    private const int DeviceInvalidated = unchecked((int)0x88890004);     // AUDCLNT_E_DEVICE_INVALIDATED
    private const int NotFound = unchecked((int)0x80070490);              // E_NOTFOUND (unknown endpoint id)

    public static bool IsAccessDenied(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is COMException c && c.HResult == AccessDenied);

    public static bool IsDeviceGone(Exception ex) =>
        ex is AudioDeviceDisconnectedException
        || (ex is COMException c && (c.HResult == DeviceInvalidated || c.HResult == NotFound));
}
