import Foundation

/// A short message the pill shows when a dictation could not start.
public enum PillMessage: Equatable, Sendable {
    case modelLoading
    case modelFailed
    case micBlocked
    case micMissing

    public var text: String {
        switch self {
        case .modelLoading: "Getting the speech model ready…"
        case .modelFailed: "Speech model failed"
        case .micBlocked: "Turn on your mic in System Settings"
        case .micMissing: "Microphone not detected"
        }
    }

    /// Alerts get the red rim; the loading message is not something the user did wrong.
    public var isAlert: Bool { self != .modelLoading }
}

/// How a dictation ended, reported once per dictation in pill mode.
public enum DictationOutcome: Equatable, Sendable {
    case pasted
    case message(PillMessage)
    /// Ended without text or message (silence, cancel, failure, screen lock): the pill hides.
    case dismissed
}
