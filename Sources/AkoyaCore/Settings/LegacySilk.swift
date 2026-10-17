import AppKit

/// Something that can be asked to quit; `NSRunningApplication` in the app, a fake in tests.
public protocol TerminableApp {
    @discardableResult func terminate() -> Bool
}

extension NSRunningApplication: TerminableApp {}

/// The app was called Silk before 0.2.0 and used another bundle identifier, so an old Silk can
/// still be running (and, with Launch at Login on, relaunching) next to Akoya. Both would
/// listen for the same hotkey and paste every dictation twice.
public enum LegacySilk {
    public static let bundleIdentifier = "com.rohitnikamm.silk"

    /// Asks every running Silk to quit and returns how many were asked. Never touches any
    /// other bundle identifier. Call before the hotkey monitor starts.
    @discardableResult
    public static func quitRunning(
        lookup: (String) -> [any TerminableApp] = { NSRunningApplication.runningApplications(withBundleIdentifier: $0) }
    ) -> Int {
        let running = lookup(bundleIdentifier)
        for app in running { app.terminate() }
        return running.count
    }
}
