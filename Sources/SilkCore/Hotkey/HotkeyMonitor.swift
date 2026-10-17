import CoreGraphics
import Foundation

/// Global push-to-talk key listener backed by a CGEventTap. Needs Accessibility permission;
/// `start()` returns false when the tap cannot be created. Runs entirely on the main thread.
@MainActor public final class HotkeyMonitor {
    private let key: () -> HotkeyKey
    private let isRecording: () -> Bool
    private let onEvent: (HotkeyEvent) -> Void

    private var tap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var isHeld = false

    public init(key: @escaping () -> HotkeyKey, isRecording: @escaping () -> Bool,
                onEvent: @escaping (HotkeyEvent) -> Void) {
        self.key = key
        self.isRecording = isRecording
        self.onEvent = onEvent
    }

    public func start() -> Bool {
        if tap != nil { return true }
        let mask: CGEventMask = (1 << CGEventType.flagsChanged.rawValue) | (1 << CGEventType.keyDown.rawValue)
        let callback: CGEventTapCallBack = { _, type, event, userInfo in
            guard let userInfo else { return Unmanaged.passUnretained(event) }
            let monitor = Unmanaged<HotkeyMonitor>.fromOpaque(userInfo).takeUnretainedValue()
            // The tap's source lives on the main run loop, so this runs on the main thread.
            let swallow = MainActor.assumeIsolated { monitor.handle(type: type, event: event) }
            return swallow ? nil : Unmanaged.passUnretained(event)
        }
        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
            eventsOfInterest: mask, callback: callback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()
        ) else { return false }
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        self.tap = tap
        self.runLoopSource = source
        return true
    }

    public func stop() {
        if let source = runLoopSource { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap { CGEvent.tapEnable(tap: tap, enable: false); CFMachPortInvalidate(tap) }
        tap = nil
        runLoopSource = nil
        isHeld = false
    }

    /// Returns true when the event must be swallowed.
    private func handle(type: CGEventType, event: CGEvent) -> Bool {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return false
        }
        let decision = HotkeyDecoder.decode(
            type: type, keyCode: event.getIntegerValueField(.keyboardEventKeycode),
            flags: event.flags.rawValue, key: key(), isRecording: isRecording(), isHeld: isHeld)
        switch decision.event {
        case .pressed: isHeld = true
        case .released: isHeld = false
        default: break
        }
        if let e = decision.event { onEvent(e) }
        return decision.swallow
    }
}
