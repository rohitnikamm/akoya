import AppKit
import Combine
import SwiftUI

/// An `NSHostingView` that never takes mouse events, so clicks reach the status item button
/// underneath and open its menu.
private final class PassthroughHostingView<Content: View>: NSHostingView<Content> {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

/// Owns the menu bar item: hosts `WaveformView` in the status button and keeps the item's
/// width in step with the dictation phase.
@MainActor
public final class StatusItemController {
    public static let autosaveName = "SilkStatusItem"
    private static let expandDuration: TimeInterval = 0.24

    private let model: WaveformModel
    private let statusItem: NSStatusItem
    private var subscriptions: Set<AnyCancellable> = []
    private var widthTimer: Timer?

    public init(model: WaveformModel, menu: NSMenu) {
        self.model = model
        statusItem = NSStatusBar.system.statusItem(withLength: WaveformGeometry.idleItemWidth)
        statusItem.autosaveName = Self.autosaveName
        statusItem.menu = menu

        if let button = statusItem.button {
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
        // .common so the tween keeps running while a menu is being tracked.
        RunLoop.main.add(timer, forMode: .common)
        widthTimer = timer
    }
}
