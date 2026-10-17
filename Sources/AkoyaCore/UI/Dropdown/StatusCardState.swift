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

    public var chipTitle: String {
        switch self {
        case .ready: "Ready"
        case .listening: "Listening"
        case .transcribing: "Transcribing"
        case .downloading(let fraction?): "Downloading \(Int((min(max(fraction, 0), 1) * 100).rounded()))%"
        case .downloading(nil): "Downloading…"
        case .modelFailed: "Model failed"
        case .setupNeeded: "Setup needed"
        }
    }
}
