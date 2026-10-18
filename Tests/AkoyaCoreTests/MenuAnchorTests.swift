import AppKit
import Testing
@testable import AkoyaCore

/// While a dropdown menu is open, the main dispatch queue must keep draining: hotkey events,
/// audio levels, transcription results and device refreshes all arrive through it. NSMenu
/// tracking spins a nested run loop in event-tracking mode; that only services the main queue
/// if the menu was not popped up from inside a main-queue block.
@MainActor
@Test func mainQueueKeepsDrainingWhileAMenuTracks() async throws {
    // As in the app: AppKit makes event-tracking mode one of the run loop's common modes.
    _ = NSApplication.shared
    let anchor = MenuAnchor()
    var ranDuringTracking = false
    let trackingStub = PopUpMenuAction { _, _, _ in
        var ran = false
        DispatchQueue.main.async { ran = true }
        let deadline = Date().addingTimeInterval(1)
        // Stands in for NSMenu's tracking loop.
        while !ran, Date() < deadline {
            RunLoop.current.run(mode: .eventTracking, before: Date().addingTimeInterval(0.01))
        }
        ranDuringTracking = ran
    }

    anchor.present(NSMenu(), alignment: .leading, with: trackingStub)
    #expect(anchor.isOpen)
    let clock = ContinuousClock()
    let deadline = clock.now + .seconds(3)
    while anchor.isOpen, clock.now < deadline {
        try await Task.sleep(for: .milliseconds(20))
    }
    #expect(!anchor.isOpen)
    #expect(ranDuringTracking)
}
