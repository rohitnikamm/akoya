import AppKit
import ApplicationServices

/// Where the pill goes: the screen the user is working on, and the Dock it has to clear.
@MainActor
public enum ScreenLocator {
    /// Room the Dock takes beyond its icons: its padding and the gap below it, points. Measured on
    /// macOS 26: a shown Dock reserves `tilesize` + 26 (74 at 48, 90 at 64).
    static let dockPadding: CGFloat = 26
    /// The Dock's icon size when the user never changed it (an unset `tilesize` reserves 90 pt).
    static let defaultTileSize: CGFloat = 64

    /// How long an Accessibility query may wait on the frontmost app: a hung app would otherwise
    /// block the main thread for the system default (about 6 s).
    static let messagingTimeout: Float = 0.2

    /// The screen containing the centre of the frontmost app's focused window (Accessibility), or
    /// `NSScreen.main` when there is none or it cannot be read. When Akoya itself is frontmost
    /// (onboarding, settings) its key window's screen is used instead of asking Accessibility.
    public static func screenForFocusedWindow() -> NSScreen? {
        if NSWorkspace.shared.frontmostApplication?.processIdentifier == getpid() {
            return NSApp.keyWindow?.screen ?? NSScreen.main
        }
        guard let frame = focusedWindowFrame(),
              let primary = NSScreen.screens.first else { return NSScreen.main }
        // Accessibility frames are top-left based, measured from the primary screen's top.
        let centre = CGPoint(x: frame.midX, y: primary.frame.maxY - frame.midY)
        return NSScreen.screens.first { $0.frame.contains(centre) } ?? NSScreen.main
    }

    /// The Dock's edge, auto-hide and thickness, from its preferences.
    public static func dockLayout() -> DockLayout {
        let dock = UserDefaults(suiteName: "com.apple.dock")
        let edge: DockEdge = switch dock?.string(forKey: "orientation") {
        case "left": .left
        case "right": .right
        default: .bottom
        }
        let tileSize = (dock?.object(forKey: "tilesize") as? NSNumber).map { CGFloat($0.doubleValue) } ?? defaultTileSize
        return DockLayout(edge: edge, autohide: dock?.bool(forKey: "autohide") ?? false,
                          thickness: tileSize + dockPadding)
    }

    private static func focusedWindowFrame() -> CGRect? {
        guard let app = NSWorkspace.shared.frontmostApplication else { return nil }
        let element = AXUIElementCreateApplication(app.processIdentifier)
        // Applies to queries on this element and the window elements it returns.
        AXUIElementSetMessagingTimeout(element, messagingTimeout)
        var window: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXFocusedWindowAttribute as CFString, &window) == .success,
              let window, CFGetTypeID(window) == AXUIElementGetTypeID() else { return nil }
        let windowElement = window as! AXUIElement
        var position = CGPoint.zero
        var size = CGSize.zero
        guard let positionValue = axValue(windowElement, kAXPositionAttribute),
              AXValueGetValue(positionValue, .cgPoint, &position),
              let sizeValue = axValue(windowElement, kAXSizeAttribute),
              AXValueGetValue(sizeValue, .cgSize, &size) else { return nil }
        return CGRect(origin: position, size: size)
    }

    private static func axValue(_ element: AXUIElement, _ attribute: String) -> AXValue? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success,
              let value, CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
        return (value as! AXValue)
    }
}
