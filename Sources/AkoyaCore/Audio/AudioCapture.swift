@preconcurrency import AVFoundation
import CoreAudio
import Foundation
import os

public enum AudioCaptureError: Error, LocalizedError {
    case microphoneNotAuthorized
    case noInputDevice
    case converterUnavailable
    case engineFailedToStart(String)

    public var errorDescription: String? {
        switch self {
        case .microphoneNotAuthorized:
            return "Microphone access has not been granted. Allow Akoya in System Settings > Privacy & Security > Microphone."
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

    /// Copies the samples captured so far, leaving them in place for the final `take()`.
    func snapshot() -> [Float] {
        lock.lock()
        defer { lock.unlock() }
        return samples
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

/// Records the chosen (or default) microphone as 16 kHz mono Float32 and reports live RMS levels.
/// `onLevel` and `onInterrupted` are always invoked on the main actor.
@MainActor
public final class AudioCapture: AudioRecording {
    nonisolated static let sampleRate: Double = 16_000

    public var onLevel: ((Float) -> Void)?
    public var onInterrupted: (() -> Void)?
    /// UID of the microphone to record from, read at each `start()`. nil = system default.
    public var preferredDeviceUID: () -> String? = { nil }

    private static let log = Logger(subsystem: "com.rohitnikamm.akoya", category: "audio")

    private var engine: AVAudioEngine?
    private let buffer = CaptureBuffer()
    private var configObserver: NSObjectProtocol?
    private var tapInstalled = false
    private var isRecording: Bool
    private let authorizationStatus: () -> AVAuthorizationStatus
    private let requestAccess: () -> Void
    private let listInputs: () -> [AudioInputDevice]
    private let defaultInputID: () -> UInt32?
    private let applyDevice: (AVAudioEngine, AudioInputDevice) -> Bool

    public convenience init(
        authorizationStatus: @escaping () -> AVAuthorizationStatus = {
            AVCaptureDevice.authorizationStatus(for: .audio)
        },
        requestAccess: @escaping () -> Void = {
            AVCaptureDevice.requestAccess(for: .audio) { _ in }
        }
    ) {
        self.init(
            authorizationStatus: authorizationStatus, requestAccess: requestAccess,
            listInputs: AudioDevices.inputs, defaultInputID: AudioDevices.defaultInputID,
            applyDevice: Self.setInputDevice)
    }

    /// Fully injectable initializer, so tests never touch audio hardware.
    init(
        authorizationStatus: @escaping () -> AVAuthorizationStatus,
        requestAccess: @escaping () -> Void,
        listInputs: @escaping () -> [AudioInputDevice],
        defaultInputID: @escaping () -> UInt32?,
        applyDevice: @escaping (AVAudioEngine, AudioInputDevice) -> Bool
    ) {
        self.authorizationStatus = authorizationStatus
        self.requestAccess = requestAccess
        self.listInputs = listInputs
        self.defaultInputID = defaultInputID
        self.applyDevice = applyDevice
        isRecording = false
    }

    /// Test seam: sets the recording flag without touching the audio engine.
    init(testingRecording: Bool) {
        authorizationStatus = { AVCaptureDevice.authorizationStatus(for: .audio) }
        requestAccess = {}
        listInputs = AudioDevices.inputs
        defaultInputID = AudioDevices.defaultInputID
        applyDevice = Self.setInputDevice
        isRecording = testingRecording
    }

    /// Throws unless the microphone is authorized. When the user has not been asked yet, this
    /// triggers the system prompt (fire-and-forget) and still throws, so the press that caused
    /// it flashes the error cue rather than recording silence.
    func ensureMicrophoneAuthorized() throws {
        switch authorizationStatus() {
        case .authorized:
            return
        case .notDetermined:
            requestAccess()
            throw AudioCaptureError.microphoneNotAuthorized
        default:
            throw AudioCaptureError.microphoneNotAuthorized
        }
    }

    public func start() throws {
        guard !isRecording else { return }
        try ensureMicrophoneAuthorized()
        discardSamples()

        let engine = AVAudioEngine()
        // Must happen before the input format is read or the tap installed.
        _ = applyPreferredDevice(to: engine)
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

    /// Points the engine at the user's chosen microphone. Does nothing (and returns true) when no
    /// microphone is chosen or the chosen one is not connected, so the engine uses the system
    /// default. Returns false when switching failed; recording then continues on the default.
    func applyPreferredDevice(to engine: AVAudioEngine) -> Bool {
        guard let uid = preferredDeviceUID() else { return true }
        let choice = MicrophoneResolver.resolve(
            selectedUID: uid, inputs: listInputs(), defaultID: defaultInputID())
        guard !choice.isFallback, let device = choice.device else {
            Self.log.notice("Selected microphone is not connected; using the system default")
            return true
        }
        guard applyDevice(engine, device) else {
            Self.log.error("Could not switch to \(device.name, privacy: .public); using the system default")
            return false
        }
        return true
    }

    /// Sets the engine's input node to a specific CoreAudio device.
    private nonisolated static func setInputDevice(_ engine: AVAudioEngine, _ device: AudioInputDevice) -> Bool {
        guard let unit = engine.inputNode.audioUnit else { return false }
        var deviceID = device.id
        let status = AudioUnitSetProperty(
            unit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0,
            &deviceID, UInt32(MemoryLayout<AudioDeviceID>.size))
        return status == noErr
    }

    public func stop() -> [Float] {
        teardownEngine()
        isRecording = false
        return buffer.take()
    }

    /// The samples captured so far, copied without clearing them (used by the live preview).
    public func snapshot() -> [Float] {
        buffer.snapshot()
    }

    /// Test seam: appends samples as the audio tap would, without an engine.
    func appendSamplesForTesting(_ samples: [Float]) {
        _ = buffer.append(samples)
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
