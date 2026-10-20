using Akoya.Core.Hotkey;

namespace Akoya.Core.Settings;

/// <summary>Which ONNX execution provider the speech model should prefer.</summary>
public enum InferenceProvider { Auto, Cpu }

/// <summary>User settings persisted to %AppData%\Akoya\settings.json.</summary>
public sealed record AkoyaSettings(
    HotkeyKey Hotkey = HotkeyKey.RightAlt,
    string? MicrophoneId = null,
    bool LaunchAtStartup = false,
    bool SetupDone = false,
    InferenceProvider Provider = InferenceProvider.Auto);
