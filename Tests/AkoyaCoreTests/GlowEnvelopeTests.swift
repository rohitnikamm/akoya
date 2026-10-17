import Testing
@testable import AkoyaCore

@Test func opacityAtRestIsZero() {
    #expect(GlowEnvelope.opacity(level: 0.9, recording: false, reduceMotion: false) == 0)
}

@Test func opacityMapsLevel() {
    #expect(GlowEnvelope.opacity(level: 1, recording: true, reduceMotion: false) == 0.8)
    #expect(GlowEnvelope.opacity(level: 0, recording: true, reduceMotion: false) == 0.2)
}

@Test func opacityClampsLevelOutsideUnitRange() {
    #expect(GlowEnvelope.opacity(level: 3, recording: true, reduceMotion: false) == 0.8)
    #expect(GlowEnvelope.opacity(level: -1, recording: true, reduceMotion: false) == 0.2)
}

@Test func reduceMotionIsConstant() {
    #expect(GlowEnvelope.opacity(level: 0.1, recording: true, reduceMotion: true) == 0.5)
}

@Test func reduceMotionAtRestIsZero() {
    #expect(GlowEnvelope.opacity(level: 0.1, recording: false, reduceMotion: true) == 0)
}

@Test func attackFasterThanRelease() {
    var rising = GlowEnvelope()
    #expect(rising.step(target: 1, dt: 0.1) >= 0.6)

    var falling = GlowEnvelope()
    for _ in 0..<600 { _ = falling.step(target: 1, dt: 1.0 / 60) }
    #expect(falling.step(target: 0, dt: 0.1) >= 0.6)
}

@Test func envelopeConverges() {
    var envelope = GlowEnvelope()
    var value = 0.0
    for _ in 0..<120 { value = envelope.step(target: 1, dt: 1.0 / 60) }
    #expect(value > 0.99)
}
