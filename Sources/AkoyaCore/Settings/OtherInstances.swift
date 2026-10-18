import AppKit

/// A running app that can be asked to quit and knows its process id.
public protocol RunningInstance: TerminableApp {
    var processIdentifier: pid_t { get }
}

extension NSRunningApplication: RunningInstance {}

/// Two copies of Akoya (say, a development build and the Homebrew install) would both listen
/// for the hotkey and paste every dictation twice. The newest launch wins: it asks every other
/// running copy to quit.
public enum OtherInstances {
    /// Asks every running app with `bundleIdentifier`, except this process, to quit, and returns
    /// how many were asked. Call before the hotkey monitor starts.
    @discardableResult
    public static func quitRunning(
        bundleIdentifier: String? = Bundle.main.bundleIdentifier,
        ownPID: pid_t = ProcessInfo.processInfo.processIdentifier,
        lookup: (String) -> [any RunningInstance] = { NSRunningApplication.runningApplications(withBundleIdentifier: $0) }
    ) -> Int {
        guard let bundleIdentifier else { return 0 }
        let others = lookup(bundleIdentifier).filter { $0.processIdentifier != ownPID }
        for app in others { app.terminate() }
        return others.count
    }
}
