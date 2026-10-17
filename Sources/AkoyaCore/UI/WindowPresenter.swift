import AppKit
import SwiftUI

/// Hosts the settings and onboarding views in plain windows. Akoya is an `LSUIElement` app, so
/// each window is brought forward explicitly.
@MainActor
public final class WindowPresenter {
    private var settingsWindow: NSWindow?
    private var onboardingWindow: NSWindow?
    private var closeObservers: [ObjectIdentifier: NSObjectProtocol] = [:]

    public init() {}

    public func showSettings(_ settings: AkoyaSettings) {
        settingsWindow = present(settingsWindow, title: "Akoya Settings", content: SettingsView(settings: settings))
    }

    public func showOnboarding() {
        onboardingWindow = present(onboardingWindow, title: "Welcome to Akoya", content: OnboardingView())
    }

    /// True when no other Akoya window is visible. Pure so it can be unit tested without AppKit.
    nonisolated static func shouldHideApp(othersVisible: [Bool]) -> Bool {
        !othersVisible.contains(true)
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
        observeClose(of: window)
        return window
    }

    /// Akoya is an accessory app, so when its last window closes the app would otherwise stay
    /// frontmost and the next ⌘V would land in Akoya. Hiding hands focus back to the previously
    /// active app. The closed window is dropped too, so its view (and the onboarding permission
    /// polling) is torn down; the next show builds a fresh one.
    private func observeClose(of window: NSWindow) {
        let key = ObjectIdentifier(window)
        closeObservers[key] = NotificationCenter.default.addObserver(
            forName: NSWindow.willCloseNotification, object: window, queue: .main
        ) { [weak self, weak window] _ in
            MainActor.assumeIsolated {
                guard let self, let window else { return }
                if let token = self.closeObservers.removeValue(forKey: key) {
                    NotificationCenter.default.removeObserver(token)
                }
                let others = [self.settingsWindow, self.onboardingWindow]
                    .compactMap { $0 }.filter { $0 !== window }
                if Self.shouldHideApp(othersVisible: others.map(\.isVisible)) {
                    NSApp.hide(nil)
                }
                // Release after AppKit finishes closing the window.
                DispatchQueue.main.async {
                    MainActor.assumeIsolated {
                        if self.settingsWindow === window { self.settingsWindow = nil }
                        if self.onboardingWindow === window { self.onboardingWindow = nil }
                    }
                }
            }
        }
    }
}
