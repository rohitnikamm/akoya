import AppKit
import Combine
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

/// Owns the menu bar item: hosts `WaveformView` in the status button, reports clicks on it
/// (the app toggles the dropdown panel), and keeps the item's width in step with the
/// dictation phase.
@MainActor
public final class StatusItemController {
    public static let autosaveName = "AkoyaStatusItem"
    private static let expandDuration: TimeInterval = 0.24

    private let model: WaveformModel
    private let statusItem: NSStatusItem
    private var subscriptions: Set<AnyCancellable> = []
    private var widthTimer: Timer?

    /// - Parameter onClick: called with the item's button on each left or right click.
    public init(model: WaveformModel, onClick: @escaping (NSStatusBarButton) -> Void) {
        self.model = model
        statusItem = NSStatusBar.system.statusItem(withLength: WaveformGeometry.idleItemWidth)
        statusItem.autosaveName = Self.autosaveName

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
                self?.apply(WaveformGeometry.itemWidth(phase: phase, hideIdleIcon: hideIdleIcon))
            }
            .store(in: &subscriptions)

        statusItem.length = WaveformGeometry.itemWidth(phase: model.phase, hideIdleIcon: model.hideIdleIcon)
    }

    /// Applies `WaveformGeometry.itemWidth` for the model's current state to the item length.
    public func refreshWidth() {
        apply(WaveformGeometry.itemWidth(phase: model.phase, hideIdleIcon: model.hideIdleIcon))
    }

    private func apply(_ target: CGFloat) {
        widthTimer?.invalidate()
        widthTimer = nil
        let start = statusItem.length
        guard model.expandAnimation, start != target else {
            statusItem.length = target
            return
        }
        // NSStatusItem.length is a plain property, not an animatable view property, so
        // NSAnimationContext can't drive it. Tween it by hand instead.
        let began = Date()
        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                let progress = Date().timeIntervalSince(began) / Self.expandDuration
                self.statusItem.length = WaveformLayout.easedLength(from: start, to: target, progress: progress)
                if progress >= 1 {
                    self.widthTimer?.invalidate()
                    self.widthTimer = nil
                }
            }
        }
        // .common so the tween keeps running during event tracking.
        RunLoop.main.add(timer, forMode: .common)
        widthTimer = timer
    }
}
