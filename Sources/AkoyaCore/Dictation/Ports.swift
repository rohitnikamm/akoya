import Foundation

public enum DictationPhase: Equatable, Sendable { case idle, recording, transcribing, error }
public enum HotkeyEvent: Equatable, Sendable { case pressed, released, cancel }
public enum DictationMode: String, CaseIterable, Sendable { case hold, toggle }
public enum WaveLook: String, CaseIterable, Sendable { case pixel, solid }

@MainActor public protocol AudioRecording: AnyObject {
    var onLevel: ((Float) -> Void)? { get set }      // RMS 0…1, ~25 Hz, main actor
    var onInterrupted: (() -> Void)? { get set }      // device change mid-recording
    func start() throws
    func stop() -> [Float]                            // 16 kHz mono samples
    func snapshot() -> [Float]                        // samples so far, without clearing them
    func cancel()
}
public protocol Transcribing: Sendable { func transcribe(_ samples: [Float]) async throws -> String }
@MainActor public protocol TextInserting { func insert(_ text: String) async throws }
