import Foundation

/// What the dropdown's status card shows. Precedence: an active recording or transcription
/// first, then missing permissions, then a failed model, then download progress, then ready.
public enum StatusCardState: Equatable, Sendable {
    case ready, listening, transcribing, downloading(Double?), modelFailed, setupNeeded

    public static func from(phase: DictationPhase, model: ModelState, permissionsGranted: Bool) -> StatusCardState {
        switch phase {
        case .recording: return .listening
        case .transcribing: return .transcribing
        case .idle, .error: break
        }
        guard permissionsGranted else { return .setupNeeded }
        switch model {
        case .failed: return .modelFailed
        case .downloading(let fraction): return .downloading(fraction)
        // Before the first progress report the model is still being fetched or compiled.
        case .notReady: return .downloading(nil)
        case .ready: return .ready
        }
    }

    /// Whether the card offers Retry. A failed model keeps its Retry even while a missing
    /// permission outranks it in the card state, or there would be no way to retry at all.
    public func showsRetry(model: ModelState) -> Bool {
        guard case .failed = model else { return false }
        return self == .modelFailed || self == .setupNeeded
    }

    /// The line under "Hold … to talk", shown only when something needs saying; nil while ready,
    /// listening or transcribing (the card has no status chip).
    public var statusLine: String? {
        switch self {
        case .ready, .listening, .transcribing: nil
        case .downloading(let fraction?):
            "Downloading speech model · \(Int((min(max(fraction, 0), 1) * 100).rounded()))%"
        // No progress report yet: the files may already be on disk and only be loading.
        case .downloading(nil): "Getting the speech model ready…"
        case .modelFailed: "Model failed"
        case .setupNeeded: "Setup needed"
        }
    }

}
