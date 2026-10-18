import AppKit

/// Hands keyboard focus back to the previously active app when Akoya has nothing left on
/// screen that takes focus. (Onboarding has its own `OnboardingWindowController`; the dropdown
/// is a non-activating panel.)
@MainActor
public enum WindowPresenter {
    /// True when no other Akoya window is visible. Pure so it can be unit tested without AppKit.
    nonisolated static func shouldHideApp(othersVisible: [Bool]) -> Bool {
        !othersVisible.contains(true)
    }

    /// Akoya is an accessory app, so when its last window closes the app would otherwise stay
    /// frontmost and the next ⌘V would land in Akoya. Hiding hands focus back to the previously
    /// active app. Only windows that can become main count (onboarding), not the status item
    /// or the dropdown panel.
    static func handBackFocus(closing window: NSWindow) {
        let others = NSApp.windows.filter { $0 !== window && $0.canBecomeMain }
        if shouldHideApp(othersVisible: others.map(\.isVisible)) {
            NSApp.hide(nil)
        }
    }

    /// For the dropdown closing: reopening Akoya from Spotlight or Finder activates it, so
    /// once the panel closes, hand focus back unless an onboarding window is showing.
    public static func handBackFocusIfActive() {
        guard NSApp.isActive else { return }
        let others = NSApp.windows.filter { $0.canBecomeMain }
        if shouldHideApp(othersVisible: others.map(\.isVisible)) {
            NSApp.hide(nil)
        }
    }
}
