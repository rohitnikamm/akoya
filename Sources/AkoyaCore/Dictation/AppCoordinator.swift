import Foundation

/// The dictation state machine: turns hotkey events into audio capture, transcription and
/// text insertion, and mirrors the current phase and waveform levels into `WaveformModel`.
///
/// Transitions: `idle → recording → transcribing → idle`, with `error` (shown briefly) as the
/// exit for a failed start, a silent recording, an empty transcript, or a transcribe/insert
/// failure. The phase stays `transcribing` until `insert` returns, so a new press cannot start
/// a recording while the clipboard is still being restored.
///
/// A screen lock or sleep (`screenLocked()`) cancels a recording, and keeps a transcription
/// in progress as the last dictation instead of pasting it.
///
/// The hotkey is hold-or-tap: releasing after `holdThreshold` stops (push-to-talk); releasing
/// sooner latches the recording on, and the next press stops it. Esc cancels either way.
///
/// In pill mode (`usesPill()` at the press) there is no `.error` phase: every dictation goes
/// straight back to `.idle` and reports exactly one `DictationOutcome`, which the pill shows.
@MainActor
public final class AppCoordinator {
    /// A recording whose loudest level step (see `LevelMeter.step(forRMS:)`) never rose above
    /// this is treated as silence and not transcribed: Parakeet invents text such as "Okay."
    /// for silent audio. Step 2 is roughly -44 to -39 dBFS.
    public nonisolated static let silenceMaxStep = 2

    /// Where a finished dictation goes. Onboarding sets `.practice` so the transcript lands in
    /// its card (live while speaking, then final on release) instead of being pasted.
    public enum Destination {
        case paste
        case practice(onText: @MainActor (String, _ isFinal: Bool) -> Void)
    }

    private let audio: AudioRecording
    private let transcriber: Transcribing
    private let inserter: TextInserting
    private let model: WaveformModel
    private let now: () -> TimeInterval
    private let maxRecording: TimeInterval
    private let holdThreshold: TimeInterval
    private let errorDisplay: TimeInterval

    private var meter = LevelMeter()
    private var pillMeter = LevelMeter(columnCount: PillDots.columns)
    /// Loudest raw level step seen in the current recording.
    private var loudestStep = 1
    private var recordingStartedAt: TimeInterval = 0
    /// True once a quick tap released the key: the recording keeps going until the next press.
    private var isLatched = false
    private var maxRecordingTimer: Task<Void, Never>?

    // Practice-mode live preview state.
    private var previewScheduler = LivePreviewScheduler()
    private var previewLoop: Task<Void, Never>?
    private var previewInFlight = false
    /// Bumped per recording so a late partial from an earlier one is ignored.
    private var recordingGeneration = 0
    private static let previewTickInterval: Duration = .milliseconds(100)

    /// Set when the screen locks or the Mac sleeps mid-transcription: the result is kept as the
    /// last dictation instead of being pasted into a screen the user cannot see.
    private var holdsResult = false

    /// `usesPill()` as read at the press that started the current dictation.
    private var isPillDictation = false

    /// The in-flight transcription/insert task (or error-cue task). Test hook.
    var pendingWork: Task<Void, Never>?

    /// The in-flight live-preview transcription. Test hook.
    var pendingPreview: Task<Void, Never>?
    var isPreviewLoopRunning: Bool { previewLoop != nil }

    /// Read when a recording stops, so switching back to `.paste` mid-recording pastes.
    public var destination: Destination = .paste

    /// Presses are ignored while this returns false (e.g. the model is still downloading).
    public var isReady: () -> Bool = { true }

    /// Read at each press: whether this dictation reports to the pill instead of the menu bar.
    public var usesPill: () -> Bool = { false }

    /// Whether the speech model failed to load, so a press shows that instead of "loading".
    public var isModelFailed: () -> Bool = { false }

    /// How each pill-mode dictation ended (never for practice).
    public var onOutcome: (DictationOutcome) -> Void = { _ in }

    public var phase: DictationPhase { model.phase }
    public var isRecording: Bool { model.phase == .recording }

    public init(audio: AudioRecording, transcriber: Transcribing, inserter: TextInserting,
                model: WaveformModel,
                now: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
                maxRecording: TimeInterval = 300, holdThreshold: TimeInterval = 0.30,
                errorDisplay: TimeInterval = 0.6) {
        self.audio = audio
        self.transcriber = transcriber
        self.inserter = inserter
        self.model = model
        self.now = now
        self.maxRecording = maxRecording
        self.holdThreshold = holdThreshold
        self.errorDisplay = errorDisplay

        audio.onLevel = { [weak self] rms in
            MainActor.assumeIsolated { self?.receiveLevel(rms) }
        }
        audio.onInterrupted = { [weak self] in
            MainActor.assumeIsolated { self?.receiveInterruption() }
        }
    }

    public func handle(_ event: HotkeyEvent) {
        switch (event, model.phase) {
        case (.pressed, .idle):
            isPillDictation = usesPill()
            guard isReady() else {
                fail(.message(isModelFailed() ? .modelFailed : .modelLoading))
                return
            }
            startRecording()
        case (.pressed, .recording):
            // A press while the key is already held (no release seen) is not a stop request.
            if isLatched { stopRecording() }
        case (.released, .recording):
            // A latched recording ignores releases. (The release that follows the stopping
            // press arrives after the phase left `.recording`, so it never reaches here.)
            guard !isLatched else { return }
            if now() - recordingStartedAt >= holdThreshold {
                stopRecording()
            } else {
                isLatched = true
            }
        case (.cancel, .recording):
            cancelRecording()
        default:
            break // presses while transcribing/error, stray releases and Esc are ignored
        }
    }

    /// The screen locked or the Mac is going to sleep. A recording is cancelled, as with Esc; a
    /// transcription still finishes, but its text goes to the last dictation rather than being
    /// pasted, and a failure ends quietly (nothing went wrong from the user's side).
    public func screenLocked() {
        switch model.phase {
        case .recording: cancelRecording()
        case .transcribing: holdsResult = true
        case .idle, .error: break
        }
    }

    // MARK: - Recording

    private func startRecording() {
        meter.reset()
        pillMeter.reset()
        loudestStep = 1
        model.columns = meter.columns
        model.pillColumns = pillMeter.columns
        do {
            try audio.start()
        } catch AudioCaptureError.microphoneNotAuthorized {
            fail(.message(.micBlocked))
            return
        } catch {
            fail(.message(.micMissing))
            return
        }
        isLatched = false
        recordingStartedAt = now()
        model.phase = .recording
        scheduleMaxRecordingStop()
        startPreviewLoop()
    }

    private func cancelRecording() {
        maxRecordingTimer?.cancel()
        endPreview()
        audio.cancel()
        model.voiceLevel = 0
        model.phase = .idle
        if isPillDictation { report(.dismissed, for: destination) }
    }

    private func stopRecording() {
        maxRecordingTimer?.cancel()
        endPreview()
        let samples = audio.stop()
        model.voiceLevel = 0
        let destination = destination
        guard loudestStep > Self.silenceMaxStep else {
            fail(.dismissed) // nothing was said; treat it like an empty transcript
            return
        }
        model.phase = .transcribing
        holdsResult = false
        let transcriber = transcriber
        let inserter = inserter
        let isPill = isPillDictation
        // A partial pass may still be running; never run two transcriptions at once.
        let partial = pendingPreview
        pendingWork = Task {
            do {
                await partial?.value
                let text = try await transcriber.transcribe(samples)
                let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
                switch destination {
                case .paste where holdsResult:
                    if !trimmed.isEmpty { model.lastDictation = trimmed }
                    if isPill { return report(.dismissed, for: destination) }
                case .paste:
                    guard !trimmed.isEmpty else { return await fail(.dismissed, isPill, destination) }
                    try await inserter.insert(trimmed + " ")
                    model.lastDictation = trimmed
                    if isPill { return report(.pasted, for: destination) }
                case .practice(let onText):
                    onText(trimmed, true)
                    guard !trimmed.isEmpty else { return await fail(.dismissed, isPill, destination) }
                }
                model.phase = .idle
            } catch {
                guard !holdsResult else {
                    model.phase = .idle
                    if isPill { report(.dismissed, for: destination) }
                    return
                }
                await fail(.dismissed, isPill, destination)
            }
        }
    }

    private func scheduleMaxRecordingStop() {
        maxRecordingTimer?.cancel()
        maxRecordingTimer = Task { [maxRecording] in
            try? await Task.sleep(for: .seconds(maxRecording))
            guard !Task.isCancelled, isRecording else { return }
            stopRecording()
        }
    }

    // MARK: - Live preview (practice mode)

    private func startPreviewLoop() {
        recordingGeneration += 1
        previewScheduler = LivePreviewScheduler()
        previewInFlight = false
        guard case .practice = destination else { return }
        previewLoop = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: Self.previewTickInterval)
                guard !Task.isCancelled, let self else { return }
                self.tickLivePreview()
            }
        }
    }

    private func endPreview() {
        previewLoop?.cancel()
        previewLoop = nil
    }

    /// One iteration of the live-preview loop: if the scheduler allows, re-transcribe the audio
    /// captured so far and report it as a partial. Does nothing outside practice mode.
    func tickLivePreview() {
        guard isRecording, case .practice = destination else { return }
        let samples = audio.snapshot()
        guard previewScheduler.shouldRun(
            now: now(), audioSeconds: Double(samples.count) / AudioCapture.sampleRate,
            loudestStep: loudestStep, inFlight: previewInFlight) else { return }
        previewInFlight = true
        let generation = recordingGeneration
        let transcriber = transcriber
        pendingPreview = Task {
            let text = (try? await transcriber.transcribe(samples)) ?? ""
            guard generation == recordingGeneration else { return }
            previewInFlight = false
            // The final pass owns the result once the key is released.
            guard isRecording, case .practice(let onText) = destination else { return }
            let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
            if !trimmed.isEmpty { onText(trimmed, false) }
        }
    }

    // MARK: - Audio callbacks

    private func receiveLevel(_ rms: Float) {
        guard isRecording else { return }
        let step = LevelMeter.step(forRMS: rms)
        loudestStep = max(loudestStep, step)
        model.voiceLevel = Double(step - 1) / 7
        meter.push(rms: rms)
        model.columns = meter.columns
        pillMeter.push(rms: rms)
        model.pillColumns = pillMeter.columns
    }

    private func receiveInterruption() {
        guard isRecording else { return }
        stopRecording()
    }

    // MARK: - Outcomes and error cue

    /// Ends a pill-mode dictation: straight back to `.idle`, then the outcome is reported.
    /// Practice reports nothing; its Try it card is the feedback.
    private func report(_ outcome: DictationOutcome, for destination: Destination) {
        model.phase = .idle
        if case .practice = destination { return }
        onOutcome(outcome)
    }

    /// A dictation that ended without text: the pill's outcome, or the menu bar's error cue.
    private func fail(_ outcome: DictationOutcome) {
        if isPillDictation {
            report(outcome, for: destination)
        } else {
            showError()
        }
    }

    /// `fail` inside the transcription task, with the indicator and destination it captured.
    private func fail(_ outcome: DictationOutcome, _ isPill: Bool,
                      _ destination: Destination) async {
        if isPill {
            report(outcome, for: destination)
        } else {
            await flashError()
        }
    }

    /// Shows `.error` for `errorDisplay` seconds, then returns to `.idle`.
    private func flashError() async {
        model.phase = .error
        try? await Task.sleep(for: .seconds(errorDisplay))
        model.phase = .idle
    }

    /// Error cue for a failure that has no surrounding work task (a failed audio start or a
    /// silent recording).
    private func showError() {
        model.phase = .error
        pendingWork = Task { await flashError() }
    }
}
