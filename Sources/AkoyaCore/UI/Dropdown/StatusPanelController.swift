import AppKit
import SwiftUI

/// Shows SwiftUI content in a borderless, non-activating panel under the status item. The
/// panel never becomes key, so the frontmost app keeps keyboard focus (and dictated ⌘V keeps
/// landing there) even after a click inside. It closes on a click outside (in another app or
/// another Akoya window), on Esc, on a Space change, and on a second click of the item.
/// Native menus popped up from the content (`PopUpMenuAction`) track without activating Akoya.
///
/// The content is hosted only while the panel is shown, so a closed panel does no rendering
/// work while the waveform model changes during dictation.
@MainActor
public final class StatusPanelController {
    /// Gap between the menu bar item and the top of the panel.
    static let gap: CGFloat = 8
    /// Gap between a control and the menu popped up under it.
    static let menuGap: CGFloat = 4
    /// How long a shrinking panel keeps its old height, so a collapse animation (the Menu bar
    /// icon options) plays out inside the window instead of being cut off by it.
    static let shrinkDelay: TimeInterval = 0.4
    private static let escapeKeyCode: UInt16 = 53

    private let content: AnyView
    private let panel = DropdownPanel()
    private var hostingView: NSView?
    private weak var anchorButton: NSButton?
    /// The panel's current size (the content's size, or larger while a shrink is pending).
    private var contentSize: CGSize = .zero
    private var pendingShrink: DispatchWorkItem?
    /// Event monitors and the Space observer, installed in `show` and removed in `close`.
    private var monitors: [Any] = []
    private var spaceObserver: NSObjectProtocol?

    /// Called with `true` once the panel is shown and `false` once it is closed, on every path
    /// (outside click, Esc, second click of the item, Space change, `close()` from an action).
    public var onVisibilityChange: ((Bool) -> Void)?

    public init(content: some View) {
        self.content = AnyView(content)
    }

    public var isShown: Bool { hostingView != nil }

    /// The panel's frame on screen (for tests).
    var frame: CGRect { panel.frame }

    public func toggle(relativeTo button: NSButton) {
        if isShown {
            close()
        } else {
            show(relativeTo: button)
        }
    }

    /// Shows the panel under `button` unless it is already shown.
    public func open(relativeTo button: NSButton) {
        guard !isShown else { return }
        show(relativeTo: button)
    }

    public func close() {
        guard isShown else { return }
        pendingShrink?.cancel()
        pendingShrink = nil
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
            .environment(\.popUpMenu, PopUpMenuAction { [weak self] menu, rect, alignment in
                self?.popUp(menu, below: rect, alignment: alignment)
            })
            .fixedSize()
            .onGeometryChange(for: CGSize.self, of: { $0.size }) { [weak self] size in
                self?.contentSizeChanged(to: size)
            }
            // Pinned to the top while a shrink is pending and the window is still taller.
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
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

    /// Grows at once; shrinks after `shrinkDelay`, so collapsing content animates inside the
    /// window. The top stays anchored under the item either way.
    private func contentSizeChanged(to size: CGSize) {
        guard isShown, size.width > 0, size.height > 0 else { return }
        pendingShrink?.cancel()
        pendingShrink = nil
        guard size != contentSize else { return }
        if size.height < contentSize.height {
            let work = DispatchWorkItem { [weak self] in
                MainActor.assumeIsolated { self?.resize(to: size) }
            }
            pendingShrink = work
            DispatchQueue.main.asyncAfter(deadline: .now() + Self.shrinkDelay, execute: work)
        } else {
            resize(to: size)
        }
    }

    private func resize(to size: CGSize) {
        pendingShrink = nil
        guard isShown else { return }
        contentSize = size
        place()
    }

    /// Pops `menu` up under `rect` (hosting view coordinates), leading- or trailing-aligned.
    /// Menu tracking runs in its own loop and returns when the menu closes; it neither
    /// activates Akoya nor makes the panel key.
    private func popUp(_ menu: NSMenu, below rect: CGRect, alignment: PopUpMenuAction.Alignment) {
        guard let hostingView else { return }
        menu.appearance = NSAppearance(named: .darkAqua)
        let x = alignment == .leading ? rect.minX : rect.maxX - menu.size.width
        let y = hostingView.isFlipped
            ? rect.maxY + Self.menuGap : hostingView.bounds.height - rect.maxY - Self.menuGap
        menu.popUp(positioning: nil, at: CGPoint(x: x, y: y), in: hostingView)
    }

    /// Right-aligns the panel to the item, `gap` below it, clamped to the screen's visible frame.
    /// An item that is on no screen (a 0-width item while the idle icon is hidden can be) puts
    /// the panel at the top right of the main screen instead.
    private func place() {
        guard let button = anchorButton, let buttonWindow = button.window else { return }
        let anchor = buttonWindow.convertToScreen(button.convert(button.bounds, to: nil))
        let size = contentSize
        var origin = CGPoint(x: anchor.maxX - size.width, y: anchor.minY - Self.gap - size.height)
        if buttonWindow.screen == nil, let visible = NSScreen.main?.visibleFrame {
            origin = CGPoint(x: visible.maxX - Self.gap - size.width, y: visible.maxY - Self.gap - size.height)
        }
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
        // the panel. Clicks on Akoya's own item arrive as its hosting view's mouse-down and toggle
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
        // When Akoya itself is active (reopened from Spotlight), Esc comes to Akoya, which
        // global monitors never see.
        if let monitor = NSEvent.addLocalMonitorForEvents(
            matching: .keyDown,
            handler: { [weak self] event in
                if event.keyCode == Self.escapeKeyCode {
                    MainActor.assumeIsolated { self?.close() }
                }
                return event
            })
        {
            monitors.append(monitor)
        }
        // Clicks in another Akoya window (onboarding). Clicks on the item itself are left to
        // its hosting view, which toggles on mouse-down; this monitor runs before that
        // dispatch, so closing here would make the toggle reopen the panel. Clicks in a menu
        // popped up from the panel (menu windows sit at pop-up menu level) keep it open.
        if let monitor = NSEvent.addLocalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown],
            handler: { [weak self] event in
                let window = event.window
                MainActor.assumeIsolated {
                    guard let self, let window, window !== self.panel,
                          window !== self.anchorButton?.window,
                          window.level.rawValue < NSWindow.Level.popUpMenu.rawValue
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
        // The dropdown is dark only; its native menus follow the panel.
        appearance = NSAppearance(named: .darkAqua)
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// Lets clicks on controls act although the panel is never key.
private final class FirstClickHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}
