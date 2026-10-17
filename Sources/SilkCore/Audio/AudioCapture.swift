@preconcurrency import AVFoundation
import Foundation

public enum AudioCaptureError: Error, LocalizedError {
    case noInputDevice
    case converterUnavailable
    case engineFailedToStart(String)

    public var errorDescription: String? {
        switch self {
        case .noInputDevice:
            return "No microphone input is available (the input device reports 0 channels or a 0 Hz sample rate)."
        case .converterUnavailable:
            return "Could not create an audio converter from the microphone format to 16 kHz mono."
        case .engineFailedToStart(let reason):
            return "The audio engine failed to start: \(reason)"
        }
    }
}

/// Samples captured so far plus the RMS windower, shared between the realtime audio thread
/// (which appends) and the main actor (which drains). All access goes through the lock.
private final class CaptureBuffer: @unchecked Sendable {
    private let lock = NSLock()
    private var samples: [Float] = []
    private var windower = RMSWindower(sampleRate: AudioCapture.sampleRate, window: 0.04)

    /// Appends converted samples and returns any RMS values completed by them.
    func append(_ chunk: [Float]) -> [Float] {
        lock.lock()
        defer { lock.unlock() }
        samples.append(contentsOf: chunk)
        return windower.append(chunk)
    }

    func take() -> [Float] {
        lock.lock()
        defer { lock.unlock() }
        let taken = samples
        samples = []
        windower = RMSWindower(sampleRate: AudioCapture.sampleRate, window: 0.04)
        return taken
    }
}

/// Records the default microphone as 16 kHz mono Float32 and reports live RMS levels.
/// `onLevel` and `onInterrupted` are always invoked on the main actor.
@MainActor
public final class AudioCapture: AudioRecording {
    nonisolated static let sampleRate: Double = 16_000

    public var onLevel: ((Float) -> Void)?
    public var onInterrupted: (() -> Void)?

    private var engine: AVAudioEngine?
    private let buffer = CaptureBuffer()
    private var configObserver: NSObjectProtocol?
    private var tapInstalled = false
    private var isRecording: Bool

    public init() {
        isRecording = false
    }

    /// Test seam: sets the recording flag without touching the audio engine.
    init(testingRecording: Bool) {
        isRecording = testingRecording
    }

    public func start() throws {
        guard !isRecording else { return }
        discardSamples()

        let engine = AVAudioEngine()
        let input = engine.inputNode
        let hardwareFormat = input.outputFormat(forBus: 0)
        guard hardwareFormat.channelCount > 0, hardwareFormat.sampleRate > 0 else {
            throw AudioCaptureError.noInputDevice
        }
        guard let targetFormat = AVAudioFormat(
            commonFormat: .pcmFormatFloat32, sampleRate: Self.sampleRate, channels: 1, interleaved: false),
            let converter = AVAudioConverter(from: hardwareFormat, to: targetFormat)
        else {
            throw AudioCaptureError.converterUnavailable
        }

        input.installTap(
            onBus: 0, bufferSize: 1024, format: hardwareFormat,
            block: Self.makeTap(converter: converter, target: targetFormat, buffer: buffer) { [weak self] levels in
                // Realtime thread: hop to main, then drop the levels if the session ended.
                DispatchQueue.main.async {
                    MainActor.assumeIsolated {
                        guard let self, self.isRecording else { return }
                        for level in levels { self.onLevel?(level) }
                    }
                }
            })
        tapInstalled = true
        self.engine = engine

        do {
            try engine.start()
        } catch {
            teardownEngine()
            throw AudioCaptureError.engineFailedToStart(error.localizedDescription)
        }

        // The notification may be posted on any thread; deliver it on the main queue.
        configObserver = NotificationCenter.default.addObserver(
            forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.handleConfigurationChange() }
        }
        isRecording = true
    }

    public func stop() -> [Float] {
        teardownEngine()
        isRecording = false
        return buffer.take()
    }

    public func cancel() {
        teardownEngine()
        isRecording = false
        discardSamples()
    }

    /// The audio route changed under us (device unplugged, default input switched, ...).
    /// Stops the engine but keeps the captured samples for the caller's subsequent `stop()`.
    func handleConfigurationChange() {
        guard isRecording else { return }
        teardownEngine()
        isRecording = false
        onInterrupted?()
    }

    private func discardSamples() {
        _ = buffer.take()
    }

    private func teardownEngine() {
        if let configObserver {
            NotificationCenter.default.removeObserver(configObserver)
            self.configObserver = nil
        }
        if let engine {
            if tapInstalled { engine.inputNode.removeTap(onBus: 0) }
            engine.stop()
        }
        tapInstalled = false
        engine = nil
    }

    /// Builds the tap block in a nonisolated context so it carries no main-actor isolation
    /// (Swift 6 would otherwise assert when the realtime thread runs it).
    private nonisolated static func makeTap(
        converter: AVAudioConverter,
        target: AVAudioFormat,
        buffer: CaptureBuffer,
        publish: @escaping @Sendable ([Float]) -> Void
    ) -> AVAudioNodeTapBlock {
        return { input, _ in
            let samples = convert(input, with: converter, to: target)
            guard !samples.isEmpty else { return }
            let levels = buffer.append(samples)
            if !levels.isEmpty { publish(levels) }
        }
    }

    nonisolated static func convert(
        _ input: AVAudioPCMBuffer, with converter: AVAudioConverter, to target: AVAudioFormat
    ) -> [Float] {
        let ratio = target.sampleRate / input.format.sampleRate
        let capacity = AVAudioFrameCount((Double(input.frameLength) * ratio).rounded(.up)) + 32
        guard let output = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: capacity) else { return [] }

        var supplied = false
        var error: NSError?
        let status = converter.convert(to: output, error: &error) { _, outStatus in
            if supplied {
                outStatus.pointee = .noDataNow
                return nil
            }
            supplied = true
            outStatus.pointee = .haveData
            return input
        }
        guard status != .error, error == nil,
            let channel = output.floatChannelData?[0], output.frameLength > 0
        else { return [] }
        return Array(UnsafeBufferPointer(start: channel, count: Int(output.frameLength)))
    }
}
