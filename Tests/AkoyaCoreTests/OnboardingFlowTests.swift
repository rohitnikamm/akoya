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

@Test func launchStartRules() {
    let none = grants([])
    let some = grants([.microphone])
    let all = grants([.microphone, .accessibility, .inputMonitoring])
    // First launch with nothing granted: the whole flow.
    #expect(OnboardingFlow.launchStart(completed: false, granted: none) == .welcome)
    // Relaunch mid-onboarding with something already granted: skip the welcome.
    #expect(OnboardingFlow.launchStart(completed: false, granted: some) == .permission(.accessibility))
    #expect(OnboardingFlow.launchStart(completed: false, granted: grants([.accessibility])) == .permission(.microphone))
    #expect(OnboardingFlow.launchStart(completed: false, granted: all) == .tryIt)
    // Completed: only a missing permission brings it back.
    #expect(OnboardingFlow.launchStart(completed: true, granted: all) == nil)
    #expect(OnboardingFlow.launchStart(completed: true, granted: some) == .permission(.accessibility))
    #expect(OnboardingFlow.launchStart(completed: true, granted: none) == .permission(.microphone))
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
    let failedLine = ModelState.failed("x").onboardingStatusLine
    #expect(failedLine != nil)
    // Retry is a button now, not a pointer at the menu bar.
    #expect(failedLine?.contains("menu bar") == false)
}

@Test func onboardingRetryOnlyWhenTheModelFailed() {
    #expect(ModelState.failed("x").onboardingShowsRetry)
    for state in [ModelState.ready, .notReady, .downloading(nil), .downloading(0.3)] {
        #expect(!state.onboardingShowsRetry)
    }
}

// MARK: - Practice destination and closing

@MainActor
private final class RigAudio: AudioRecording {
    var onLevel: ((Float) -> Void)?
    var onInterrupted: (() -> Void)?
    private(set) var cancelCount = 0
    func start() throws {}
    func stop() -> [Float] { [0.1, 0.2, 0.3] }
    func snapshot() -> [Float] { [] }
    func cancel() { cancelCount += 1 }
}

private struct WordsTranscriber: Transcribing {
    func transcribe(_ samples: [Float]) async throws -> String { "practice words" }
}

@MainActor
private final class RecordingInserter: TextInserting {
    private(set) var inserted: [String] = []
    func insert(_ text: String) async throws { inserted.append(text) }
}

@MainActor
private final class OnboardingRig {
    let model = WaveformModel()
    let settings = AkoyaSettings(defaults: UserDefaults(suiteName: "akoya-onboarding-test-\(UUID())")!)
    let audio = RigAudio()
    let inserter = RecordingInserter()
    let coordinator: AppCoordinator
    private(set) var finishedHaptics = 0
    private(set) var controller: OnboardingWindowController!

    /// Toggle mode lets a test stop a recording without waiting out the minimum hold.
    init(mode: DictationMode = .hold) {
        coordinator = AppCoordinator(
            audio: audio, transcriber: WordsTranscriber(), inserter: inserter, model: model,
            mode: { mode }, errorDisplay: 0.01)
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

@MainActor @Test func closingMidRecordingCancelsInsteadOfPasting() async {
    for exit in [OnboardingExit.closed, .skipped, .done] {
        let rig = OnboardingRig(mode: .toggle)
        rig.coordinator.destination = .practice { _, _ in }
        rig.coordinator.handle(.pressed)
        rig.audio.onLevel?(1.0)  // speech, so the silence gate would let it through
        #expect(rig.coordinator.phase == .recording)
        rig.controller.finish(exit)
        #expect(rig.coordinator.phase == .idle)
        #expect(rig.audio.cancelCount == 1)
        // The next tap starts a fresh recording rather than transcribing the practice speech.
        rig.coordinator.handle(.pressed)
        #expect(rig.coordinator.phase == .recording)
        rig.coordinator.handle(.cancel)
        await rig.coordinator.pendingWork?.value
        #expect(rig.inserter.inserted.isEmpty)
    }
}

@MainActor @Test func closingWhilePracticeTranscribesNeverPastesTheFinal() async {
    final class Received { var finals: [String] = [] }
    let received = Received()
    let rig = OnboardingRig(mode: .toggle)
    rig.coordinator.destination = .practice { text, isFinal in if isFinal { received.finals.append(text) } }
    rig.coordinator.handle(.pressed)
    rig.audio.onLevel?(1.0)
    rig.coordinator.handle(.pressed)  // toggle stop: the final pass starts
    #expect(rig.coordinator.phase == .transcribing)
    rig.controller.close()
    await rig.coordinator.pendingWork?.value
    // The coordinator captured the practice destination when the recording stopped.
    #expect(rig.inserter.inserted.isEmpty)
    #expect(received.finals == ["practice words"])
    #expect(rig.isPasting)
}

// MARK: - View model: close, retry, reaching Try it

@MainActor
private func viewModel(
    start: OnboardingStep, granted: Set<Permission> = [], retry: @escaping () -> Void = {},
    reachedTryIt: @escaping () -> Void = {}, rig: OnboardingRig
) -> OnboardingViewModel {
    OnboardingViewModel(
        start: start, modelState: .ready,
        tryIt: TryItSession(coordinator: rig.coordinator, model: rig.model),
        isGranted: { granted.contains($0) }, microphoneStatus: { .authorized },
        grantedHaptic: {}, retryModel: retry, reachedTryIt: reachedTryIt)
}

@MainActor @Test func closeCountsAsSkipOnTryItOrWhenEverythingIsGranted() {
    let rig = OnboardingRig()
    let all: Set<Permission> = [.microphone, .accessibility, .inputMonitoring]
    #expect(viewModel(start: .tryIt, granted: all, rig: rig).closeExit == .skipped)
    // Try it is the last step, so it counts even if a grant has since been revoked.
    #expect(viewModel(start: .tryIt, granted: [], rig: rig).closeExit == .skipped)
    #expect(viewModel(start: .welcome, granted: all, rig: rig).closeExit == .skipped)
    // Mid-flow with a permission missing: not complete.
    #expect(viewModel(start: .welcome, granted: [], rig: rig).closeExit == .closed)
    #expect(viewModel(start: .permission(.accessibility), granted: [.microphone], rig: rig).closeExit == .closed)
}

@MainActor @Test func closingAsSkipCompletesOnboarding() {
    let rig = OnboardingRig()
    rig.controller.finish(viewModel(start: .tryIt, rig: rig).closeExit)
    #expect(rig.settings.onboardingCompleted)
    let other = OnboardingRig()
    other.controller.finish(viewModel(start: .permission(.microphone), rig: other).closeExit)
    #expect(!other.settings.onboardingCompleted)
}

@MainActor @Test func retryModelCallsTheClosure() {
    final class Count { var value = 0 }
    let count = Count()
    let rig = OnboardingRig()
    let model = viewModel(start: .permission(.microphone), retry: { count.value += 1 }, rig: rig)
    model.retryModel()
    model.retryModel()
    #expect(count.value == 2)
}

@MainActor @Test func reachingTryItBringsTheWindowForwardOnce() {
    final class Grants { var on: Set<Permission> = [.microphone, .accessibility]; var fronted = 0 }
    let state = Grants()
    let rig = OnboardingRig()
    let model = OnboardingViewModel(
        start: .permission(.inputMonitoring), modelState: .ready,
        tryIt: TryItSession(coordinator: rig.coordinator, model: rig.model),
        isGranted: { state.on.contains($0) }, microphoneStatus: { .authorized },
        grantedHaptic: {}, reachedTryIt: { state.fronted += 1 })
    model.poll()
    #expect(state.fronted == 0)
    state.on.insert(.inputMonitoring)
    model.poll()
    #expect(model.flow.step == .tryIt)
    #expect(state.fronted == 1)
    model.poll()
    #expect(state.fronted == 1)
}

@MainActor @Test func advancingToAnotherPermissionDoesNotBringTheWindowForward() {
    final class Grants { var on: Set<Permission> = []; var fronted = 0 }
    let state = Grants()
    let rig = OnboardingRig()
    let model = OnboardingViewModel(
        start: .permission(.microphone), modelState: .ready,
        tryIt: TryItSession(coordinator: rig.coordinator, model: rig.model),
        isGranted: { state.on.contains($0) }, microphoneStatus: { .authorized },
        grantedHaptic: {}, reachedTryIt: { state.fronted += 1 })
    state.on.insert(.microphone)
    model.poll()
    #expect(model.flow.step == .permission(.accessibility))
    #expect(state.fronted == 0)
}

@MainActor @Test func tryItBeginDropsAStaleRetryHint() {
    let rig = OnboardingRig()
    let session = TryItSession(coordinator: rig.coordinator, model: rig.model)
    // A missed take in paste mode (the subscription listens whatever the destination).
    rig.model.phase = .recording
    rig.model.phase = .error
    #expect(session.transcript.showsRetryHint)
    session.begin()
    #expect(!session.transcript.showsRetryHint)
    #expect(session.transcript.text.isEmpty)
    #expect(!session.transcript.hasFinal)
}
