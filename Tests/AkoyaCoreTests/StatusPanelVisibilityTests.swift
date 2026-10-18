import AppKit
import SwiftUI
import Testing
@testable import AkoyaCore

/// Drives `StatusPanelController` with a plain `NSButton` standing in for the status item's
/// button, and records `onVisibilityChange`, which the app uses for the menu bar highlight.
private struct StubContent: View {
    var body: some View { Color.clear.frame(width: 120, height: 40) }
}

@MainActor
private struct Harness {
    let panel = StatusPanelController(content: StubContent())
    let button = NSButton(frame: NSRect(x: 0, y: 0, width: 30, height: 22))
    let events = EventLog()

    init() {
        _ = NSApplication.shared
        panel.onVisibilityChange = { [events] in events.values.append($0) }
    }
}

@MainActor
private final class EventLog {
    var values: [Bool] = []
}

/// Every panel test shares one serialized suite: the Space-change test posts a workspace
/// notification that closes every shown panel, so panel tests must not run side by side.
@MainActor
@Suite(.serialized) struct StatusPanelTests {
    @Test func visibilityChangeFiresTrueOnShowAndFalseOnClose() {
        let h = Harness()
        h.panel.toggle(relativeTo: h.button)
        #expect(h.panel.isShown)
        #expect(h.events.values == [true])

        h.panel.close()
        #expect(!h.panel.isShown)
        #expect(h.events.values == [true, false])
    }

    @Test func secondToggleClosesWithoutReopening() {
        let h = Harness()
        h.panel.toggle(relativeTo: h.button)
        h.panel.toggle(relativeTo: h.button)
        #expect(!h.panel.isShown)
        #expect(h.events.values == [true, false])
    }

    @Test func closingAnAlreadyClosedPanelReportsNothing() {
        let h = Harness()
        h.panel.close()
        #expect(h.events.values.isEmpty)
        h.panel.toggle(relativeTo: h.button)
        h.panel.close()
        h.panel.close()
        #expect(h.events.values == [true, false])
    }

    @Test func spaceChangeClosesAndReports() async {
        let h = Harness()
        h.panel.toggle(relativeTo: h.button)
        NSWorkspace.shared.notificationCenter.post(name: NSWorkspace.activeSpaceDidChangeNotification, object: nil)
        // The observer is delivered on the main queue; let it run.
        try? await Task.sleep(for: .milliseconds(100))
        #expect(!h.panel.isShown)
        #expect(h.events.values == [true, false])
    }

    @Test func reopenAfterCloseReportsAgain() {
        let h = Harness()
        h.panel.toggle(relativeTo: h.button)
        h.panel.close()
        h.panel.toggle(relativeTo: h.button)
        #expect(h.events.values == [true, false, true])
        h.panel.close()
    }

    /// Reopening the app opens the panel; a second reopen leaves it open (it never toggles).
    @Test func openShowsThePanelAndLeavesAnOpenPanelAlone() {
        let h = Harness()
        h.panel.open(relativeTo: h.button)
        h.panel.open(relativeTo: h.button)
        #expect(h.panel.isShown)
        #expect(h.events.values == [true])
        h.panel.close()
    }
}
