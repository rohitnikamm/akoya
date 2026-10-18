import AppKit
import SwiftUI
import Testing
@testable import AkoyaCore

/// The panel follows its content's height (the Menu bar icon options expanding and
/// collapsing): it grows at once, shrinks only after `shrinkDelay` so a collapse animation
/// can play inside it, and keeps its top edge anchored under the item throughout.
@MainActor
private final class ContentHeight: ObservableObject {
    @Published var value: CGFloat = 40
}

private struct ResizableContent: View {
    @ObservedObject var height: ContentHeight
    var body: some View { Color.gray.frame(width: 120, height: height.value) }
}

@MainActor
private func waitUntil(timeout: Duration = .seconds(2), _ condition: () -> Bool) async throws {
    let clock = ContinuousClock()
    let deadline = clock.now + timeout
    while !condition(), clock.now < deadline {
        try await Task.sleep(for: .milliseconds(20))
    }
}

extension StatusPanelTests {
    @Test func panelGrowsAtOnceAndShrinksAfterTheDelayKeepingItsTop() async throws {
        _ = NSApplication.shared
        let height = ContentHeight()
        let panel = StatusPanelController(content: ResizableContent(height: height))
        let window = NSWindow(
            contentRect: NSRect(x: 400, y: 400, width: 30, height: 22), styleMask: .borderless, backing: .buffered,
            defer: false)
        let button = NSButton(frame: NSRect(x: 0, y: 0, width: 30, height: 22))
        window.contentView = button
        panel.toggle(relativeTo: button)
        defer { panel.close() }

        #expect(panel.frame.size == CGSize(width: 120, height: 40))
        let top = panel.frame.maxY

        height.value = 100
        try await waitUntil { panel.frame.height == 100 }
        #expect(panel.frame.height == 100)
        #expect(panel.frame.maxY == top)

        height.value = 50
        try await Task.sleep(for: .milliseconds(100))
        #expect(panel.frame.height == 100, "still the old height while the collapse animates")
        try await waitUntil { panel.frame.height == 50 }
        #expect(panel.frame.height == 50)
        #expect(panel.frame.maxY == top)
    }
}
