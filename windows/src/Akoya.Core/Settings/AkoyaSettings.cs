using Akoya.Core.Hotkey;

namespace Akoya.Core.Settings;

/// <summary>Which ONNX execution provider the speech model should prefer.</summary>
public enum InferenceProvider { Auto, Cpu }

/// <summary>The pill's waveform: dots (the Mac's Pixel) or one rounded bar per column (Solid).</summary>
public enum WaveLook { Pixel, Solid }

/// <summary>
/// User settings persisted to %AppData%\Akoya\settings.json. <see cref="Look"/> and <see cref="RedTint"/> are the tray
/// panel's Pill options (the Mac's Menu bar icon options): the waveform's look, and a red waveform while recording.
/// </summary>
public sealed record AkoyaSettings(
    HotkeyKey Hotkey = HotkeyKey.RightAlt,
    string? MicrophoneId = null,
    bool LaunchAtStartup = false,
    bool SetupDone = false,
    InferenceProvider Provider = InferenceProvider.Auto,
    WaveLook Look = WaveLook.Pixel,
    bool RedTint = false);
