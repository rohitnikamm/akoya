import Testing
@testable import AkoyaCore

struct MicrophoneResolverTests {
    private let builtIn = AudioInputDevice(id: 10, uid: "builtin", name: "MacBook Pro Microphone")
    private let airpods = AudioInputDevice(id: 20, uid: "airpods", name: "AirPods")

    @Test func selectedDevicePresentIsUsedWithoutFallback() {
        let choice = MicrophoneResolver.resolve(
            selectedUID: "airpods", inputs: [builtIn, airpods], defaultID: builtIn.id)
        #expect(choice == MicrophoneChoice(device: airpods, isFallback: false))
    }

    @Test func selectedDeviceMissingFallsBackToDefault() {
        let choice = MicrophoneResolver.resolve(
            selectedUID: "gone", inputs: [builtIn, airpods], defaultID: builtIn.id)
        #expect(choice == MicrophoneChoice(device: builtIn, isFallback: true))
    }

    @Test func noSelectionUsesDefaultWithoutFallback() {
        let choice = MicrophoneResolver.resolve(
            selectedUID: nil, inputs: [builtIn, airpods], defaultID: airpods.id)
        #expect(choice == MicrophoneChoice(device: airpods, isFallback: false))
    }

    @Test func noSelectionAndNoDefaultYieldsNoDevice() {
        let choice = MicrophoneResolver.resolve(selectedUID: nil, inputs: [builtIn], defaultID: nil)
        #expect(choice == MicrophoneChoice(device: nil, isFallback: false))
    }

    @Test func missingSelectionAndNoDefaultYieldsNoDeviceAsFallback() {
        let choice = MicrophoneResolver.resolve(selectedUID: "gone", inputs: [], defaultID: nil)
        #expect(choice == MicrophoneChoice(device: nil, isFallback: true))
    }

    @Test func defaultIDNotInInputsYieldsNoDevice() {
        let choice = MicrophoneResolver.resolve(selectedUID: nil, inputs: [builtIn], defaultID: 99)
        #expect(choice.device == nil)
    }
}
