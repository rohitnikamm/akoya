import CoreGraphics
import Foundation

public enum DockEdge: Sendable { case bottom, left, right }

/// Where the Dock is and how much room it takes. `thickness` is its height including its margin, in points.
public struct DockLayout: Equatable, Sendable {
    public var edge: DockEdge
    public var autohide: Bool
    public var thickness: CGFloat

    public init(edge: DockEdge, autohide: Bool, thickness: CGFloat) {
        self.edge = edge
        self.autohide = autohide
        self.thickness = thickness
    }
}

/// Where the dictation pill sits: centred on the visible frame, `gap` above the Dock. A bottom Dock reserves its
/// thickness on every screen (auto-hidden, or on a screen without the Dock), so the pill stays at one height.
public enum PillPlacement {
    public static let gap: CGFloat = 20

    /// The pill window's bottom-left, in AppKit coordinates (origin bottom-left).
    public static func origin(screenFrame: CGRect, visibleFrame: CGRect, dock: DockLayout, pillSize: CGSize) -> CGPoint {
        let reserved = dock.edge == .bottom ? dock.thickness : visibleFrame.minY - screenFrame.minY
        return CGPoint(x: (visibleFrame.midX - pillSize.width / 2).rounded(),
                       y: (screenFrame.minY + reserved + gap).rounded())
    }
}
