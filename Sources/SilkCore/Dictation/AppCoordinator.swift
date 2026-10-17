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
    public static let silenceMaxStep = 2

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

    /// The in-flight transcription/insert task (or error-cue task). Test hook.
    var pendingWork: Task<Void, Never>?

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
    }

    private func cancelRecording() {
        maxRecordingTimer?.cancel()
        audio.cancel()
        model.phase = .idle
    }

    private func stopRecording() {
        maxRecordingTimer?.cancel()
        let samples = audio.stop()
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
                guard !trimmed.isEmpty else { return await flashError() }
                try await inserter.insert(trimmed + " ")
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

    // MARK: - Audio callbacks

    private func receiveLevel(_ rms: Float) {
        guard isRecording else { return }
        loudestStep = max(loudestStep, LevelMeter.step(forRMS: rms))
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
