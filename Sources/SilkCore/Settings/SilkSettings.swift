import Combine
import Foundation
import os
import ServiceManagement

/// User preferences. Each value is written to the injected `UserDefaults` as it changes and
/// read back in `init`, falling back to the spec default when a value is missing or invalid.
/// `launchAtLogin` is not stored in defaults; it mirrors `SMAppService.mainApp`.
@MainActor
public final class SilkSettings: ObservableObject {
    private enum Key {
        static let hotkey = "silk.hotkey"
        static let mode = "silk.mode"
        static let look = "silk.look"
        static let redTint = "silk.redTint"
        static let hideIdleIcon = "silk.hideIdleIcon"
        static let expandAnimation = "silk.expandAnimation"
    }

    private static let log = Logger(subsystem: "com.rohitnikamm.silk", category: "settings")

    private let defaults: UserDefaults
    private var isRevertingLaunchAtLogin = false

    @Published public var hotkey: HotkeyKey {
        didSet { defaults.set(hotkey.rawValue, forKey: Key.hotkey) }
    }
    @Published public var mode: DictationMode {
        didSet { defaults.set(mode.rawValue, forKey: Key.mode) }
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
    @Published public var expandAnimation: Bool {
        didSet { defaults.set(expandAnimation, forKey: Key.expandAnimation) }
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
        mode = defaults.string(forKey: Key.mode).flatMap(DictationMode.init(rawValue:)) ?? .hold
        look = defaults.string(forKey: Key.look).flatMap(WaveLook.init(rawValue:)) ?? .pixel
        redTint = defaults.bool(forKey: Key.redTint)
        hideIdleIcon = defaults.bool(forKey: Key.hideIdleIcon)
        expandAnimation = defaults.bool(forKey: Key.expandAnimation)
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }
}
