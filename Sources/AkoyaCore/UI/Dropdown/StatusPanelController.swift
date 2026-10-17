import AppKit
import SwiftUI

/// Shows SwiftUI content in a borderless, non-activating panel under the status item. The
/// panel never becomes key, so the frontmost app keeps keyboard focus (and dictated ⌘V keeps
/// landing there) even after a click inside. It closes on a click outside (in another app or
/// another Akoya window), on Esc, on a Space change, and on a second click of the item.
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
    private weak var anchorButton: NSButton?
    private var contentSize: CGSize = .zero
    /// Event monitors and the Space observer, installed in `show` and removed in `close`.
    private var monitors: [Any] = []
    private var spaceObserver: NSObjectProtocol?

    /// Called with `true` once the panel is shown and `false` once it is closed, on every path
    /// (outside click, Esc, second click of the item, Space change, `close()` from an action).
    /// The owner drives the menu bar item's selected look from this, so the two cannot drift.
    public var onVisibilityChange: ((Bool) -> Void)?

    public init(content: some View) {
        self.content = AnyView(content)
    }

    public var isShown: Bool { hostingView != nil }

    public func toggle(relativeTo button: NSButton) {
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
        anchorButton = nil
        onVisibilityChange?(false)
    }

    private func show(relativeTo button: NSButton) {
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
        // Front without making it key or activating Akoya.
        panel.orderFrontRegardless()
        // The window shadow follows the drawn alpha (the rounded panel), so draw first.
        panel.display()
        panel.invalidateShadow()
        installMonitors()
        onVisibilityChange?(true)
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
        // the panel. Clicks on Akoya's own item arrive as its action (on mouse-down) and toggle
        // instead. The monitors are installed after the opening mouse-down was dispatched, so
        // that event never reaches them.
        if let monitor = NSEvent.addGlobalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown],
            handler: { [weak self] _ in MainActor.assumeIsolated { self?.close() } })
        {
            monitors.append(monitor)
        }
        // The panel is never key, so Esc goes to the frontmost app; watch for it globally
        // (Akoya already has the Accessibility access this needs). The app sees Esc too.
        if let monitor = NSEvent.addGlobalMonitorForEvents(
            matching: .keyDown,
            handler: { [weak self] event in
                guard event.keyCode == Self.escapeKeyCode else { return }
                MainActor.assumeIsolated { self?.close() }
            })
        {
            monitors.append(monitor)
        }
        // Clicks in another Akoya window (Settings, onboarding). Clicks on the item itself are
        // left to its action, which fires on mouse-down and toggles; this monitor runs before
        // that dispatch, so closing here would make the action reopen the panel.
        if let monitor = NSEvent.addLocalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown],
            handler: { [weak self] event in
                let window = event.window
                MainActor.assumeIsolated {
                    guard let self, let window, window !== self.panel,
                          window !== self.anchorButton?.window
                    else { return }
                    self.close()
                }
                return event
            })
        {
            monitors.append(monitor)
        }
        spaceObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.close() }
        }
    }

    private func removeMonitors() {
        monitors.forEach(NSEvent.removeMonitor)
        monitors = []
        if let spaceObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(spaceObserver)
            self.spaceObserver = nil
        }
    }
}

/// Borderless, non-activating, transparent floating panel at status bar level. It never
/// becomes key or main: if it did, a click inside would leave it key, and dictation's ⌘V and
/// the user's typing would go to the panel instead of the frontmost app.
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

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// Lets clicks on controls act although the panel is never key.
private final class FirstClickHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}
