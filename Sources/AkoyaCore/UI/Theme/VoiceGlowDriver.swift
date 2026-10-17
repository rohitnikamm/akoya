import AppKit
import Combine
import Foundation

/// Smooths the voice-driven rim-glow opacity (the dropdown's status card, the onboarding
/// practice card) with `GlowEnvelope` at about 60 Hz, ticking only while recording and while
/// the glow fades out afterwards. Starts settled at the model's current state, so a view
/// opened mid-recording glows at once.
@MainActor
final class VoiceGlowDriver: ObservableObject {
    private static let tickInterval: TimeInterval = 1.0 / 60.0
    /// Below this the fade-out is finished and the glow is dropped.
    private static let restThreshold = 0.002

    @Published private(set) var opacity: Double = 0

    private weak var model: WaveformModel?
    private var envelope = GlowEnvelope()
    private var timer: Timer?
    private var lastTick = Date()
    private var phaseSubscription: AnyCancellable?

    init(model: WaveformModel) {
        self.model = model
        // A step far longer than either time constant lands on the target.
        opacity = envelope.step(target: Self.target(for: model), dt: 60)
        // `@Published` emits in willSet, so use the delivered phase, not `model.phase`.
        phaseSubscription = model.$phase.sink { [weak self] phase in
            if phase == .recording { self?.startTicking() }
        }
    }

    private static func target(for model: WaveformModel) -> Double {
        GlowEnvelope.opacity(
            level: model.voiceLevel, recording: model.phase == .recording,
            reduceMotion: NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
    }

    private func startTicking() {
        guard timer == nil else { return }
        lastTick = Date()
        let timer = Timer(timeInterval: Self.tickInterval, repeats: true) { [weak self] timer in
            // Stop when the owning view (and this driver with it) has gone.
            guard let self else { return timer.invalidate() }
            MainActor.assumeIsolated { self.tick() }
        }
        // .common so the glow keeps moving during event tracking.
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func tick() {
        guard let model else { return stopTicking() }
        let now = Date()
        let dt = now.timeIntervalSince(lastTick)
        lastTick = now
        let target = Self.target(for: model)
        // Reduce Motion: a fixed glow while recording, switched rather than breathing.
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        var value = envelope.step(target: target, dt: reduceMotion ? 60 : dt)
        if target == 0, value < Self.restThreshold {
            value = envelope.step(target: 0, dt: 60)
            stopTicking()
        }
        if value != opacity { opacity = value }
    }

    private func stopTicking() {
        timer?.invalidate()
        timer = nil
    }
}
