import Combine
import Foundation
import os
import ServiceManagement

/// User preferences. Each value is written to the injected `UserDefaults` as it changes and
/// read back in `init`, falling back to the spec default when a value is missing or invalid.
/// `launchAtLogin` is not stored in defaults; it mirrors `SMAppService.mainApp`.
@MainActor
public final class AkoyaSettings: ObservableObject {
    private enum Key {
        static let hotkey = "akoya.hotkey"
        static let look = "akoya.look"
        static let redTint = "akoya.redTint"
        static let hideIdleIcon = "akoya.hideIdleIcon"
        static let onboardingCompleted = "akoya.onboardingCompleted"
        static let inputDeviceUID = "akoya.inputDeviceUID"
    }

    private static let log = Logger(subsystem: "com.rohitnikamm.akoya", category: "settings")

    private let defaults: UserDefaults
    private var isRevertingLaunchAtLogin = false

    @Published public var hotkey: HotkeyKey {
        didSet { defaults.set(hotkey.rawValue, forKey: Key.hotkey) }
    }
    @Published public var look: WaveLook {
        didSet { defaults.set(look.rawValue, forKey: Key.look) }
    }
    @Published public var redTint: Bool {
        didSet { defaults.set(redTint, forKey: Key.redTint) }
    }
    @Published public var hideIdleIcon: Bool {
        didSet { defaults.set(hideIdleIcon, forKey: Key.hideIdleIcon) }
    }
    @Published public var onboardingCompleted: Bool {
        didSet { defaults.set(onboardingCompleted, forKey: Key.onboardingCompleted) }
    }
    /// UID of the chosen microphone; nil means the system default.
    @Published public var inputDeviceUID: String? {
        didSet {
            if let inputDeviceUID {
                defaults.set(inputDeviceUID, forKey: Key.inputDeviceUID)
            } else {
                defaults.removeObject(forKey: Key.inputDeviceUID)
            }
        }
    }
    @Published public var launchAtLogin: Bool {
        didSet {
            guard !isRevertingLaunchAtLogin, launchAtLogin != oldValue else { return }
            do {
                if launchAtLogin {
                    try SMAppService.mainApp.register()
                } else {
                    try SMAppService.mainApp.unregister()
                }
            } catch {
                Self.log.error("Launch at login change failed: \(error.localizedDescription, privacy: .public)")
                isRevertingLaunchAtLogin = true
                launchAtLogin = oldValue
                isRevertingLaunchAtLogin = false
            }
        }
    }

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        hotkey = defaults.string(forKey: Key.hotkey).flatMap(HotkeyKey.init(rawValue:)) ?? .rightOption
        look = defaults.string(forKey: Key.look).flatMap(WaveLook.init(rawValue:)) ?? .pixel
        redTint = defaults.bool(forKey: Key.redTint)
        hideIdleIcon = defaults.bool(forKey: Key.hideIdleIcon)
        onboardingCompleted = defaults.bool(forKey: Key.onboardingCompleted)
        inputDeviceUID = defaults.string(forKey: Key.inputDeviceUID)
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }
}
