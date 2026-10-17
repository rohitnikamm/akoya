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

    init(transcript: String = "hello", mode: DictationMode = .hold,
         maxRecording: TimeInterval = 300, errorDisplay: TimeInterval = 0.01) {
        let transcriber = FakeTranscriber(result: transcript)
        let clock = self.clock
        self.transcriber = transcriber
        coordinator = AppCoordinator(
            audio: audio, transcriber: transcriber, inserter: inserter, model: model,
            mode: { mode }, now: { clock.time },
            maxRecording: maxRecording, errorDisplay: errorDisplay)
    }

    /// Press, speak (one loud level so the silence gate lets it through), wait past the
    /// minimum hold, release.
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

@MainActor @Test func shortTapCancels() async {
    let rig = Rig()
    rig.coordinator.handle(.pressed)
    rig.clock.advance(0.1)
    rig.coordinator.handle(.released)
    await rig.coordinator.pendingWork?.value
    #expect(rig.audio.cancelCount == 1)
    #expect(rig.audio.stopCount == 0)
    #expect(rig.transcriber.received.isEmpty)
    #expect(rig.coordinator.phase == .idle)
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

@MainActor @Test func toggleModeStartsAndStopsOnPress() async {
    let rig = Rig(mode: .toggle)
    rig.coordinator.handle(.pressed)
    #expect(rig.coordinator.phase == .recording)
    rig.coordinator.handle(.released)
    #expect(rig.coordinator.phase == .recording)
    rig.speak()
    rig.coordinator.handle(.pressed)
    #expect(rig.coordinator.phase == .transcribing)
    await rig.coordinator.pendingWork?.value
    #expect(rig.transcriber.received.count == 1)
    #expect(rig.inserter.inserted == ["hello "])
    #expect(rig.coordinator.phase == .idle)
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
        mode: { .hold }, now: { clock.time })
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
