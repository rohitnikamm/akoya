import AVFoundation
import Foundation
import Testing
@testable import SilkCore

/// Collects `ModelState` callbacks from `prepare`, which may arrive on any thread.
private final class StateRecorder: @unchecked Sendable {
    private let lock = NSLock()
    private var recorded: [ModelState] = []

    func record(_ state: ModelState) {
        lock.lock()
        defer { lock.unlock() }
        recorded.append(state)
    }

    var states: [ModelState] {
        lock.lock()
        defer { lock.unlock() }
        return recorded
    }
}

/// Speaks `phrase` with the system synthesizer and returns 16 kHz mono float samples.
private func synthesizeSamples(_ phrase: String) throws -> [Float] {
    let wav = FileManager.default.temporaryDirectory
        .appendingPathComponent("silk-parakeet-\(UUID().uuidString).wav")
    defer { try? FileManager.default.removeItem(at: wav) }

    let say = Process()
    say.executableURL = URL(fileURLWithPath: "/usr/bin/say")
    say.arguments = ["-o", wav.path, "--data-format=LEF32@16000", phrase]
    try say.run()
    say.waitUntilExit()
    try #require(say.terminationStatus == 0)

    let file = try AVAudioFile(forReading: wav)
    let format = file.processingFormat
    try #require(format.sampleRate == 16_000 && format.channelCount == 1)
    let buffer = try #require(
        AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(file.length)))
    try file.read(into: buffer)
    let channel = try #require(buffer.floatChannelData?[0])
    return Array(UnsafeBufferPointer(start: channel, count: Int(buffer.frameLength)))
}

@Test func transcribeBeforePrepareThrows() async throws {
    // A throwaway directory proves nothing is downloaded or written by transcribe.
    let directory = FileManager.default.temporaryDirectory
        .appendingPathComponent("silk-parakeet-unprepared-\(UUID().uuidString)")
    let transcriber = ParakeetTranscriber(modelsDirectory: directory)
    await #expect(throws: ParakeetTranscriberError.notPrepared) {
        _ = try await transcriber.transcribe([0, 0, 0])
    }
    #expect(!FileManager.default.fileExists(atPath: directory.path))
}

@Test(.enabled(if: ProcessInfo.processInfo.environment["SILK_MODEL_TESTS"] == "1"))
func transcribesSpokenPhrase() async throws {
    let samples = try synthesizeSamples("hello world")
    let transcriber = ParakeetTranscriber()
    let recorder = StateRecorder()
    await transcriber.prepare { recorder.record($0) }

    let states = recorder.states
    #expect(states.last == .ready, "prepare ended in \(String(describing: states.last))")
    if case .downloading = states.first {} else if states.first != .ready {
        Issue.record("first state should be .downloading (or .ready if cached), got \(String(describing: states.first))")
    }

    let result = try await transcriber.transcribe(samples)
    #expect(result.lowercased().contains("hello"), "got: \(result)")
    #expect(result.lowercased().contains("world"), "got: \(result)")
}
