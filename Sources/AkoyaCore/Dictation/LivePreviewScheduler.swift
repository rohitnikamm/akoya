import Foundation

/// Decides when the practice-mode live preview may re-transcribe the audio captured so far.
///
/// A pass runs only when no other pass is in flight, the recording has risen above the
/// silence gate (`AppCoordinator.silenceMaxStep`) and holds at least `minAudio` seconds, and
/// `interval` seconds have passed since the last pass started.
public struct LivePreviewScheduler {
    public let interval: TimeInterval
    public let minAudio: TimeInterval
    private var lastRun: TimeInterval?

    public init(interval: TimeInterval = 0.6, minAudio: TimeInterval = 0.5) {
        self.interval = interval
        self.minAudio = minAudio
    }

    /// Returns true, and starts the interval, when a pass should run now. A tick that is
    /// skipped (in flight, silent, too short) does not consume the interval.
    public mutating func shouldRun(now: TimeInterval, audioSeconds: Double,
                                   loudestStep: Int, inFlight: Bool) -> Bool {
        guard !inFlight,
              loudestStep > AppCoordinator.silenceMaxStep,
              audioSeconds >= minAudio else { return false }
        if let lastRun, now - lastRun < interval { return false }
        lastRun = now
        return true
    }
}
