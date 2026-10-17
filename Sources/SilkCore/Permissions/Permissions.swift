import AppKit
import ApplicationServices
import AVFoundation
import CoreGraphics

public enum Permission: CaseIterable, Sendable {
    case microphone, accessibility, inputMonitoring
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
}
