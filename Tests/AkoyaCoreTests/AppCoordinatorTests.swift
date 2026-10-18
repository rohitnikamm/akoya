import Foundation
import Testing
@testable import AkoyaCore

// MARK: - Fakes

private struct FakeFailure: Error {}

@MainActor
private final class FakeAudio: AudioRecording {
    var onLevel: ((Float) -> Void)?
    var onInterrupted: (() -> Void)?
    var startError: Error?
    var samples: [Float] = [0.1, 0.2, 0.3]
    private(set) var startCount = 0
    private(set) var stopCount = 0
    private(set) var cancelCount = 0

    func start() throws {
        startCount += 1
        if let startError { throw startError }
    }
    func stop() -> [Float] { stopCount += 1; return samples }
    func snapshot() -> [Float] { samples }
    func cancel() { cancelCount += 1 }
}

private final class FakeTranscriber: Transcribing, @unchecked Sendable {
    private let lock = NSLock()
    private var _result: String
    private var _error: Error?
    private var _received: [[Float]] = []

    init(result: String = "hello") { _result = result }

    var result: String {
        get { lock.withLock { _result } }
        set { lock.withLock { _result = newValue } }
    }
    var error: Error? {
        get { lock.withLock { _error } }
        set { lock.withLock { _error = newValue } }
    }
    var received: [[Float]] { lock.withLock { _received } }

    func transcribe(_ samples: [Float]) async throws -> String {
        let (text, error) = lock.withLock { () -> (String, Error?) in
            _received.append(samples)
            return (_result, _error)
        }
        if let error { throw error }
        return text
    }
}

@MainActor
private final class FakeInserter: TextInserting {
    var error: Error?
    private(set) var inserted: [String] = []
    func insert(_ text: String) async throws {
        if let error { throw error }
        inserted.append(text)
    }
}

private final class FakeClock: @unchecked Sendable {
    var time: TimeInterval = 1000
    func advance(_ seconds: TimeInterval) { time += seconds }
}

@MainActor
private struct Rig {
    let audio = FakeAudio()
    let transcriber: FakeTranscriber
    let inserter = FakeInserter()
    let model = WaveformModel()
    let clock = FakeClock()
    let coordinator: AppCoordinator

    init(transcript: String = "hello",
         maxRecording: TimeInterval = 300, errorDisplay: TimeInterval = 0.01) {
        let transcriber = FakeTranscriber(result: transcript)
        let clock = self.clock
        self.transcriber = transcriber
        coordinator = AppCoordinator(
            audio: audio, transcriber: transcriber, inserter: inserter, model: model,
            now: { clock.time },
            maxRecording: maxRecording, errorDisplay: errorDisplay)
    }

    /// Press, speak (one loud level so the silence gate lets it through), hold past the
    /// hold threshold, release.
    func dictate() {
        coordinator.handle(.pressed)
        speak()
        clock.advance(1)
        coordinator.handle(.released)
    }

    /// Feeds one loud level, as if the user spoke.
    func speak() {
        audio.onLevel?(1.0)
    }
}

// MARK: - Tests

@MainActor @Test func holdPressReleaseInsertsTrimmedTextWithSpace() async {
    let rig = Rig(transcript: "  hello world ")
    rig.coordinator.handle(.pressed)
    #expect(rig.coordinator.phase == .recording)
    #expect(rig.coordinator.isRecording)
    rig.speak()
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(rig.inserter.inserted == ["hello world "])
    #expect(rig.coordinator.phase == .idle)
    #expect(rig.model.phase == .idle)
}

@MainActor @Test func holdThenReleaseStops() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(0.5)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(rig.audio.stopCount == 1)
    #expect(rig.audio.cancelCount == 0)
    #expect(rig.inserter.inserted == ["hello "])
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func quickTapKeepsRecording() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .recording)
    #expect(rig.audio.stopCount == 0)
    #expect(rig.audio.cancelCount == 0)
    #expect(rig.transcriber.received.isEmpty)
    #expect(rig.inserter.inserted.isEmpty)
}

@MainActor @Test func releaseJustUnderThresholdLatchesAndJustOverStops() async {
    let under = Rig()
    under.coordinator.handle(.pressed)
    under.speak()
    under.clock.advance(0.29)
    under.coordinator.handle(.released)
    #expect(under.coordinator.phase == .recording)

    let over = Rig()
    over.coordinator.handle(.pressed)
    over.speak()
    over.clock.advance(0.31)
    over.coordinator.handle(.released)
    #expect(over.coordinator.phase == .transcribing)
    await over.coordinator.pendingWork?.value
}

@MainActor @Test func tapThenPressStops() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)
    rig.clock.advance(2)  // talking, hands off the key
    rig.coordinator.handle(.pressed)
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(rig.audio.stopCount == 1)
    #expect(rig.transcriber.received.count == 1)
    #expect(rig.inserter.inserted == ["hello "])
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func releaseAfterTapStopPressIsIgnored() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)
    rig.coordinator.handle(.pressed)  // stops
    await rig.coordinator.pendingWork?.value
    #expect(rig.coordinator.phase == .idle)
    rig.coordinator.handle(.released)  // the key coming back up must do nothing
    #expect(rig.coordinator.phase == .idle)
    #expect(rig.audio.startCount == 1)
    #expect(rig.audio.stopCount == 1)
    #expect(rig.audio.cancelCount == 0)
    #expect(rig.transcriber.received.count == 1)
}

@MainActor @Test func releaseOfStopPressWhileTranscribingIsIgnored() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)
    rig.coordinator.handle(.pressed)  // stops
    #expect(rig.coordinator.phase == .transcribing)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(rig.inserter.inserted == ["hello "])
}

@MainActor @Test func escapeCancelsAfterTap() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .recording)
    rig.coordinator.handle(.cancel)
    await rig.coordinator.pendingWork?.value
    #expect(rig.audio.cancelCount == 1)
    #expect(rig.audio.stopCount == 0)
    #expect(rig.transcriber.received.isEmpty)
    #expect(rig.coordinator.phase == .idle)
    // A new recording after the cancel starts unlatched: a long hold stops on release.
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
}

@MainActor @Test func nextRecordingAfterTapStopStartsUnlatched() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)
    rig.coordinator.handle(.pressed)  // stops
    await rig.coordinator.pendingWork?.value
    rig.coordinator.handle(.released)
    rig.coordinator.handle(.pressed)  // fresh recording
    rig.speak()
    rig.clock.advance(1)
    rig.coordinator.handle(.released)  // a real hold: must stop, not be treated as latched
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(rig.audio.startCount == 2)
    #expect(rig.transcriber.received.count == 2)
}

@MainActor @Test func silentTapStillErrorCue() async {
    let rig = Rig(errorDisplay: 0.1)
    rig.coordinator.handle(.pressed)
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)  // accidental tap, nothing said
    #expect(rig.coordinator.phase == .recording)
    rig.clock.advance(2)
    rig.coordinator.handle(.pressed)
    #expect(rig.coordinator.phase == .error)
    #expect(rig.transcriber.received.isEmpty)
    await rig.coordinator.pendingWork?.value
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func tapDoesNotStopOnMaxRecordingEarlierThanConfigured() async {
    // A latched recording is still bounded by maxRecording.
    let rig = Rig(maxRecording: 0.05)
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .recording)
    while rig.coordinator.phase == .recording { await Task.yield() }
    await rig.coordinator.pendingWork?.value
    #expect(rig.inserter.inserted == ["hello "])
}

@MainActor @Test func escapeCancels() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.clock.advance(1)
    rig.coordinator.handle(.cancel)
    await rig.coordinator.pendingWork?.value
    #expect(rig.audio.cancelCount == 1)
    #expect(rig.transcriber.received.isEmpty)
    #expect(rig.inserter.inserted.isEmpty)
    #expect(rig.coordinator.phase == .idle)
    #expect(!rig.coordinator.isRecording)
}

@MainActor @Test func pressWhileTranscribingIsIgnored() async {
    let rig = Rig()
    rig.dictate()
    #expect(rig.coordinator.phase == .transcribing)
    rig.coordinator.handle(.pressed)
    #expect(rig.audio.startCount == 1)
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.count == 1)
}

@MainActor @Test func releaseWithoutPressIsIgnored() {
    let rig = Rig()
    rig.coordinator.handle(.released)
    #expect(rig.audio.startCount == 0)
    #expect(rig.audio.stopCount == 0)
    #expect(rig.audio.cancelCount == 0)
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func emptyTranscriptShowsErrorThenIdle() async {
    let rig = Rig(transcript: "   ", errorDisplay: 0.1)
    rig.dictate()
    while rig.coordinator.phase == .transcribing { await Task.yield() }
    #expect(rig.coordinator.phase == .error)
    await rig.coordinator.pendingWork?.value
    #expect(rig.inserter.inserted.isEmpty)
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func transcriberFailureShowsErrorThenIdle() async {
    let rig = Rig(errorDisplay: 0.1)
    rig.transcriber.error = FakeFailure()
    rig.dictate()
    while rig.coordinator.phase == .transcribing { await Task.yield() }
    #expect(rig.coordinator.phase == .error)
    await rig.coordinator.pendingWork?.value
    #expect(rig.inserter.inserted.isEmpty)
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func inserterFailureShowsErrorThenIdle() async {
    let rig = Rig(errorDisplay: 0.1)
    rig.inserter.error = FakeFailure()
    rig.dictate()
    while rig.coordinator.phase == .transcribing { await Task.yield() }
    #expect(rig.coordinator.phase == .error)
    await rig.coordinator.pendingWork?.value
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func audioStartFailureShowsError() async {
    let rig = Rig(errorDisplay: 0.1)
    rig.audio.startError = FakeFailure()
    rig.coordinator.handle(.pressed)
    #expect(rig.coordinator.phase == .error)
    #expect(!rig.coordinator.isRecording)
    // A release or Esc must not act on a recording that never started.
    rig.coordinator.handle(.released)
    rig.coordinator.handle(.cancel)
    #expect(rig.audio.stopCount == 0)
    await rig.coordinator.pendingWork?.value
    #expect(rig.coordinator.phase == .idle)
    #expect(rig.transcriber.received.isEmpty)
}

@MainActor @Test func interruptionTranscribesWhatWasCaptured() async {
    let rig = Rig()
    rig.audio.samples = [0.5, 0.6]
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.audio.onInterrupted?()
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received == [[0.5, 0.6]])
    #expect(rig.inserter.inserted == ["hello "])
}

@MainActor @Test func levelsFeedModelColumns() {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.audio.onLevel?(1.0)
    #expect(rig.model.columns.last == 8)
    #expect(rig.model.columns.count == 22)
}

@MainActor @Test func levelsAreIgnoredWhenNotRecordingAndResetOnStart() {
    let rig = Rig()
    rig.audio.onLevel?(1.0)
    #expect(rig.model.columns.last == 1)
    rig.coordinator.handle(.pressed)
    rig.audio.onLevel?(1.0)
    rig.clock.advance(1)
    rig.coordinator.handle(.cancel)
    rig.coordinator.handle(.pressed)
    #expect(rig.model.columns == Array(repeating: 1, count: 22))
}

@MainActor @Test func maxRecordingAutoStops() async {
    let rig = Rig(maxRecording: 0.05)
    rig.coordinator.handle(.pressed)
    rig.speak()
    try? await Task.sleep(for: .milliseconds(200))
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.count == 1)
    #expect(rig.inserter.inserted == ["hello "])
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func manualStopCancelsMaxRecordingTimer() async {
    let rig = Rig(maxRecording: 0.05)
    rig.coordinator.handle(.pressed)
    rig.clock.advance(1)
    rig.coordinator.handle(.cancel)
    try? await Task.sleep(for: .milliseconds(200))
    #expect(rig.transcriber.received.isEmpty)
    #expect(rig.audio.stopCount == 0)
}

@MainActor @Test func notReadyFlashesErrorCueWithoutRecording() async {
    let rig = Rig(errorDisplay: 0.1)
    rig.coordinator.isReady = { false }
    rig.coordinator.handle(.pressed)
    #expect(rig.coordinator.phase == .error)
    #expect(rig.audio.startCount == 0)
    await rig.coordinator.pendingWork?.value
    #expect(rig.coordinator.phase == .idle)
    #expect(rig.audio.startCount == 0)
    #expect(rig.transcriber.received.isEmpty)
}

// MARK: - Silence gate

@MainActor @Test func silentRecordingSkipsTranscription() async {
    let rig = Rig(errorDisplay: 0.1)
    rig.coordinator.handle(.pressed)
    for _ in 0..<10 { rig.audio.onLevel?(0) }
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .error)
    #expect(rig.audio.stopCount == 1)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.isEmpty)
    #expect(rig.inserter.inserted.isEmpty)
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func quietLevelsAtTheThresholdStillCountAsSilence() async {
    let rig = Rig(errorDisplay: 0.1)
    // -40 dBFS maps to step 2: the loudest level that still counts as silence.
    let rms = Float(pow(10, -40.0 / 20))
    #expect(LevelMeter.step(forRMS: rms) == AppCoordinator.silenceMaxStep)
    rig.coordinator.handle(.pressed)
    rig.audio.onLevel?(rms)
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.isEmpty)
}

@MainActor @Test func silentInterruptionSkipsTranscription() async {
    let rig = Rig(errorDisplay: 0.1)
    rig.coordinator.handle(.pressed)
    rig.audio.onLevel?(0)
    rig.audio.onInterrupted?()
    #expect(rig.coordinator.phase == .error)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.isEmpty)
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func silentMaxRecordingAutoStopSkipsTranscription() async {
    let rig = Rig(maxRecording: 0.05, errorDisplay: 0.01)
    rig.coordinator.handle(.pressed)
    try? await Task.sleep(for: .milliseconds(200))
    await rig.coordinator.pendingWork?.value
    #expect(rig.audio.stopCount == 1)
    #expect(rig.transcriber.received.isEmpty)
    #expect(rig.coordinator.phase == .idle)
}

@MainActor @Test func loudnessFromAnEarlierRecordingDoesNotCarryOver() async {
    let rig = Rig(errorDisplay: 0.01)
    rig.dictate()
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.count == 1)
    rig.coordinator.handle(.pressed)
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.count == 1)
}

// MARK: - Insert window

@MainActor
private final class SuspendingInserter: TextInserting {
    private var continuation: CheckedContinuation<Void, Never>?
    private(set) var started = false
    func insert(_ text: String) async throws {
        started = true
        await withCheckedContinuation { continuation = $0 }
    }
    func finish() { continuation?.resume(); continuation = nil }
}

@MainActor @Test func phaseStaysBusyUntilInsertReturns() async {
    let audio = FakeAudio()
    let inserter = SuspendingInserter()
    let clock = FakeClock()
    let coordinator = AppCoordinator(
        audio: audio, transcriber: FakeTranscriber(), inserter: inserter, model: WaveformModel(),
        now: { clock.time })
    coordinator.handle(.pressed)
    audio.onLevel?(1.0)
    clock.advance(1)
    coordinator.handle(.released)
    while !inserter.started { await Task.yield() }
    // Inside the paste/clipboard-restore window a new press must not start a recording.
    #expect(coordinator.phase == .transcribing)
    coordinator.handle(.pressed)
    #expect(audio.startCount == 1)
    inserter.finish()
    await coordinator.pendingWork?.value
    #expect(coordinator.phase == .idle)
}

// MARK: - Voice level and last dictation

@MainActor @Test func voiceLevelFollowsRMS() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.audio.onLevel?(1.0)
    #expect(rig.model.voiceLevel == 1)
    rig.audio.onLevel?(0)
    #expect(rig.model.voiceLevel == 0)
    rig.audio.onLevel?(1.0)
    #expect(rig.model.voiceLevel == 1)
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    #expect(rig.model.voiceLevel == 0)
    await rig.coordinator.pendingWork?.value
}

@MainActor @Test func voiceLevelResetsOnCancel() {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.audio.onLevel?(1.0)
    rig.coordinator.handle(.cancel)
    #expect(rig.model.voiceLevel == 0)
}

@MainActor @Test func voiceLevelResetsOnInterruption() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.audio.onLevel?(1.0)
    rig.audio.onInterrupted?()
    #expect(rig.model.voiceLevel == 0)
    await rig.coordinator.pendingWork?.value
}

@MainActor @Test func lastDictationSetAfterInsert() async {
    let rig = Rig(transcript: "  hello world ")
    #expect(rig.model.lastDictation == nil)
    rig.dictate()
    await rig.coordinator.pendingWork?.value
    #expect(rig.model.lastDictation == "hello world")
}

@MainActor @Test func lastDictationUnchangedWhenInsertFails() async {
    let rig = Rig()
    rig.dictate()
    await rig.coordinator.pendingWork?.value
    #expect(rig.model.lastDictation == "hello")
    rig.transcriber.result = "second"
    rig.inserter.error = FakeFailure()
    rig.dictate()
    await rig.coordinator.pendingWork?.value
    #expect(rig.model.lastDictation == "hello")
}

@MainActor @Test func pasteModeNeverPreviews() async {
    let rig = Rig()
    rig.audio.samples = Array(repeating: 0.1, count: 32_000)
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.coordinator.tickLivePreview()
    rig.clock.advance(1)
    rig.coordinator.tickLivePreview()
    rig.coordinator.handle(.released)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.count == 1)
}

// MARK: - Practice mode

@MainActor
private final class PracticeLog {
    var events: [(text: String, isFinal: Bool)] = []
    var texts: [String] { events.map(\.text) }
    var finals: [Bool] { events.map(\.isFinal) }
}

/// A transcriber whose calls suspend until the test finishes them, in any order.
private final class GatedTranscriber: Transcribing, @unchecked Sendable {
    private let lock = NSLock()
    private var continuations: [CheckedContinuation<String, Never>] = []
    var callCount: Int { lock.withLock { continuations.count } }

    func transcribe(_ samples: [Float]) async throws -> String {
        await withCheckedContinuation { continuation in
            lock.withLock { continuations.append(continuation) }
        }
    }

    func finish(call index: Int, with text: String) {
        let continuation = lock.withLock { continuations[index] }
        continuation.resume(returning: text)
    }
}

@MainActor
private func waitForCalls(_ transcriber: GatedTranscriber, _ count: Int) async {
    while transcriber.callCount < count { await Task.yield() }
}

@MainActor
private func practiceRig(transcript: String = "partial") -> (Rig, PracticeLog) {
    let rig = Rig(transcript: transcript)
    rig.audio.samples = Array(repeating: 0.1, count: 16_000) // 1 s of audio
    let log = PracticeLog()
    rig.coordinator.destination = .practice { text, isFinal in log.events.append((text, isFinal)) }
    return (rig, log)
}

@MainActor @Test func practiceEmitsPartials() async {
    let (rig, log) = practiceRig(transcript: " partial ")
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.coordinator.tickLivePreview()
    await rig.coordinator.pendingPreview?.value
    #expect(log.texts == ["partial"])
    #expect(log.finals == [false])
    #expect(rig.transcriber.received.first?.count == 16_000)
    #expect(rig.model.phase == .recording)
}

@MainActor @Test func practiceWaitsForSpeechAndMinAudio() async {
    let (rig, log) = practiceRig()
    rig.coordinator.handle(.pressed)
    rig.coordinator.tickLivePreview() // silent so far
    rig.audio.onLevel?(0.001)          // step 2: still below the gate
    rig.coordinator.tickLivePreview()
    rig.speak()
    rig.audio.samples = Array(repeating: 0.1, count: 4_000) // 0.25 s
    rig.coordinator.tickLivePreview()
    await rig.coordinator.pendingPreview?.value
    #expect(rig.transcriber.received.isEmpty)
    #expect(log.events.isEmpty)
}

@MainActor @Test func practicePartialsRespectTheInterval() async {
    let (rig, log) = practiceRig()
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.coordinator.tickLivePreview()
    await rig.coordinator.pendingPreview?.value
    rig.clock.advance(0.3)
    rig.coordinator.tickLivePreview()
    await rig.coordinator.pendingPreview?.value
    #expect(log.events.count == 1)
    rig.clock.advance(0.4)
    rig.coordinator.tickLivePreview()
    await rig.coordinator.pendingPreview?.value
    #expect(log.events.count == 2)
}

@MainActor @Test func practiceSkipsEmptyPartials() async {
    let (rig, log) = practiceRig(transcript: "   ")
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.coordinator.tickLivePreview()
    await rig.coordinator.pendingPreview?.value
    #expect(rig.transcriber.received.count == 1)
    #expect(log.events.isEmpty)
}

@MainActor @Test func practiceFinalReplacesPartial() async {
    let (rig, log) = practiceRig(transcript: "par")
    rig.model.lastDictation = "earlier"
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.coordinator.tickLivePreview()
    await rig.coordinator.pendingPreview?.value
    rig.transcriber.result = " partial text "
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    await rig.coordinator.pendingWork?.value
    #expect(log.texts == ["par", "partial text"])
    #expect(log.finals == [false, true])
    #expect(rig.inserter.inserted.isEmpty)
    #expect(rig.model.lastDictation == "earlier")
    #expect(rig.model.phase == .idle)
}

@MainActor @Test func practiceNoOverlap() async {
    let rig = Rig()
    rig.audio.samples = Array(repeating: 0.1, count: 16_000)
    let gated = GatedTranscriber()
    let coordinator = AppCoordinator(
        audio: rig.audio, transcriber: gated, inserter: rig.inserter, model: rig.model,
        now: { [clock = rig.clock] in clock.time })
    let log = PracticeLog()
    coordinator.destination = .practice { text, isFinal in log.events.append((text, isFinal)) }
    coordinator.handle(.pressed)
    rig.speak()
    coordinator.tickLivePreview()
    await waitForCalls(gated, 1)
    rig.clock.advance(1)
    coordinator.tickLivePreview()
    rig.clock.advance(1)
    coordinator.tickLivePreview()
    for _ in 0..<20 { await Task.yield() }
    #expect(gated.callCount == 1)
    gated.finish(call: 0, with: "one")
    await coordinator.pendingPreview?.value
    #expect(log.texts == ["one"])
    // With the first pass done, the next tick may run again.
    rig.clock.advance(1)
    coordinator.tickLivePreview()
    await waitForCalls(gated, 2)
    gated.finish(call: 1, with: "two")
    await coordinator.pendingPreview?.value
    #expect(log.texts == ["one", "two"])
}

@MainActor @Test func practicePartialFinishingAfterReleaseIsDropped() async {
    let rig = Rig()
    rig.audio.samples = Array(repeating: 0.1, count: 16_000)
    let gated = GatedTranscriber()
    let coordinator = AppCoordinator(
        audio: rig.audio, transcriber: gated, inserter: rig.inserter, model: rig.model,
        now: { [clock = rig.clock] in clock.time })
    let log = PracticeLog()
    coordinator.destination = .practice { text, isFinal in log.events.append((text, isFinal)) }
    coordinator.handle(.pressed)
    rig.speak()
    coordinator.tickLivePreview()
    await waitForCalls(gated, 1)
    rig.clock.advance(1)
    coordinator.handle(.released)
    // The final pass waits for the partial (see practiceFinalWaitsForTheInFlightPartial).
    gated.finish(call: 0, with: "late partial")
    await coordinator.pendingPreview?.value
    await waitForCalls(gated, 2)
    gated.finish(call: 1, with: "final")
    await coordinator.pendingWork?.value
    #expect(log.texts == ["final"])
    #expect(log.finals == [true])
}

@MainActor @Test func practiceFinalWaitsForTheInFlightPartial() async {
    let rig = Rig()
    rig.audio.samples = Array(repeating: 0.1, count: 16_000)
    let gated = GatedTranscriber()
    let coordinator = AppCoordinator(
        audio: rig.audio, transcriber: gated, inserter: rig.inserter, model: rig.model,
        now: { [clock = rig.clock] in clock.time })
    let log = PracticeLog()
    coordinator.destination = .practice { text, isFinal in log.events.append((text, isFinal)) }
    coordinator.handle(.pressed)
    rig.speak()
    coordinator.tickLivePreview()
    await waitForCalls(gated, 1)
    rig.clock.advance(1)
    coordinator.handle(.released)
    // Never two transcriptions at once: the final must not start while the partial runs.
    for _ in 0..<50 { await Task.yield() }
    #expect(gated.callCount == 1)
    gated.finish(call: 0, with: "partial")
    await waitForCalls(gated, 2)
    gated.finish(call: 1, with: "final")
    await coordinator.pendingWork?.value
    #expect(gated.callCount == 2)
    #expect(log.texts == ["final"])
    #expect(rig.model.phase == .idle)
}

@MainActor @Test func practiceEmptyFinalReportsEmptyAndShowsError() async {
    let (rig, log) = practiceRig(transcript: "  ")
    rig.dictate()
    #expect(rig.model.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(log.texts == [""])
    #expect(log.finals == [true])
    #expect(rig.inserter.inserted.isEmpty)
    #expect(rig.model.phase == .idle)
}

@MainActor @Test func practiceSilentRecordingStillShowsErrorCue() async {
    let (rig, log) = practiceRig()
    rig.coordinator.handle(.pressed)
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    #expect(rig.model.phase == .error)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.isEmpty)
    #expect(log.events.isEmpty)
}

@MainActor @Test func resetToPasteAfterPractice() async {
    let (rig, log) = practiceRig(transcript: "hello")
    rig.coordinator.handle(.pressed)
    rig.speak()
    rig.coordinator.destination = .paste
    rig.coordinator.tickLivePreview() // no longer previews
    rig.clock.advance(1)
    rig.coordinator.handle(.released)
    await rig.coordinator.pendingWork?.value
    #expect(rig.inserter.inserted == ["hello "])
    #expect(rig.model.lastDictation == "hello")
    #expect(log.events.isEmpty)
    #expect(rig.transcriber.received.count == 1)
}

@MainActor @Test func practiceLoopTicksWhileRecordingAndStopsAfter() async {
    let (rig, log) = practiceRig(transcript: "live")
    rig.coordinator.handle(.pressed)
    rig.speak()
    // The real 0.1 s loop (not the manual hook) delivers a partial without any help.
    for _ in 0..<200 where log.events.isEmpty { try? await Task.sleep(for: .milliseconds(20)) }
    #expect(log.texts.first == "live")
    rig.coordinator.handle(.cancel)
    #expect(rig.coordinator.isPreviewLoopRunning == false)
}
