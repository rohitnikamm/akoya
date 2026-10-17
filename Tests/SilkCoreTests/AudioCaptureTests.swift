import AVFoundation
import Testing
@testable import SilkCore

@MainActor
@Test func configurationChangeWhileRecordingFiresInterrupted() {
    let capture = AudioCapture(testingRecording: true)
    var interruptions = 0
    capture.onInterrupted = { interruptions += 1 }
    capture.handleConfigurationChange()
    #expect(interruptions == 1)
    // A second notification after the interruption must not fire again.
    capture.handleConfigurationChange()
    #expect(interruptions == 1)
}

@MainActor
@Test func configurationChangeWhileIdleIsSilent() {
    let capture = AudioCapture()
    var interruptions = 0
    capture.onInterrupted = { interruptions += 1 }
    capture.handleConfigurationChange()
    #expect(interruptions == 0)
}

@MainActor
@Test func stopAfterInterruptionReturnsEmptyWhenNothingCaptured() {
    let capture = AudioCapture(testingRecording: true)
    capture.handleConfigurationChange()
    #expect(capture.stop().isEmpty)
}

@MainActor
@Test func startThrowsWhenMicrophoneDenied() {
    var requests = 0
    let capture = AudioCapture(authorizationStatus: { .denied }, requestAccess: { requests += 1 })
    #expect(throws: AudioCaptureError.self) { try capture.start() }
    #expect(requests == 0)
}

@MainActor
@Test func startThrowsWhenMicrophoneRestricted() {
    let capture = AudioCapture(authorizationStatus: { .restricted }, requestAccess: {})
    #expect(throws: AudioCaptureError.self) { try capture.start() }
}

@MainActor
@Test func startRequestsAccessOnceWhenNotDetermined() {
    var requests = 0
    let capture = AudioCapture(authorizationStatus: { .notDetermined }, requestAccess: { requests += 1 })
    #expect(throws: AudioCaptureError.self) { try capture.start() }
    #expect(requests == 1)
}

@MainActor
@Test func authorizedMicrophonePassesTheCheck() throws {
    var requests = 0
    let capture = AudioCapture(authorizationStatus: { .authorized }, requestAccess: { requests += 1 })
    try capture.ensureMicrophoneAuthorized()
    #expect(requests == 0)
}

@Test func convertDownsamplesStereo48kToMono16k() throws {
    let source = try #require(AVAudioFormat(
        commonFormat: .pcmFormatFloat32, sampleRate: 48_000, channels: 2, interleaved: false))
    let target = try #require(AVAudioFormat(
        commonFormat: .pcmFormatFloat32, sampleRate: 16_000, channels: 1, interleaved: false))
    let converter = try #require(AVAudioConverter(from: source, to: target))
    let input = try #require(AVAudioPCMBuffer(pcmFormat: source, frameCapacity: 4800))
    input.frameLength = 4800
    for channel in 0..<2 {
        let data = try #require(input.floatChannelData?[channel])
        for i in 0..<4800 { data[i] = 0.5 }
    }

    var total = 0
    for _ in 0..<20 { total += AudioCapture.convert(input, with: converter, to: target).count }
    // 20 x 4800 input frames at 48 kHz -> about 32000 frames at 16 kHz (within 1%: the
    // converter holds back a little latency).
    #expect(abs(total - 32_000) < 320)

    let steady = AudioCapture.convert(input, with: converter, to: target)
    #expect(!steady.isEmpty)
    let mean = steady.reduce(0, +) / Float(steady.count)
    #expect(mean > 0.1 && mean < 1.0)
}
