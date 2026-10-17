import Testing
@testable import SilkCore

@Test func phasesAreDistinct() { #expect(DictationPhase.idle != .recording) }
