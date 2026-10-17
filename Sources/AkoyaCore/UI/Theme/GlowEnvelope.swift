import Foundation

/// Smooths the voice level that drives the rim glow so it breathes rather than flickers:
/// a one-pole smoother with a fast attack and a slower release (`α = 1 − exp(−dt/τ)`).
public struct GlowEnvelope: Sendable {
    public let attack: TimeInterval
    public let release: TimeInterval
    public private(set) var value: Double = 0

    public init(attack: TimeInterval = 0.10, release: TimeInterval = 0.45) {
        self.attack = attack
        self.release = release
    }

    /// Moves the smoothed value toward `target` over `dt` seconds and returns it.
    public mutating func step(target: Double, dt: TimeInterval) -> Double {
        guard dt > 0 else { return value }
        let tau = target > value ? attack : release
        let alpha = tau > 0 ? 1 - exp(-dt / tau) : 1
        value += (target - value) * alpha
        return value
    }

    /// Glow opacity for a smoothed voice `level` (0…1): `0.2 + 0.6 × level` while recording,
    /// 0 at rest, and a fixed 0.5 while recording with Reduce Motion on.
    public static func opacity(level: Double, recording: Bool, reduceMotion: Bool) -> Double {
        guard recording else { return 0 }
        if reduceMotion { return 0.5 }
        return 0.2 + 0.6 * min(max(level, 0), 1)
    }
}
