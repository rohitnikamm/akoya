import AppKit
import Testing
@testable import AkoyaCore

@MainActor
private func makeItem() -> (container: NSView, hosting: StatusItemHostingView, presses: () -> Int) {
    _ = NSApplication.shared
    final class Counter { var n = 0 }
    let counter = Counter()
    let container = NSView(frame: NSRect(x: 0, y: 0, width: 28, height: 24))
    let hosting = StatusItemHostingView(model: WaveformModel()) { counter.n += 1 }
    hosting.frame = container.bounds
    container.addSubview(hosting)
    return (container, hosting, { counter.n })
}

@MainActor
@Test func hostingViewTakesHitsInsideItsBoundsOnly() {
    let item = makeItem()
    #expect(item.container.hitTest(NSPoint(x: 14, y: 12)) === item.hosting)
    #expect(item.container.hitTest(NSPoint(x: 40, y: 12)) == nil)
}

@MainActor
@Test func hostingViewPressesOnMouseDownAndAcceptsFirstMouse() {
    let item = makeItem()
    let down = NSEvent.mouseEvent(
        with: .leftMouseDown, location: .zero, modifierFlags: [], timestamp: 0, windowNumber: 0,
        context: nil, eventNumber: 0, clickCount: 1, pressure: 1)
    item.hosting.mouseDown(with: down!)
    let right = NSEvent.mouseEvent(
        with: .rightMouseDown, location: .zero, modifierFlags: [], timestamp: 0, windowNumber: 0,
        context: nil, eventNumber: 0, clickCount: 1, pressure: 1)
    item.hosting.rightMouseDown(with: right!)
    #expect(item.presses() == 2)
    #expect(item.hosting.acceptsFirstMouse(for: nil))
}

@MainActor
@Test func hostingViewExposesAccessibilityPress() {
    let item = makeItem()
    #expect(item.hosting.isAccessibilityElement())
    #expect(item.hosting.accessibilityRole() == .button)
    #expect(item.hosting.accessibilityLabel() == "Akoya")
    #expect(item.hosting.accessibilityPerformPress())
    #expect(item.presses() == 1)
}
