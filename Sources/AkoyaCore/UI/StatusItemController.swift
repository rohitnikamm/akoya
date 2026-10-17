import AppKit
import Combine
import SwiftUI

/// An `NSHostingView` that never takes mouse events, so clicks reach the status item button
/// underneath.
private final class PassthroughHostingView<Content: View>: NSHostingView<Content> {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

/// Owns the menu bar item: hosts `WaveformView` in the status button (the hosting view draws
/// no background, so only the system highlight shows), reports clicks on it
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
    private let clickTarget: ClickTarget
    private var wantsHighlight = false
    private var mouseUpMonitor: Any?

    /// - Parameter onClick: called with the item's button on each left or right click.
    public init(model: WaveformModel, onClick: @escaping (NSStatusBarButton) -> Void) {
        self.model = model
        clickTarget = ClickTarget(onClick: onClick)
        statusItem = NSStatusBar.system.statusItem(withLength: WaveformGeometry.idleItemWidth)
        statusItem.autosaveName = Self.autosaveName

        if let button = statusItem.button {
            button.setAccessibilityLabel("Akoya")
            button.target = clickTarget
            button.action = #selector(ClickTarget.clicked(_:))
            // Open on mouse-down, like native items, so the system never shows its dark *pressed*
            // pill while the button is held; the selected look comes from `setHighlighted`.
            button.sendAction(on: [.leftMouseDown, .rightMouseDown])
            let hosting = PassthroughHostingView(rootView: WaveformView(model: model))
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

    /// Shows or clears the system "selected" highlight on the item while its dropdown is open.
    ///
    /// An action sent on mouse-down is followed by the button's own tracking loop, which clears
    /// the highlight again on mouse-up. So while highlighted, the next mouse-up re-asserts it
    /// (after the button has finished tracking) unless the dropdown has closed by then.
    public func setHighlighted(_ highlighted: Bool) {
        wantsHighlight = highlighted
        statusItem.button?.highlight(highlighted)
        if let mouseUpMonitor {
            NSEvent.removeMonitor(mouseUpMonitor)
            self.mouseUpMonitor = nil
        }
        guard highlighted else { return }
        mouseUpMonitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseUp, .rightMouseUp]) { [weak self] event in
            MainActor.assumeIsolated {
                guard let self else { return }
                if let monitor = self.mouseUpMonitor {
                    NSEvent.removeMonitor(monitor)
                    self.mouseUpMonitor = nil
                }
                DispatchQueue.main.async { [weak self] in
                    guard let self, self.wantsHighlight else { return }
                    self.statusItem.button?.highlight(true)
                }
            }
            return event
        }
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

/// Objective-C target for the status button's action.
@MainActor
private final class ClickTarget: NSObject {
    private let onClick: (NSStatusBarButton) -> Void

    init(onClick: @escaping (NSStatusBarButton) -> Void) {
        self.onClick = onClick
    }

    @objc func clicked(_ sender: NSStatusBarButton) {
        onClick(sender)
    }
}
