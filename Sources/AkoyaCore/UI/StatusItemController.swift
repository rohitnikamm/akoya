import AppKit
import Combine
import QuartzCore
import SwiftUI

/// Hosts `WaveformView` in the status button and takes the item's clicks itself, so the
/// `NSStatusBarButton` underneath never tracks a click and never draws the system dark
/// "pressed" capsule. The selected look is drawn by `WaveformView` (`WaveformModel.isSelected`).
///
/// Opens on mouse-down (like native items). It is a subview of the button, so it lives in the
/// same window, which the dropdown panel's click monitor skips for the anchor button.
final class StatusItemHostingView: NSHostingView<WaveformView> {
    /// Invoked for a left or right mouse-down, or an accessibility press.
    var onPress: (() -> Void)?

    convenience init(model: WaveformModel, onPress: @escaping () -> Void) {
        self.init(rootView: WaveformView(model: model))
        self.onPress = onPress
    }

    required init(rootView: WaveformView) { super.init(rootView: rootView) }

    @MainActor @preconcurrency required dynamic init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    /// `point` is in the superview's coordinates.
    override func hitTest(_ point: NSPoint) -> NSView? {
        bounds.contains(convert(point, from: superview)) ? self : nil
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) { onPress?() }
    override func rightMouseDown(with event: NSEvent) { onPress?() }

    // Overridden (not set) because NSHostingView supplies its own group role and children.
    override func isAccessibilityElement() -> Bool { true }
    override func accessibilityRole() -> NSAccessibility.Role? { .button }
    override func accessibilityLabel() -> String? { "Akoya" }
    override func accessibilityChildren() -> [Any]? { [] }

    override func accessibilityPerformPress() -> Bool {
        onPress?()
        return true
    }
}

/// Calls `onFrame` from a display link (or a fallback timer). A separate NSObject because the
/// display link needs an Objective-C target and retains it.
@MainActor
private final class FrameTicker: NSObject {
    private let onFrame: @MainActor () -> Void

    init(onFrame: @escaping @MainActor () -> Void) {
        self.onFrame = onFrame
    }

    @objc func tick(_ sender: Any) { onFrame() }
}

/// Owns the menu bar item: hosts `WaveformView` in the status button, reports clicks on it
/// (the app toggles the dropdown panel), and keeps the item's width in step with the
/// dictation phase.
///
/// Width changes tween `NSStatusItem.length` with `ItemTween` on a display-synced clock
/// while the old content cross-fades into the new (`WaveformModel.fade`). The item grows and
/// shrinks at its leading edge (its trailing edge is held by the next item), and
/// `WaveformView` pins content to the trailing edge, so content stays put on screen. With
/// Reduce Motion the width jumps and only the short cross-fade plays.
@MainActor
public final class StatusItemController {
    public static let autosaveName = "AkoyaStatusItem"

    private let model: WaveformModel
    private let statusItem: NSStatusItem
    private var subscriptions: Set<AnyCancellable> = []

    /// What the item shows once any running tween ends.
    private var shownContent: StatusItemContent
    private var tween: ItemTween?
    private var tweenFade: ContentFade?
    private var tweenBegan: CFTimeInterval = 0
    private var ticker: FrameTicker?
    private var displayLink: CADisplayLink?
    private var fallbackTimer: Timer?
    /// Ends the tween on time even if the display link never fires (e.g. a 0-width item that
    /// is off every display).
    private var deadline: Timer?

    /// - Parameter onClick: called with the item's button on each left or right click.
    public init(model: WaveformModel, onClick: @escaping (NSStatusBarButton) -> Void) {
        self.model = model
        statusItem = NSStatusBar.system.statusItem(withLength: WaveformGeometry.idleItemWidth)
        statusItem.autosaveName = Self.autosaveName
        shownContent = StatusItemContent(phase: model.phase, hideIdleIcon: model.hideIdleIcon)

        if let button = statusItem.button {
            button.setAccessibilityLabel("Akoya")
            let hosting = StatusItemHostingView(model: model) { [weak button] in
                guard let button else { return }
                onClick(button)
            }
            hosting.translatesAutoresizingMaskIntoConstraints = false
            button.addSubview(hosting)
            NSLayoutConstraint.activate([
                hosting.leadingAnchor.constraint(equalTo: button.leadingAnchor),
                hosting.trailingAnchor.constraint(equalTo: button.trailingAnchor),
                hosting.topAnchor.constraint(equalTo: button.topAnchor),
                hosting.bottomAnchor.constraint(equalTo: button.bottomAnchor),
            ])
        }

        // `@Published` emits in willSet, so use the delivered values rather than reading the
        // model, which still holds the old ones at that moment.
        model.$phase
            .combineLatest(model.$hideIdleIcon)
            .dropFirst()
            .sink { [weak self] phase, hideIdleIcon in
                self?.apply(phase: phase, hideIdleIcon: hideIdleIcon)
            }
            .store(in: &subscriptions)

        statusItem.length = WaveformGeometry.itemWidth(phase: model.phase, hideIdleIcon: model.hideIdleIcon)
    }

    /// Applies `WaveformGeometry.itemWidth` for the model's current state to the item length.
    public func refreshWidth() {
        apply(phase: model.phase, hideIdleIcon: model.hideIdleIcon)
    }

    private func apply(phase: DictationPhase, hideIdleIcon: Bool) {
        let target = WaveformGeometry.itemWidth(phase: phase, hideIdleIcon: hideIdleIcon)
        let content = StatusItemContent(phase: phase, hideIdleIcon: hideIdleIcon)
        // Already showing (or heading to) this state: leave any running tween alone.
        if content == shownContent && target == (tween?.toLength ?? statusItem.length) { return }

        // Fade out whatever dominates right now: the old target of a half-done fade, else its source.
        let from: StatusItemContent
        if let fade = model.fade, fade.progress < 0.5 { from = fade.from } else { from = shownContent }
        // Still `phaseEnteredAt` of the outgoing phase: this runs in `phase`'s willSet.
        let fromEnteredAt = model.fade.map { $0.progress < 0.5 ? $0.fromPhaseEnteredAt : model.phaseEnteredAt }
            ?? model.phaseEnteredAt
        stopTween()
        shownContent = content

        let tween = ItemTween(
            fromLength: statusItem.length.rounded(), toLength: target,
            reduceMotion: NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
        self.tween = tween
        tweenFade = from == content ? nil : ContentFade(from: from, progress: 0, fromPhaseEnteredAt: fromEnteredAt)
        tweenBegan = CACurrentMediaTime()
        // The start frame renders now, not one tick later.
        render(elapsed: 0)
        startTicking(for: tween.duration)
    }

    private func startTicking(for duration: TimeInterval) {
        let ticker = FrameTicker { [weak self] in self?.tick() }
        self.ticker = ticker
        if let button = statusItem.button, button.window != nil {
            let link = button.displayLink(target: ticker, selector: #selector(FrameTicker.tick(_:)))
            // .common so the tween keeps running during event tracking.
            link.add(to: .main, forMode: .common)
            displayLink = link
        } else {
            let timer = Timer(timeInterval: 1.0 / 120.0, target: ticker, selector: #selector(FrameTicker.tick(_:)),
                userInfo: nil, repeats: true)
            RunLoop.main.add(timer, forMode: .common)
            fallbackTimer = timer
        }
        let deadline = Timer(timeInterval: duration + 0.05, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.finishTween() }
        }
        RunLoop.main.add(deadline, forMode: .common)
        self.deadline = deadline
    }

    private func tick() {
        let elapsed = CACurrentMediaTime() - tweenBegan
        render(elapsed: elapsed)
        if tween?.isFinished(at: elapsed) ?? true { finishTween() }
    }

    private func render(elapsed: TimeInterval) {
        guard let tween else { return }
        let length = tween.length(at: elapsed)
        if statusItem.length != length { statusItem.length = length }
        var fade = tweenFade
        let progress = tween.fadeProgress(at: elapsed)
        fade?.progress = progress
        let next = progress < 1 ? fade : nil
        if model.fade != next { model.fade = next }
    }

    private func finishTween() {
        if let tween, statusItem.length != tween.toLength { statusItem.length = tween.toLength }
        if model.fade != nil { model.fade = nil }
        stopTween()
    }

    private func stopTween() {
        displayLink?.invalidate()
        displayLink = nil
        fallbackTimer?.invalidate()
        fallbackTimer = nil
        deadline?.invalidate()
        deadline = nil
        ticker = nil
        tween = nil
        tweenFade = nil
    }
}
