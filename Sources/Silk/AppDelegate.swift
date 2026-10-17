import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.autosaveName = "SilkStatusItem"
        item.button?.image = NSImage(systemSymbolName: "waveform", accessibilityDescription: "Silk")
        statusItem = item
    }
}
