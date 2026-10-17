import Testing
@testable import AkoyaCore

@Suite struct StatusCardStateTests {
    @Test func recordingIsListening() {
        let state = StatusCardState.from(phase: .recording, model: .ready, permissionsGranted: true)
        #expect(state == .listening)
        #expect(state.chipTitle == "Listening")
    }

    @Test func transcribing() {
        let state = StatusCardState.from(phase: .transcribing, model: .ready, permissionsGranted: true)
        #expect(state == .transcribing)
        #expect(state.chipTitle == "Transcribing")
    }

    @Test func idleReady() {
        let state = StatusCardState.from(phase: .idle, model: .ready, permissionsGranted: true)
        #expect(state == .ready)
        #expect(state.chipTitle == "Ready")
    }

    @Test func downloadingShowsPercent() {
        let state = StatusCardState.from(phase: .idle, model: .downloading(0.42), permissionsGranted: true)
        #expect(state == .downloading(0.42))
        #expect(state.chipTitle == "Downloading 42%")
    }

    @Test func downloadingUnknown() {
        let state = StatusCardState.from(phase: .idle, model: .downloading(nil), permissionsGranted: true)
        #expect(state == .downloading(nil))
        #expect(state.chipTitle == "Downloading…")
        // Before the first progress report the model is still being prepared.
        #expect(StatusCardState.from(phase: .idle, model: .notReady, permissionsGranted: true) == .downloading(nil))
    }

    @Test func failed() {
        let state = StatusCardState.from(phase: .idle, model: .failed("offline"), permissionsGranted: true)
        #expect(state == .modelFailed)
        #expect(state.chipTitle == "Model failed")
    }

    @Test func setupNeededWhenIdle() {
        let state = StatusCardState.from(phase: .idle, model: .ready, permissionsGranted: false)
        #expect(state == .setupNeeded)
        #expect(state.chipTitle == "Setup needed")
        // Setup beats model problems and progress too.
        #expect(StatusCardState.from(phase: .idle, model: .failed("x"), permissionsGranted: false) == .setupNeeded)
        #expect(StatusCardState.from(phase: .idle, model: .downloading(0.5), permissionsGranted: false) == .setupNeeded)
    }

    @Test func recordingBeatsSetupNeeded() {
        #expect(StatusCardState.from(phase: .recording, model: .ready, permissionsGranted: false) == .listening)
        #expect(StatusCardState.from(phase: .transcribing, model: .ready, permissionsGranted: false) == .transcribing)
    }

    @Test func errorPhaseShowsReady() {
        #expect(StatusCardState.from(phase: .error, model: .ready, permissionsGranted: true) == .ready)
    }
}
