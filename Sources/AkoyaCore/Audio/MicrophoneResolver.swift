import Foundation

/// An audio device that can capture sound. `id` is the CoreAudio device ID (only stable until
/// the device disconnects); `uid` is the persistent identifier used for the saved preference.
public struct AudioInputDevice: Equatable, Sendable, Identifiable {
    public let id: UInt32
    public let uid: String
    public let name: String

    public init(id: UInt32, uid: String, name: String) {
        self.id = id
        self.uid = uid
        self.name = name
    }
}

/// The microphone a recording should use. `device == nil` means "let the engine use the system
/// default". `isFallback` is true when the user chose a specific device that is not connected.
public struct MicrophoneChoice: Equatable {
    public let device: AudioInputDevice?
    public let isFallback: Bool

    public init(device: AudioInputDevice?, isFallback: Bool) {
        self.device = device
        self.isFallback = isFallback
    }
}

public enum MicrophoneResolver {
    /// Picks the selected device when it is connected, otherwise the system default.
    public static func resolve(
        selectedUID: String?, inputs: [AudioInputDevice], defaultID: UInt32?
    ) -> MicrophoneChoice {
        if let selectedUID, let selected = inputs.first(where: { $0.uid == selectedUID }) {
            return MicrophoneChoice(device: selected, isFallback: false)
        }
        let systemDefault = defaultID.flatMap { id in inputs.first { $0.id == id } }
        return MicrophoneChoice(device: systemDefault, isFallback: selectedUID != nil)
    }
}
