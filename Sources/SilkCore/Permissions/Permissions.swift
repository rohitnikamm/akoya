import AppKit
import ApplicationServices
import AVFoundation
import CoreGraphics

public enum Permission: CaseIterable, Sendable {
    case microphone, accessibility, inputMonitoring
}

/// What the onboarding button should do for a permission that may not be granted yet.
public enum OnboardingAction: Equatable, Sendable {
    case none
    /// Ask macOS, which shows its own prompt and lists Silk in the matching pane.
    case request
    case openSettings
    /// Ask first so Silk appears in the pane, then open that pane.
    case requestThenOpenSettings
}

/// Thin wrapper over the macOS privacy (TCC) checks Silk depends on.
public enum Permissions {
    public static func isGranted(_ p: Permission) -> Bool {
        switch p {
        case .microphone:
            AVCaptureDevice.authorizationStatus(for: .audio) == .authorized
        case .accessibility:
            AXIsProcessTrusted()
        case .inputMonitoring:
            CGPreflightListenEventAccess()
        }
    }

    /// Triggers the system prompt where macOS offers one.
    public static func request(_ p: Permission) {
        switch p {
        case .microphone:
            AVCaptureDevice.requestAccess(for: .audio) { _ in }
        case .accessibility:
            // Value of `kAXTrustedCheckOptionPrompt`, spelled out because the imported
            // global is not concurrency-safe under Swift 6.
            _ = AXIsProcessTrustedWithOptions(["AXTrustedCheckOptionPrompt": true] as CFDictionary)
        case .inputMonitoring:
            _ = CGRequestListenEventAccess()
        }
    }

    public static func openSettings(for p: Permission) {
        let pane = switch p {
        case .microphone: "Privacy_Microphone"
        case .accessibility: "Privacy_Accessibility"
        case .inputMonitoring: "Privacy_ListenEvent"
        }
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane)") else { return }
        NSWorkspace.shared.open(url)
    }

    public static var allGranted: Bool {
        Permission.allCases.allSatisfy(isGranted)
    }

    /// Decides what the onboarding button does. macOS only lists an app in the Microphone and
    /// Input Monitoring panes after it has asked for access, so ask before sending the user there.
    /// A microphone that was already denied or restricted can only be changed in Settings.
    public static func onboardingAction(
        for p: Permission,
        microphoneStatus: AVAuthorizationStatus,
        granted: Bool
    ) -> OnboardingAction {
        if granted { return .none }
        switch p {
        case .microphone:
            return microphoneStatus == .notDetermined ? .request : .openSettings
        case .accessibility, .inputMonitoring:
            return .requestThenOpenSettings
        }
    }

    /// Carries out `onboardingAction` for the current state, using the given request/open hooks.
    public static func performOnboardingAction(
        for p: Permission,
        microphoneStatus: AVAuthorizationStatus = AVCaptureDevice.authorizationStatus(for: .audio),
        granted: Bool,
        request: (Permission) -> Void = Permissions.request(_:),
        openSettings: (Permission) -> Void = Permissions.openSettings(for:)
    ) {
        switch onboardingAction(for: p, microphoneStatus: microphoneStatus, granted: granted) {
        case .none: break
        case .request: request(p)
        case .openSettings: openSettings(p)
        case .requestThenOpenSettings:
            request(p)
            openSettings(p)
        }
    }
}
