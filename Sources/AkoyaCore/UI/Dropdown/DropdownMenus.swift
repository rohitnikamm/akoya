import AppKit
import SwiftUI

// MARK: - Models

/// What the Microphone row and its menu show: "System default (<name>)", then every input
/// device, with a check on the current choice. A chosen device that is not connected stays
/// listed and shown (checked, disabled, "(unavailable)") while recording falls back to the
/// system default.
public struct MicrophoneMenuModel: Equatable {
    public struct Item: Equatable {
        public let title: String
        /// The device UID to select; nil for System default.
        public let uid: String?
        public let isChecked: Bool
        public var isEnabled = true

        public init(title: String, uid: String?, isChecked: Bool, isEnabled: Bool = true) {
            self.title = title
            self.uid = uid
            self.isChecked = isChecked
            self.isEnabled = isEnabled
        }
    }

    /// The row's value: the chosen device, the default's name when following the system
    /// default ("System default" when there is none), or "<name> (unavailable)" while the
    /// chosen device is disconnected.
    public let rowValue: String
    public let systemDefault: Item
    public let devices: [Item]

    /// - Parameters:
    ///   - selectedUID: the saved choice (`AkoyaSettings.inputDeviceUID`); nil = System default.
    ///   - selectedName: the saved choice's name, shown while it is disconnected.
    public init(selectedUID: String?, selectedName: String?, inputs: [AudioInputDevice], defaultID: UInt32?) {
        let choice = MicrophoneResolver.resolve(selectedUID: selectedUID, inputs: inputs, defaultID: defaultID)
        let systemDefaultDevice = defaultID.flatMap { id in inputs.first { $0.id == id } }

        let unavailableTitle = "\(selectedName ?? "Selected microphone") (unavailable)"
        if choice.isFallback {
            rowValue = unavailableTitle
        } else {
            rowValue = choice.device?.name ?? "System default"
        }
        systemDefault = Item(
            title: systemDefaultDevice.map { "System default (\($0.name))" } ?? "System default",
            uid: nil, isChecked: selectedUID == nil)
        var devices = inputs.map { Item(title: $0.name, uid: $0.uid, isChecked: $0.uid == selectedUID) }
        if let selectedUID, choice.isFallback {
            devices.append(Item(title: unavailableTitle, uid: selectedUID, isChecked: true, isEnabled: false))
        }
        self.devices = devices
    }
}

/// The hotkey menu's rows: every key, its symbol, and a check on the current one.
public enum HotkeyMenuModel {
    public struct Item: Equatable {
        public let key: HotkeyKey
        public let title: String
        public let symbol: String
        public let isChecked: Bool
    }

    public static func items(current: HotkeyKey) -> [Item] {
        HotkeyKey.allCases.map { Item(key: $0, title: $0.displayName, symbol: $0.symbol, isChecked: $0 == current) }
    }
}

/// The Menu bar icon row's value: the current look while collapsed, nothing (just the rotated
/// chevron) while expanded.
public enum MenuBarIconDisclosure {
    public static func value(look: WaveLook, isExpanded: Bool) -> String? {
        isExpanded ? nil : look.displayName
    }
}

// MARK: - Labels

extension HotkeyKey {
    /// Human-readable key name, shown in the hotkey menu.
    public var displayName: String {
        switch self {
        case .rightOption: "Right Option"
        case .rightCommand: "Right Command"
        case .rightControl: "Right Control"
        }
    }

    /// The modifier's symbol, e.g. "⌥".
    public var symbol: String {
        switch self {
        case .rightOption: "⌥"
        case .rightCommand: "⌘"
        case .rightControl: "⌃"
        }
    }

    /// Short keycap label, e.g. "Right ⌘".
    public var keycapLabel: String { "Right \(symbol)" }
}

extension WaveLook {
    public var displayName: String {
        switch self {
        case .pixel: "Pixel"
        case .solid: "Solid"
        }
    }
}

// MARK: - Native menus

/// Native menus for the dropdown. They are popped up from the panel (see `PopUpMenuAction`);
/// menu tracking never activates Akoya or makes the panel key, so the frontmost app keeps focus.
@MainActor
enum DropdownMenus {
    static func hotkey(current: HotkeyKey, select: @escaping (HotkeyKey) -> Void) -> NSMenu {
        let menu = NSMenu(title: "Hotkey")
        menu.addItem(.sectionHeader(title: "Hotkey"))
        for item in HotkeyMenuModel.items(current: current) {
            let menuItem = ClosureMenuItem(title: item.title) { select(item.key) }
            menuItem.attributedTitle = titleWithTrailingSymbol(item.title, item.symbol)
            menuItem.state = item.isChecked ? .on : .off
            menu.addItem(menuItem)
        }
        return menu
    }

    static func microphone(_ model: MicrophoneMenuModel, select: @escaping (String?) -> Void) -> NSMenu {
        let menu = NSMenu(title: "Microphone")
        func add(_ item: MicrophoneMenuModel.Item) {
            let menuItem = ClosureMenuItem(title: item.title) { select(item.uid) }
            menuItem.state = item.isChecked ? .on : .off
            menuItem.isEnabled = item.isEnabled
            menu.addItem(menuItem)
        }
        add(model.systemDefault)
        if !model.devices.isEmpty { menu.addItem(.separator()) }
        model.devices.forEach(add)
        return menu
    }

    /// "Right Option" with "⌥" right-aligned in the secondary label colour.
    private static func titleWithTrailingSymbol(_ title: String, _ symbol: String) -> NSAttributedString {
        let font = NSFont.menuFont(ofSize: 0)
        let paragraph = NSMutableParagraphStyle()
        paragraph.tabStops = [NSTextTab(textAlignment: .right, location: 150)]
        let string = NSMutableAttributedString(
            string: "\(title)\t", attributes: [.font: font, .paragraphStyle: paragraph])
        string.append(NSAttributedString(
            string: symbol,
            attributes: [.font: font, .paragraphStyle: paragraph, .foregroundColor: NSColor.secondaryLabelColor]))
        return string
    }
}

/// A menu item that runs a closure; it is its own target, and the menu retains its items.
private final class ClosureMenuItem: NSMenuItem {
    private let handler: () -> Void

    init(title: String, handler: @escaping () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(run), keyEquivalent: "")
        target = self
    }

    @available(*, unavailable)
    required init(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    @objc private func run() { handler() }
}

// MARK: - Presenting

/// Pops a native menu up under a rect given in the hosting view's coordinates (SwiftUI's
/// global space in the panel). `StatusPanelController` supplies it; elsewhere (previews,
/// renders) it does nothing. Returns once the menu closes.
public struct PopUpMenuAction: Sendable {
    public enum Alignment: Sendable { case leading, trailing }

    private let handler: @MainActor @Sendable (NSMenu, CGRect, Alignment) -> Void

    public init(_ handler: @escaping @MainActor @Sendable (NSMenu, CGRect, Alignment) -> Void) {
        self.handler = handler
    }

    @MainActor
    func callAsFunction(_ menu: NSMenu, below rect: CGRect, alignment: Alignment) {
        handler(menu, rect, alignment)
    }
}

private struct PopUpMenuKey: EnvironmentKey {
    static let defaultValue = PopUpMenuAction { _, _, _ in }
}

extension EnvironmentValues {
    public var popUpMenu: PopUpMenuAction {
        get { self[PopUpMenuKey.self] }
        set { self[PopUpMenuKey.self] = newValue }
    }
}
