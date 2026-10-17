import Foundation

/// The dictation state machine: turns hotkey events into audio capture, transcription and
/// text insertion, and mirrors the current phase and waveform levels into `WaveformModel`.
///
/// Transitions: `idle → recording → transcribing → idle`, with `error` (shown briefly) as the
/// exit for a failed start, a silent recording, an empty transcript, or a transcribe/insert
/// failure. The phase stays `transcribing` until `insert` returns, so a new press cannot start
/// a recording while the clipboard is still being restored.
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
    private let mode: () -> DictationMode
    private let now: () -> TimeInterval
    private let maxRecording: TimeInterval
    private let minHold: TimeInterval
    private let errorDisplay: TimeInterval

    private var meter = LevelMeter()
    /// Loudest raw level step seen in the current recording.
    private var loudestStep = 1
    private var recordingStartedAt: TimeInterval = 0
    private var recordingMode: DictationMode = .hold
    private var maxRecordingTimer: Task<Void, Never>?

    // Practice-mode live preview state.
    private var previewScheduler = LivePreviewScheduler()
    private var previewLoop: Task<Void, Never>?
    private var previewInFlight = false
    /// Bumped per recording so a late partial from an earlier one is ignored.
    private var recordingGeneration = 0
    private static let previewTickInterval: Duration = .milliseconds(100)

    /// The in-flight transcription/insert task (or error-cue task). Test hook.
    var pendingWork: Task<Void, Never>?

    /// The in-flight live-preview transcription. Test hook.
    var pendingPreview: Task<Void, Never>?
    var isPreviewLoopRunning: Bool { previewLoop != nil }

    /// Read when a recording stops, so switching back to `.paste` mid-recording pastes.
    public var destination: Destination = .paste

    /// Presses are ignored while this returns false (e.g. the model is still downloading).
    public var isReady: () -> Bool = { true }

    public var phase: DictationPhase { model.phase }
    public var isRecording: Bool { model.phase == .recording }

    public init(audio: AudioRecording, transcriber: Transcribing, inserter: TextInserting,
                model: WaveformModel, mode: @escaping () -> DictationMode,
                now: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
                maxRecording: TimeInterval = 300, minHold: TimeInterval = 0.25,
                errorDisplay: TimeInterval = 0.6) {
        self.audio = audio
        self.transcriber = transcriber
        self.inserter = inserter
        self.model = model
        self.mode = mode
        self.now = now
        self.maxRecording = maxRecording
        self.minHold = minHold
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
            guard isReady() else {
                showError()
                return
            }
            startRecording()
        case (.pressed, .recording):
            if recordingMode == .toggle { stopRecording() }
        case (.released, .recording):
            guard recordingMode == .hold else { return }
            if now() - recordingStartedAt < minHold {
                cancelRecording()
            } else {
                stopRecording()
            }
        case (.cancel, .recording):
            cancelRecording()
        default:
            break // presses while transcribing/error, stray releases and Esc are ignored
        }
    }

    // MARK: - Recording

    private func startRecording() {
        meter.reset()
        loudestStep = 1
        model.columns = meter.columns
        do {
            try audio.start()
        } catch {
            showError()
            return
        }
        recordingMode = mode()
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
    }

    private func stopRecording() {
        maxRecordingTimer?.cancel()
        endPreview()
        let samples = audio.stop()
        model.voiceLevel = 0
        let destination = destination
        guard loudestStep > Self.silenceMaxStep else {
            showError() // nothing was said; treat it like an empty transcript
            return
        }
        model.phase = .transcribing
        let transcriber = transcriber
        let inserter = inserter
        pendingWork = Task {
            do {
                let text = try await transcriber.transcribe(samples)
                let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
                switch destination {
                case .paste:
                    guard !trimmed.isEmpty else { return await flashError() }
                    try await inserter.insert(trimmed + " ")
                    model.lastDictation = trimmed
                case .practice(let onText):
                    onText(trimmed, true)
                    guard !trimmed.isEmpty else { return await flashError() }
                }
                model.phase = .idle
            } catch {
                await flashError()
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
    }

    private func receiveInterruption() {
        guard isRecording else { return }
        stopRecording()
    }

    // MARK: - Error cue

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
