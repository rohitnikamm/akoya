import SwiftUI

public struct SettingsView: View {
    @ObservedObject private var settings: AkoyaSettings

    public init(settings: AkoyaSettings) {
        self.settings = settings
    }

    public var body: some View {
        Form {
            Section("Dictation") {
                Picker("Hotkey", selection: $settings.hotkey) {
                    ForEach(HotkeyKey.allCases, id: \.self) { Text($0.displayName).tag($0) }
                }
                Picker("Mode", selection: $settings.mode) {
                    ForEach(DictationMode.allCases, id: \.self) { Text($0.displayName).tag($0) }
                }
            }
            Section("Appearance") {
                Picker("Waveform", selection: $settings.look) {
                    ForEach(WaveLook.allCases, id: \.self) { Text($0.displayName).tag($0) }
                }
                Toggle("Red tint while recording", isOn: $settings.redTint)
                Toggle("Hide icon when idle", isOn: $settings.hideIdleIcon)
                Toggle("Expand animation", isOn: $settings.expandAnimation)
            }
            Section("General") {
                Toggle("Launch at login", isOn: $settings.launchAtLogin)
            }
        }
        .tint(AkoyaTheme.accentTop)
        .formStyle(.grouped)
        .frame(width: 380)
        .fixedSize(horizontal: false, vertical: true)
    }
}

public extension HotkeyKey {
    /// Human-readable key name, shown in Settings and the menu.
    var displayName: String {
        switch self {
        case .rightOption: "Right Option"
        case .rightCommand: "Right Command"
        case .rightControl: "Right Control"
        }
    }
}

private extension DictationMode {
    var displayName: String {
        switch self {
        case .hold: "Hold to talk"
        case .toggle: "Press to start, press to stop"
        }
    }
}

private extension WaveLook {
    var displayName: String {
        switch self {
        case .pixel: "Pixel"
        case .solid: "Solid"
        }
    }
}
