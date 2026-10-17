import Foundation
import Testing
@testable import AkoyaCore

@MainActor
struct AkoyaSettingsTests {
    private func makeDefaults() -> UserDefaults {
        UserDefaults(suiteName: "akoya-test-\(UUID())")!
    }

    @Test func defaultsMatchSpec() {
        let settings = AkoyaSettings(defaults: makeDefaults())
        #expect(settings.hotkey == .rightOption)
        #expect(settings.mode == .hold)
        #expect(settings.look == .pixel)
        #expect(settings.redTint == false)
        #expect(settings.hideIdleIcon == false)
        #expect(settings.expandAnimation == false)
    }

    @Test func valuesPersist() {
        let defaults = makeDefaults()
        let first = AkoyaSettings(defaults: defaults)
        first.hotkey = .rightCommand
        first.look = .solid
        first.mode = .toggle
        first.redTint = true
        first.hideIdleIcon = true
        first.expandAnimation = true

        let second = AkoyaSettings(defaults: defaults)
        #expect(second.hotkey == .rightCommand)
        #expect(second.look == .solid)
        #expect(second.mode == .toggle)
        #expect(second.redTint)
        #expect(second.hideIdleIcon)
        #expect(second.expandAnimation)
    }

    @Test func invalidStoredValuesFallBackToDefaults() {
        let defaults = makeDefaults()
        defaults.set("bogus", forKey: "akoya.hotkey")
        defaults.set("nope", forKey: "akoya.mode")
        defaults.set(42, forKey: "akoya.look")
        let settings = AkoyaSettings(defaults: defaults)
        #expect(settings.hotkey == .rightOption)
        #expect(settings.mode == .hold)
        #expect(settings.look == .pixel)
    }

    @Test func settingsDefaultsUnderAkoyaKeys() {
        let defaults = makeDefaults()
        let settings = AkoyaSettings(defaults: defaults)
        #expect(settings.onboardingCompleted == false)

        settings.hotkey = .rightCommand
        settings.mode = .toggle
        settings.look = .solid
        settings.redTint = true
        settings.hideIdleIcon = true
        settings.expandAnimation = true

        #expect(defaults.string(forKey: "akoya.hotkey") == HotkeyKey.rightCommand.rawValue)
        #expect(defaults.string(forKey: "akoya.mode") == DictationMode.toggle.rawValue)
        #expect(defaults.string(forKey: "akoya.look") == WaveLook.solid.rawValue)
        #expect(defaults.bool(forKey: "akoya.redTint"))
        #expect(defaults.bool(forKey: "akoya.hideIdleIcon"))
        #expect(defaults.bool(forKey: "akoya.expandAnimation"))
    }

    @Test func onboardingCompletedPersists() {
        let defaults = makeDefaults()
        let first = AkoyaSettings(defaults: defaults)
        first.onboardingCompleted = true
        #expect(defaults.bool(forKey: "akoya.onboardingCompleted"))

        let second = AkoyaSettings(defaults: defaults)
        #expect(second.onboardingCompleted)
    }
}
