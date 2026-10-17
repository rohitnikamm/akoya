import Foundation

/// A screen of the onboarding window: welcome, one missing permission, or the practice step.
public enum OnboardingStep: Hashable, Sendable {
    case welcome, permission(Permission), tryIt

    /// Position for the page dots: welcome 0, permissions 1, try it 2.
    var pageIndex: Int {
        switch self {
        case .welcome: 0
        case .permission: 1
        case .tryIt: 2
        }
    }
}

/// Which onboarding step is showing, and where it goes next. Permissions are asked for in the
/// order Microphone → Accessibility → Input Monitoring, skipping any already granted.
public struct OnboardingFlow: Equatable, Sendable {
    public private(set) var step: OnboardingStep

    public init(start: OnboardingStep = .welcome) {
        step = start
    }

    /// From the welcome step ("Get started"): on to the first missing permission, or straight
    /// to the practice step when everything is granted.
    public mutating func next(granted: (Permission) -> Bool) {
        step = Self.firstIncomplete(granted: granted)
    }

    /// Re-checks the active permission. When it has been granted, moves on to the next missing
    /// one (or the practice step) and returns true so the caller can tap the haptic.
    public mutating func refresh(granted: (Permission) -> Bool) -> Bool {
        guard case .permission(let active) = step, granted(active) else { return false }
        step = Self.firstIncomplete(granted: granted)
        return true
    }

    /// Where to resume: the first missing permission, or the practice step.
    public static func firstIncomplete(granted: (Permission) -> Bool) -> OnboardingStep {
        Permission.allCases.first { !granted($0) }.map(OnboardingStep.permission) ?? .tryIt
    }
}

extension OnboardingFlow {
    /// Where onboarding opens at launch, or nil when it should stay closed. First launch shows
    /// the whole flow; a relaunch before finishing resumes at the first missing permission
    /// when at least one is already on; once completed, only a missing permission reopens it.
    public static func launchStart(completed: Bool, granted: (Permission) -> Bool) -> OnboardingStep? {
        let anyGranted = Permission.allCases.contains(where: granted)
        let allGranted = Permission.allCases.allSatisfy(granted)
        if completed { return allGranted ? nil : firstIncomplete(granted: granted) }
        return anyGranted ? firstIncomplete(granted: granted) : .welcome
    }
}

/// How the onboarding window was dismissed.
enum OnboardingExit {
    /// "Done" after a practice dictation.
    case done
    /// "Skip" on the practice step.
    case skipped
    /// The × button, or the app quitting; setup is not marked complete.
    case closed
}

extension ModelState {
    /// The small footer line on the permissions and practice steps while the speech model is
    /// not usable yet; nil once it is ready.
    var onboardingStatusLine: String? {
        switch self {
        case .ready: nil
        case .notReady, .downloading(nil): "Getting the speech model ready…"
        case .downloading(let fraction?):
            "Getting the speech model ready… \(Int((min(max(fraction, 0), 1) * 100).rounded()))%"
        case .failed: "The speech model couldn’t load."
        }
    }

    /// The footer offers a Retry button next to the status line.
    var onboardingShowsRetry: Bool {
        if case .failed = self { return true }
        return false
    }
}
