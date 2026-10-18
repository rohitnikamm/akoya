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
        #expect(settings.look == .pixel)
        #expect(settings.redTint == false)
        #expect(settings.hideIdleIcon == false)
    }

    @Test func valuesPersist() {
        let defaults = makeDefaults()
        let first = AkoyaSettings(defaults: defaults)
        first.hotkey = .rightCommand
        first.look = .solid
        first.redTint = true
        first.hideIdleIcon = true

        let second = AkoyaSettings(defaults: defaults)
        #expect(second.hotkey == .rightCommand)
        #expect(second.look == .solid)
        #expect(second.redTint)
        #expect(second.hideIdleIcon)
    }

    @Test func invalidStoredValuesFallBackToDefaults() {
        let defaults = makeDefaults()
        defaults.set("bogus", forKey: "akoya.hotkey")
        defaults.set(42, forKey: "akoya.look")
        let settings = AkoyaSettings(defaults: defaults)
        #expect(settings.hotkey == .rightOption)
        #expect(settings.look == .pixel)
    }

    @Test func settingsDefaultsUnderAkoyaKeys() {
        let defaults = makeDefaults()
        let settings = AkoyaSettings(defaults: defaults)
        #expect(settings.onboardingCompleted == false)

        settings.hotkey = .rightCommand
        settings.look = .solid
        settings.redTint = true
        settings.hideIdleIcon = true

        #expect(defaults.string(forKey: "akoya.hotkey") == HotkeyKey.rightCommand.rawValue)
        #expect(defaults.string(forKey: "akoya.look") == WaveLook.solid.rawValue)
        #expect(defaults.bool(forKey: "akoya.redTint"))
        #expect(defaults.bool(forKey: "akoya.hideIdleIcon"))
    }

    @Test func onboardingCompletedPersists() {
        let defaults = makeDefaults()
        let first = AkoyaSettings(defaults: defaults)
        first.onboardingCompleted = true
        #expect(defaults.bool(forKey: "akoya.onboardingCompleted"))

        let second = AkoyaSettings(defaults: defaults)
        #expect(second.onboardingCompleted)
    }

    @Test func inputDeviceUIDDefaultsToNilAndRoundTrips() {
        let defaults = makeDefaults()
        let first = AkoyaSettings(defaults: defaults)
        #expect(first.inputDeviceUID == nil)

        first.inputDeviceUID = "AppleUSBAudioEngine:1234"
        #expect(defaults.string(forKey: "akoya.inputDeviceUID") == "AppleUSBAudioEngine:1234")
        #expect(AkoyaSettings(defaults: defaults).inputDeviceUID == "AppleUSBAudioEngine:1234")

        first.inputDeviceUID = nil
        #expect(defaults.object(forKey: "akoya.inputDeviceUID") == nil)
        #expect(AkoyaSettings(defaults: defaults).inputDeviceUID == nil)
    }
}
