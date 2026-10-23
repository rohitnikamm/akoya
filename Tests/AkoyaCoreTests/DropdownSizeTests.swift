import AppKit
import SwiftUI
import Testing
@testable import AkoyaCore

/// The status card dropped its title, status chip and hint (docs/design/dropdown-cleanup-mockup.html)
/// but keeps the size it had, so the dropdown at rest stays 320 × 351 pt.
@MainActor @Test func dropdownAtRestKeepsItsSize() {
    let actions = DropdownActions(
        selectHotkey: { _ in }, selectMicrophone: { _ in }, quit: {}, retryModel: {}, finishSetup: {},
        copyLast: { _ in })
    let defaults = UserDefaults(suiteName: "DropdownSizeTests")!
    defaults.removePersistentDomain(forName: "DropdownSizeTests")
    let view = NSHostingView(rootView: DropdownView(
        model: WaveformModel(), settings: AkoyaSettings(defaults: defaults), modelState: .ready,
        permissionsGranted: true, inputs: [], defaultInputID: nil, actions: actions))
    #expect(view.fittingSize == CGSize(width: 320, height: 351))
}
