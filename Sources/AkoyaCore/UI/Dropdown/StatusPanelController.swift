import AppKit
import SwiftUI

/// Shows SwiftUI content in a borderless, non-activating panel under the status item: opening
/// it never takes focus from the frontmost app. It closes on a click outside, on Esc while it
/// is key, and on a second click of the item.
///
/// The content is hosted only while the panel is shown, so a closed panel does no rendering
/// work while the waveform model changes during dictation.
@MainActor
public final class StatusPanelController {
    /// Gap between the menu bar item and the top of the panel.
    static let gap: CGFloat = 8
    private static let escapeKeyCode: UInt16 = 53

    private let content: AnyView
    private let panel = DropdownPanel()
    private var hostingView: NSView?
    private weak var anchorButton: NSStatusBarButton?
    private var contentSize: CGSize = .zero
    private var mouseMonitor: Any?
    private var keyMonitor: Any?

    public init(content: some View) {
        self.content = AnyView(content)
    }

    public var isShown: Bool { hostingView != nil }

    public func toggle(relativeTo button: NSStatusBarButton) {
        if isShown {
            close()
        } else {
            show(relativeTo: button)
        }
    }

    public func close() {
        guard isShown else { return }
        removeMonitors()
        panel.orderOut(nil)
        panel.contentView = nil
        hostingView = nil
        anchorButton?.highlight(false)
        anchorButton = nil
    }

    private func show(relativeTo button: NSStatusBarButton) {
        anchorButton = button
        let root = content
            .fixedSize()
            .onGeometryChange(for: CGSize.self, of: { $0.size }) { [weak self] size in
                self?.contentSizeChanged(to: size)
            }
        let hosting = FirstClickHostingView(rootView: root)
        // Measure with the default sizing options (with none, `fittingSize` is zero), then
        // drop them: the controller sizes the panel itself, without Auto Layout constraints.
        contentSize = hosting.fittingSize
        hosting.sizingOptions = []
        hostingView = hosting
        panel.contentView = hosting
        place()
        // Front without making it key or activating Akoya; a click inside makes it key.
        panel.orderFrontRegardless()
        // The window shadow follows the drawn alpha (the rounded panel), so draw first.
        panel.display()
        panel.invalidateShadow()
        button.highlight(true)
        installMonitors()
    }

    private func contentSizeChanged(to size: CGSize) {
        guard isShown, size != contentSize, size.width > 0, size.height > 0 else { return }
        contentSize = size
        place()
    }

    /// Right-aligns the panel to the item, `gap` below it, clamped to the screen's visible frame.
    private func place() {
        guard let button = anchorButton, let buttonWindow = button.window else { return }
        let anchor = buttonWindow.convertToScreen(button.convert(button.bounds, to: nil))
        let size = contentSize
        var origin = CGPoint(x: anchor.maxX - size.width, y: anchor.minY - Self.gap - size.height)
        if let visible = (buttonWindow.screen ?? NSScreen.main)?.visibleFrame {
            origin.x = min(max(origin.x, visible.minX), visible.maxX - size.width)
            origin.y = max(min(origin.y, visible.maxY - size.height), visible.minY)
        }
        panel.setFrame(CGRect(origin: origin, size: size), display: true)
        hostingView?.frame = CGRect(origin: .zero, size: size)
        panel.invalidateShadow()
    }

    private func installMonitors() {
        // Global monitors only see other apps' events, so any click they report is outside
        // the panel. Clicks on Akoya's own item arrive as its action and toggle instead.
        mouseMonitor = NSEvent.addGlobalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown]
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.close() }
        }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            let isEscape = event.keyCode == Self.escapeKeyCode
            let handled = MainActor.assumeIsolated { () -> Bool in
                guard isEscape, let self, self.panel.isKeyWindow else { return false }
                self.close()
                return true
            }
            return handled ? nil : event
        }
    }

    private func removeMonitors() {
        for monitor in [mouseMonitor, keyMonitor].compactMap({ $0 }) {
            NSEvent.removeMonitor(monitor)
        }
        mouseMonitor = nil
        keyMonitor = nil
    }
}

/// Borderless, non-activating, transparent floating panel at status bar level. It can become
/// key (so Esc reaches it after a click inside) but never main, and never activates the app.
private final class DropdownPanel: NSPanel {
    init() {
        super.init(
            contentRect: .zero, styleMask: [.nonactivatingPanel, .borderless], backing: .buffered, defer: true)
        isFloatingPanel = true
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}

/// Lets the first click on a control act right away, even before the panel is key.
private final class FirstClickHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}
