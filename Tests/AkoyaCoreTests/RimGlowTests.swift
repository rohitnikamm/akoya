import Testing
@testable import AkoyaCore

/// Peak voice (`GlowEnvelope` opacity 0.8) must draw the approved "peak" render, which was
/// drawn at full strength (docs/design/blue-glow-target.html).
@Test func rimGlowPeakVoiceDrawsFullStrength() {
    #expect(RimGlowScale.layerOpacity(intensity: 0.8) == 1)
    #expect(RimGlowScale.layerOpacity(intensity: 0) == 0)
    #expect(abs(RimGlowScale.layerOpacity(intensity: 0.44) - 0.55) < 1e-9)
}

@Test func rimGlowLayerOpacityIsClamped() {
    #expect(RimGlowScale.layerOpacity(intensity: 1) == 1)
    #expect(RimGlowScale.layerOpacity(intensity: -0.5) == 0)
}
