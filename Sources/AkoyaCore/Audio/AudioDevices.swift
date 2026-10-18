import CoreAudio
import Foundation
import os

/// CoreAudio queries for input devices. Everything here is read-only and safe to call from
/// any thread.
public enum AudioDevices {
    private static let log = Logger(subsystem: "com.rohitnikamm.akoya", category: "audio-devices")

    /// Every connected device that has at least one input stream, in CoreAudio's order.
    public static func inputs() -> [AudioInputDevice] {
        deviceIDs().compactMap { id in
            guard hasInputStreams(id), let uid = stringProperty(id, kAudioDevicePropertyDeviceUID) else {
                return nil
            }
            let name = stringProperty(id, kAudioObjectPropertyName) ?? uid
            return AudioInputDevice(id: id, uid: uid, name: name)
        }
    }

    /// The system default input device, or nil when there is none.
    public static func defaultInputID() -> UInt32? {
        var address = address(kAudioHardwarePropertyDefaultInputDevice)
        var id = AudioDeviceID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        let status = AudioObjectGetPropertyData(
            AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &id)
        guard status == noErr, id != kAudioObjectUnknown else { return nil }
        return id
    }

    /// Calls `onChange` on the main actor whenever devices are added or removed or the default
    /// input changes. Keep the returned token alive; deallocating it removes the listeners.
    public static func observeChanges(_ onChange: @escaping @MainActor () -> Void) -> AnyObject {
        ChangeToken(onChange)
    }

    // MARK: - CoreAudio plumbing

    private static func address(
        _ selector: AudioObjectPropertySelector,
        scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal
    ) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(
            mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
    }

    private static func deviceIDs() -> [AudioDeviceID] {
        var address = address(kAudioHardwarePropertyDevices)
        var size: UInt32 = 0
        let system = AudioObjectID(kAudioObjectSystemObject)
        guard AudioObjectGetPropertyDataSize(system, &address, 0, nil, &size) == noErr, size > 0 else {
            return []
        }
        var ids = [AudioDeviceID](repeating: 0, count: Int(size) / MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(system, &address, 0, nil, &size, &ids) == noErr else { return [] }
        return ids
    }

    private static func hasInputStreams(_ id: AudioDeviceID) -> Bool {
        var address = address(kAudioDevicePropertyStreams, scope: kAudioObjectPropertyScopeInput)
        var size: UInt32 = 0
        return AudioObjectGetPropertyDataSize(id, &address, 0, nil, &size) == noErr && size > 0
    }

    private static func stringProperty(_ id: AudioObjectID, _ selector: AudioObjectPropertySelector) -> String? {
        var address = address(selector)
        var value: Unmanaged<CFString>?
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        let status = withUnsafeMutablePointer(to: &value) {
            AudioObjectGetPropertyData(id, &address, 0, nil, &size, $0)
        }
        guard status == noErr, let value else { return nil }
        return value.takeRetainedValue() as String
    }

    /// Registers CoreAudio property listeners on init and removes them on deinit.
    private final class ChangeToken {
        private static let selectors: [AudioObjectPropertySelector] = [
            kAudioHardwarePropertyDevices, kAudioHardwarePropertyDefaultInputDevice,
        ]

        private let block: AudioObjectPropertyListenerBlock

        init(_ onChange: @escaping @MainActor () -> Void) {
            // Delivered on the main queue, so the main actor is already the running context.
            block = { _, _ in MainActor.assumeIsolated { onChange() } }
            for selector in Self.selectors {
                var address = AudioDevices.address(selector)
                let status = AudioObjectAddPropertyListenerBlock(
                    AudioObjectID(kAudioObjectSystemObject), &address, DispatchQueue.main, block)
                if status != noErr {
                    AudioDevices.log.error("Could not observe audio device changes (status \(status))")
                }
            }
        }

        deinit {
            for selector in Self.selectors {
                var address = AudioDevices.address(selector)
                AudioObjectRemovePropertyListenerBlock(
                    AudioObjectID(kAudioObjectSystemObject), &address, DispatchQueue.main, block)
            }
        }
    }
}
