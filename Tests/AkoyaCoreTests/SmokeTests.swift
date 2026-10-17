import Testing
@testable import AkoyaCore

@Test func phasesAreDistinct() { #expect(DictationPhase.idle != .recording) }
