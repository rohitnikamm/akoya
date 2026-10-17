import Foundation
import Testing
@testable import SilkCore

@MainActor
struct SilkSettingsTests {
    private func makeDefaults() -> UserDefaults {
        UserDefaults(suiteName: "silk-test-\(UUID())")!
    }

    @Test func defaultsMatchSpec() {
        let settings = SilkSettings(defaults: makeDefaults())
        #expect(settings.hotkey == .rightOption)
        #expect(settings.mode == .hold)
        #expect(settings.look == .pixel)
        #expect(settings.redTint == false)
        #expect(settings.hideIdleIcon == false)
        #expect(settings.expandAnimation == false)
    }

    @Test func valuesPersist() {
        let defaults = makeDefaults()
        let first = SilkSettings(defaults: defaults)
        first.hotkey = .rightCommand
        first.look = .solid
        first.mode = .toggle
        first.redTint = true
        first.hideIdleIcon = true
        first.expandAnimation = true

        let second = SilkSettings(defaults: defaults)
        #expect(second.hotkey == .rightCommand)
        #expect(second.look == .solid)
        #expect(second.mode == .toggle)
        #expect(second.redTint)
        #expect(second.hideIdleIcon)
        #expect(second.expandAnimation)
    }

    @Test func invalidStoredValuesFallBackToDefaults() {
        let defaults = makeDefaults()
        defaults.set("bogus", forKey: "silk.hotkey")
        defaults.set("nope", forKey: "silk.mode")
        defaults.set(42, forKey: "silk.look")
        let settings = SilkSettings(defaults: defaults)
        #expect(settings.hotkey == .rightOption)
        #expect(settings.mode == .hold)
        #expect(settings.look == .pixel)
    }
}
