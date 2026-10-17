import CoreGraphics

/// A physical modifier key that can act as the push-to-talk key.
public enum HotkeyKey: String, CaseIterable, Sendable {
    case rightOption, rightCommand, rightControl

    /// Virtual key code reported on `flagsChanged` events.
    public var keyCode: Int64 {
        switch self {
        case .rightOption: 61
        case .rightCommand: 54
        case .rightControl: 62
        }
    }

    /// Device-dependent flag bit (NX_DEVICER*KEYMASK) that is set only while this exact key is down.
    public var deviceMask: UInt64 {
        switch self {
        case .rightOption: 0x40
        case .rightCommand: 0x10
        case .rightControl: 0x2000
        }
    }
}

public struct HotkeyDecision: Equatable {
    public let event: HotkeyEvent?
    public let swallow: Bool

    public init(event: HotkeyEvent?, swallow: Bool) {
        self.event = event
        self.swallow = swallow
    }
}

/// Pure translation of raw key events into push-to-talk events. Only Esc is ever swallowed.
public enum HotkeyDecoder {
    private static let escapeKeyCode: Int64 = 53

    public static func decode(type: CGEventType, keyCode: Int64, flags: UInt64,
                              key: HotkeyKey, isRecording: Bool, isHeld: Bool) -> HotkeyDecision {
        let pass = HotkeyDecision(event: nil, swallow: false)
        let keyIsDown = flags & key.deviceMask != 0
        switch type {
        case .flagsChanged:
            if keyCode == key.keyCode {
                if keyIsDown && !isHeld { return .init(event: .pressed, swallow: false) }
                if !keyIsDown && isHeld { return .init(event: .released, swallow: false) }
                return pass
            }
            // Another modifier changed while we think the key is held, but the key's bit is
            // gone: its key-up was missed (e.g. tap was disabled), so recover.
            if isHeld && !keyIsDown { return .init(event: .released, swallow: false) }
            return pass
        case .keyDown:
            if keyCode == escapeKeyCode && isRecording { return .init(event: .cancel, swallow: true) }
            return pass
        default:
            return pass
        }
    }

    /// Ground-truth check after the tap may have missed events: if we believe the key is held
    /// but the live modifier state says it is up, its key-up was lost and we owe a `.released`.
    public static func reconcile(isHeld: Bool, currentFlags: UInt64, key: HotkeyKey) -> HotkeyEvent? {
        isHeld && currentFlags & key.deviceMask == 0 ? .released : nil
    }
}
