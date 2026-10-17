import Foundation
import Testing
@testable import AkoyaCore

// MARK: - Step logic

/// Grant state for a test: everything in `granted` is on, the rest off.
private func grants(_ granted: Set<Permission>) -> (Permission) -> Bool {
    { granted.contains($0) }
}

@Test func welcomeGoesToFirstMissing() {
    var flow = OnboardingFlow()
    #expect(flow.step == .welcome)
    flow.next(granted: grants([.microphone]))
    #expect(flow.step == .permission(.accessibility))
}

@Test func allGrantedSkipsToTryIt() {
    var flow = OnboardingFlow()
    flow.next(granted: grants([.microphone, .accessibility, .inputMonitoring]))
    #expect(flow.step == .tryIt)
}

@Test func refreshAdvancesAndReports() {
    var flow = OnboardingFlow(start: .permission(.microphone))
    // Microphone turned on; Accessibility was already on, so Input Monitoring is next.
    let advanced1 = flow.refresh(granted: grants([.microphone, .accessibility]))
    #expect(advanced1)
    #expect(flow.step == .permission(.inputMonitoring))
    let advanced2 = flow.refresh(granted: grants([.microphone, .accessibility, .inputMonitoring]))
    #expect(advanced2)
    #expect(flow.step == .tryIt)
}

@Test func refreshNoChangeReturnsFalse() {
    var flow = OnboardingFlow(start: .permission(.accessibility))
    let advanced3 = flow.refresh(granted: grants([.microphone]))
    #expect(!advanced3)
    #expect(flow.step == .permission(.accessibility))
    // Off a permission step there is nothing to advance.
    var welcome = OnboardingFlow()
    let advanced4 = welcome.refresh(granted: grants([.microphone, .accessibility, .inputMonitoring]))
    #expect(!advanced4)
    #expect(welcome.step == .welcome)
    var tryIt = OnboardingFlow(start: .tryIt)
    let advanced5 = tryIt.refresh(granted: grants([]))
    #expect(!advanced5)
    #expect(tryIt.step == .tryIt)
}

@Test func orderIsMicAxInput() {
    var flow = OnboardingFlow()
    flow.next(granted: grants([]))
    #expect(flow.step == .permission(.microphone))
    let advanced6 = flow.refresh(granted: grants([.microphone]))
    #expect(advanced6)
    #expect(flow.step == .permission(.accessibility))
    let advanced7 = flow.refresh(granted: grants([.microphone, .accessibility]))
    #expect(advanced7)
    #expect(flow.step == .permission(.inputMonitoring))
    #expect(OnboardingFlow.firstIncomplete(granted: grants([.accessibility])) == .permission(.microphone))
    #expect(OnboardingFlow.firstIncomplete(granted: grants([.microphone, .inputMonitoring])) == .permission(.accessibility))
}

@Test func firstIncompleteForReopen() {
    // "Finish setup" reopens at the first missing permission, or the practice step when none is.
    #expect(OnboardingFlow.firstIncomplete(granted: grants([.microphone, .accessibility])) == .permission(.inputMonitoring))
    #expect(OnboardingFlow.firstIncomplete(granted: grants([.microphone, .accessibility, .inputMonitoring])) == .tryIt)
    let reopened = OnboardingFlow(start: OnboardingFlow.firstIncomplete(granted: grants([.microphone])))
    #expect(reopened.step == .permission(.accessibility))
}

// MARK: - Try it transcript

@Test func tryItShowsPartialsThenFinal() {
    var transcript = TryItTranscript()
    transcript.phaseChanged(to: .recording)
    transcript.receive("Okay, this", isFinal: false)
    #expect(transcript.text == "Okay, this")
    #expect(!transcript.hasFinal)
    transcript.phaseChanged(to: .transcribing)
    transcript.receive("Okay, this is smooth.", isFinal: true)
    transcript.phaseChanged(to: .idle)
    #expect(transcript.text == "Okay, this is smooth.")
    #expect(transcript.hasFinal)
    #expect(!transcript.showsRetryHint)
}

@Test func tryItErrorAfterRecordingClearsPartialAndHints() {
    var transcript = TryItTranscript()
    transcript.phaseChanged(to: .recording)
    transcript.receive("Okay, this", isFinal: false)
    transcript.phaseChanged(to: .transcribing)
    // The final pass threw: the coordinator flashes the error cue without calling onText.
    transcript.phaseChanged(to: .error)
    #expect(transcript.text.isEmpty)
    #expect(transcript.showsRetryHint)
    #expect(!transcript.hasFinal)
    // The next recording hides the hint again.
    transcript.phaseChanged(to: .idle)
    transcript.phaseChanged(to: .recording)
    #expect(!transcript.showsRetryHint)
}

@Test func tryItEmptyFinalDoesNotEnableDone() {
    var transcript = TryItTranscript()
    transcript.phaseChanged(to: .recording)
    transcript.phaseChanged(to: .transcribing)
    transcript.receive("", isFinal: true)
    transcript.phaseChanged(to: .error)
    #expect(!transcript.hasFinal)
    #expect(transcript.showsRetryHint)
}

@Test func tryItErrorWithoutRecordingIsNotAMissedTake() {
    // A press while the model is still loading flashes the error cue without recording.
    var transcript = TryItTranscript()
    transcript.phaseChanged(to: .error)
    #expect(!transcript.showsRetryHint)
}

@Test func tryItDoneStaysPrimaryAfterALaterMiss() {
    var transcript = TryItTranscript()
    transcript.phaseChanged(to: .recording)
    transcript.receive("Hello there.", isFinal: true)
    transcript.phaseChanged(to: .idle)
    transcript.phaseChanged(to: .recording)
    transcript.phaseChanged(to: .error)
    #expect(transcript.hasFinal)
    #expect(transcript.text.isEmpty)
}

@Test func tryItWaveformDotsAreOddAndFollowTheLevel() {
    // Level steps 1…8 map to 1…9 dots (the most that fit the 70 pt row), always odd so each
    // column is centred.
    #expect(TryItWaveform.dotCount(step: 1) == 1)
    #expect(TryItWaveform.dotCount(step: 8) == 9)
    for step in 1...8 {
        #expect(TryItWaveform.dotCount(step: step) % 2 == 1)
    }
    #expect(TryItWaveform.dotCount(step: 5) > TryItWaveform.dotCount(step: 3))
}

@Test func downloadLineFollowsModelState() {
    #expect(ModelState.ready.onboardingStatusLine == nil)
    #expect(ModelState.notReady.onboardingStatusLine == "Getting the speech model ready…")
    #expect(ModelState.downloading(nil).onboardingStatusLine == "Getting the speech model ready…")
    #expect(ModelState.downloading(0.42).onboardingStatusLine == "Getting the speech model ready… 42%")
    #expect(ModelState.failed("x").onboardingStatusLine != nil)
}

// MARK: - Practice destination and closing

@MainActor
private final class SilentAudio: AudioRecording {
    var onLevel: ((Float) -> Void)?
    var onInterrupted: (() -> Void)?
    func start() throws {}
    func stop() -> [Float] { [] }
    func snapshot() -> [Float] { [] }
    func cancel() {}
}

private struct NoTranscriber: Transcribing {
    func transcribe(_ samples: [Float]) async throws -> String { "" }
}

private struct NoInserter: TextInserting {
    func insert(_ text: String) async throws {}
}

@MainActor
private final class OnboardingRig {
    let model = WaveformModel()
    let settings = AkoyaSettings(defaults: UserDefaults(suiteName: "akoya-onboarding-test-\(UUID())")!)
    let coordinator: AppCoordinator
    private(set) var finishedHaptics = 0
    private(set) var controller: OnboardingWindowController!

    init() {
        coordinator = AppCoordinator(
            audio: SilentAudio(), transcriber: NoTranscriber(), inserter: NoInserter(), model: model,
            mode: { .hold })
        controller = OnboardingWindowController(
            coordinator: coordinator, model: model, settings: settings, hotkeyName: { "Right ⌥" },
            finishedHaptic: { [unowned self] in self.finishedHaptics += 1 })
    }

    var isPasting: Bool {
        if case .paste = coordinator.destination { return true }
        return false
    }
}

@MainActor @Test func tryItSessionRoutesPracticeTextIntoTheCard() {
    let rig = OnboardingRig()
    let session = TryItSession(coordinator: rig.coordinator, model: rig.model)
    session.begin()
    guard case .practice(let onText) = rig.coordinator.destination else {
        Issue.record("destination is not practice")
        return
    }
    rig.model.phase = .recording
    onText("Okay, this", false)
    #expect(session.transcript.text == "Okay, this")
    // An error after the recording clears the partial (the model publishes the phase).
    rig.model.phase = .error
    #expect(session.transcript.text.isEmpty)
    #expect(session.transcript.showsRetryHint)
}

@MainActor @Test func doneResetsDestinationCompletesAndTaps() {
    let rig = OnboardingRig()
    rig.coordinator.destination = .practice { _, _ in }
    rig.controller.finish(.done)
    #expect(rig.isPasting)
    #expect(rig.settings.onboardingCompleted)
    #expect(rig.finishedHaptics == 1)
}

@MainActor @Test func skipResetsDestinationAndCompletesWithoutTap() {
    let rig = OnboardingRig()
    rig.coordinator.destination = .practice { _, _ in }
    rig.controller.finish(.skipped)
    #expect(rig.isPasting)
    #expect(rig.settings.onboardingCompleted)
    #expect(rig.finishedHaptics == 0)
}

@MainActor @Test func closeResetsDestinationWithoutCompleting() {
    let rig = OnboardingRig()
    rig.coordinator.destination = .practice { _, _ in }
    // Used by the × button and on app quit.
    rig.controller.close()
    #expect(rig.isPasting)
    #expect(!rig.settings.onboardingCompleted)
    #expect(rig.finishedHaptics == 0)
}

@MainActor @Test func pollAdvancesAndTapsOncePerGrant() {
    final class Grants { var on: Set<Permission> = [.microphone]; var taps = 0 }
    let state = Grants()
    let rig = OnboardingRig()
    let viewModel = OnboardingViewModel(
        start: .permission(.accessibility), modelState: .ready,
        tryIt: TryItSession(coordinator: rig.coordinator, model: rig.model),
        isGranted: { state.on.contains($0) }, microphoneStatus: { .authorized },
        grantedHaptic: { state.taps += 1 })
    viewModel.poll()
    #expect(viewModel.flow.step == .permission(.accessibility))
    #expect(state.taps == 0)
    state.on.insert(.accessibility)
    viewModel.poll()
    #expect(viewModel.flow.step == .permission(.inputMonitoring))
    #expect(viewModel.isGranted(.accessibility))
    #expect(state.taps == 1)
    viewModel.poll()
    #expect(state.taps == 1)
}
