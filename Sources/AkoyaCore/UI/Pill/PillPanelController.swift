import AppKit
import Combine
import SwiftUI

/// Shows `PillPresenter`'s content in a floating panel above the Dock on the screen being worked on.
/// It pops in each time the pill appears, and fades, sinks and shrinks away when the content goes; content
/// returning mid-hide rises back. Never takes focus or clicks, so dictation pastes into the frontmost app.
@MainActor
public final class PillPanelController {
    /// Room around the pill for its glow, points (the CSS glow reaches 52 pt below it).
    static let glowMargin: CGFloat = 60
    /// Wide enough for the longest message.
    static let panelWidth: CGFloat = 640
    /// The pop-in with Reduce Motion on: a plain fade.
    static let reduceMotionFade: TimeInterval = 0.28
    /// The hide's easing, CSS `cubic-bezier(0.2, 0.8, 0.2, 1)`.
    static let hideAnimation = Animation.timingCurve(0.2, 0.8, 0.2, 1, duration: PillHide.duration)

    private let panel = PillPanel()
    private let display = PillDisplay()
    private var subscriptions: Set<AnyCancellable> = []
    /// Bumped by every show and hide, so a stale order-out or pop-in end does nothing.
    private var generation = 0

    public init(presenter: PillPresenter, model: WaveformModel) {
        let size = CGSize(width: Self.panelWidth, height: PillView.height + 2 * Self.glowMargin)
        panel.setContentSize(size)
        let host = NSHostingView(rootView: PillView(display: display, model: model))
        host.frame = CGRect(origin: .zero, size: size)
        panel.contentView = host

        // `@Published` emits in willSet, so use the delivered values.
        model.$phase
            .sink { [weak self] phase in self?.phaseChanged(phase) }
            .store(in: &subscriptions)
        presenter.$content
            .sink { [weak self] content in self?.contentChanged(content) }
            .store(in: &subscriptions)
    }

    private func phaseChanged(_ phase: DictationPhase) {
        switch phase {
        case .recording:
            display.recordingStartedAt = Date()
            display.recordingEndedAt = nil
        case .transcribing:
            display.recordingEndedAt = Date()
        case .idle, .error:
            break
        }
    }

    private func contentChanged(_ content: PillContent?) {
        guard let content else { return hide() }
        if display.isShown {
            // Showing already: the pill resizes to the new content.
            withAnimation(PillView.resize) { display.content = content }
            return
        }
        display.content = content
        generation += 1
        if panel.isVisible {
            // Still hiding: rise back from wherever the hide got to.
            withAnimation(Self.hideAnimation) { display.isShown = true }
            return
        }
        place()
        display.reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        display.isShown = true
        display.popInStartedAt = Date()
        panel.orderFrontRegardless()
        let mine = generation
        let duration = display.reduceMotion ? Self.reduceMotionFade : PillPopIn.duration
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(duration))
            guard let self, self.generation == mine else { return }
            self.display.popInStartedAt = nil
        }
    }

    private func hide() {
        guard display.isShown else { return }
        generation += 1
        if NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            display.isShown = false
            display.popInStartedAt = nil
            panel.orderOut(nil)
            return
        }
        withAnimation(Self.hideAnimation) { display.isShown = false }
        let mine = generation
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(PillHide.duration))
            guard let self, self.generation == mine else { return }
            self.display.popInStartedAt = nil
            self.panel.orderOut(nil)
        }
    }

    /// Centres the pill above the Dock on the screen with the focused window.
    private func place() {
        guard let screen = ScreenLocator.screenForFocusedWindow() else { return }
        let origin = PillPlacement.origin(
            screenFrame: screen.frame, visibleFrame: screen.visibleFrame, dock: ScreenLocator.dockLayout(),
            pillSize: CGSize(width: Self.panelWidth, height: PillView.height))
        panel.setFrameOrigin(CGPoint(x: origin.x, y: origin.y - Self.glowMargin))
    }
}

/// Borderless, non-activating, click-through panel above other windows. It never becomes key or main, so
/// the frontmost app keeps focus and receives the paste.
private final class PillPanel: NSPanel {
    init() {
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        isFloatingPanel = true
        level = .statusBar
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        ignoresMouseEvents = true
        hidesOnDeactivate = false
        // Hiding Akoya (to hand focus back) must not hide the pill.
        canHide = false
        isReleasedWhenClosed = false
        backgroundColor = .clear
        isOpaque = false
        // The view draws its own glow.
        hasShadow = false
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}
