import Testing
@testable import AkoyaCore

struct MicrophoneMenuModelTests {
    private let builtIn = AudioInputDevice(id: 10, uid: "builtin", name: "MacBook Pro Microphone")
    private let airpods = AudioInputDevice(id: 20, uid: "airpods", name: "Rohit's AirPods Pro")

    @Test func followingSystemDefaultShowsTheDefaultsName() {
        let menu = MicrophoneMenuModel(
            selectedUID: nil, selectedName: nil, inputs: [builtIn, airpods], defaultID: builtIn.id)
        #expect(menu.rowValue == "MacBook Pro Microphone")
        #expect(menu.systemDefault == .init(title: "System default (MacBook Pro Microphone)", uid: nil, isChecked: true))
        #expect(menu.devices == [
            .init(title: "MacBook Pro Microphone", uid: "builtin", isChecked: false),
            .init(title: "Rohit's AirPods Pro", uid: "airpods", isChecked: false),
        ])
    }

    @Test func connectedSelectionIsCheckedAndShown() {
        let menu = MicrophoneMenuModel(
            selectedUID: "airpods", selectedName: "Rohit's AirPods Pro", inputs: [builtIn, airpods],
            defaultID: builtIn.id)
        #expect(menu.rowValue == "Rohit's AirPods Pro")
        #expect(menu.systemDefault == .init(title: "System default (MacBook Pro Microphone)", uid: nil, isChecked: false))
        #expect(menu.devices.map(\.isChecked) == [false, true])
    }

    @Test func missingSelectionIsListedAsUnavailableAndRowFallsBack() {
        let menu = MicrophoneMenuModel(
            selectedUID: "airpods", selectedName: "Rohit's AirPods Pro", inputs: [builtIn], defaultID: builtIn.id)
        #expect(menu.rowValue == "System default")
        #expect(!menu.systemDefault.isChecked)
        #expect(menu.devices == [
            .init(title: "MacBook Pro Microphone", uid: "builtin", isChecked: false),
            .init(title: "Rohit's AirPods Pro (unavailable)", uid: "airpods", isChecked: true, isEnabled: false),
        ])
    }

    @Test func missingSelectionWithoutAStoredNameStillReadsAsUnavailable() {
        let menu = MicrophoneMenuModel(selectedUID: "gone", selectedName: nil, inputs: [builtIn], defaultID: builtIn.id)
        #expect(menu.devices.last == .init(
            title: "Selected microphone (unavailable)", uid: "gone", isChecked: true, isEnabled: false))
    }

    @Test func noDefaultDeviceShowsPlainSystemDefault() {
        let menu = MicrophoneMenuModel(selectedUID: nil, selectedName: nil, inputs: [], defaultID: nil)
        #expect(menu.rowValue == "System default")
        #expect(menu.systemDefault == .init(title: "System default", uid: nil, isChecked: true))
        #expect(menu.devices.isEmpty)
    }
}

struct HotkeyMenuModelTests {
    @Test func listsEveryKeyWithItsSymbolAndChecksTheCurrentOne() {
        let items = HotkeyMenuModel.items(current: .rightCommand)
        #expect(items == [
            .init(key: .rightOption, title: "Right Option", symbol: "⌥", isChecked: false),
            .init(key: .rightCommand, title: "Right Command", symbol: "⌘", isChecked: true),
            .init(key: .rightControl, title: "Right Control", symbol: "⌃", isChecked: false),
        ])
    }

    @Test func keycapLabelsUseTheSymbol() {
        #expect(HotkeyKey.rightOption.keycapLabel == "Right ⌥")
        #expect(HotkeyKey.rightControl.keycapLabel == "Right ⌃")
    }
}

struct MenuBarIconDisclosureTests {
    @Test func collapsedShowsTheCurrentLook() {
        #expect(MenuBarIconDisclosure.value(look: .pixel, isExpanded: false) == "Pixel")
        #expect(MenuBarIconDisclosure.value(look: .solid, isExpanded: false) == "Solid")
    }

    @Test func expandedShowsOnlyTheChevron() {
        #expect(MenuBarIconDisclosure.value(look: .solid, isExpanded: true) == nil)
    }
}
