import AppKit
import SwiftUI

/// Hosts the settings and onboarding views in plain windows. Silk is an `LSUIElement` app, so
/// each window is brought forward explicitly.
@MainActor
public final class WindowPresenter {
    private var settingsWindow: NSWindow?
    private var onboardingWindow: NSWindow?

    public init() {}

    public func showSettings(_ settings: SilkSettings) {
        settingsWindow = present(settingsWindow, title: "Silk Settings", content: SettingsView(settings: settings))
    }

    public func showOnboarding() {
        onboardingWindow = present(onboardingWindow, title: "Welcome to Silk", content: OnboardingView())
    }

    private func present<Content: View>(_ existing: NSWindow?, title: String, content: Content) -> NSWindow {
        let window = existing ?? makeWindow(title: title, content: content)
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        return window
    }

    private func makeWindow<Content: View>(title: String, content: Content) -> NSWindow {
        let window = NSWindow(contentViewController: NSHostingController(rootView: content))
        window.title = title
        window.styleMask = [.titled, .closable]
        window.isReleasedWhenClosed = false
        window.center()
        return window
    }
}
