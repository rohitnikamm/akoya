import AVFoundation
import Testing
@testable import AkoyaCore

/// Pure decision logic only: these never touch real TCC state or call the real request/open hooks.
struct PermissionsTests {
    private func action(_ p: Permission, _ mic: AVAuthorizationStatus = .notDetermined, granted: Bool = false) -> OnboardingAction {
        Permissions.onboardingAction(for: p, microphoneStatus: mic, granted: granted)
    }

    @Test func grantedDoesNothing() {
        for p in Permission.allCases {
            #expect(action(p, .authorized, granted: true) == OnboardingAction.none)
        }
    }

    @Test func undeterminedMicrophoneOnlyRequests() {
        #expect(action(.microphone, .notDetermined) == .request)
    }

    @Test func deniedOrRestrictedMicrophoneOpensSettings() {
        #expect(action(.microphone, .denied) == .openSettings)
        #expect(action(.microphone, .restricted) == .openSettings)
    }

    @Test func accessibilityRequestsThenOpens() {
        #expect(action(.accessibility) == .requestThenOpenSettings)
    }

    @Test func onlyMicrophoneAndAccessibilityAreNeeded() {
        #expect(Permission.allCases == [.microphone, .accessibility])
    }

    @Test func performCallsHooksInOrder() {
        var calls: [String] = []
        Permissions.performOnboardingAction(
            for: .accessibility, microphoneStatus: .notDetermined, granted: false,
            request: { calls.append("request \($0)") }, openSettings: { calls.append("open \($0)") })
        #expect(calls == ["request accessibility", "open accessibility"])

        calls = []
        Permissions.performOnboardingAction(
            for: .microphone, microphoneStatus: .denied, granted: false,
            request: { calls.append("request \($0)") }, openSettings: { calls.append("open \($0)") })
        #expect(calls == ["open microphone"])

        calls = []
        Permissions.performOnboardingAction(
            for: .microphone, microphoneStatus: .notDetermined, granted: false,
            request: { calls.append("request \($0)") }, openSettings: { calls.append("open \($0)") })
        #expect(calls == ["request microphone"])

        calls = []
        Permissions.performOnboardingAction(
            for: .accessibility, microphoneStatus: .authorized, granted: true,
            request: { calls.append("request \($0)") }, openSettings: { calls.append("open \($0)") })
        #expect(calls.isEmpty)
    }
}
