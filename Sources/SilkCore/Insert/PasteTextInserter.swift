import AppKit
import ApplicationServices

public enum InsertError: Error, Equatable { case accessibilityDenied }

/// Types text into the focused app by putting it on the pasteboard, simulating ⌘V, and then
/// restoring whatever the user had on the clipboard — unless they copied something meanwhile.
@MainActor public final class PasteTextInserter: TextInserting {
    private let pasteboard: NSPasteboard
    private let isTrusted: () -> Bool
    private let postPaste: () -> Void
    private let restoreDelay: Duration

    static let transientType = NSPasteboard.PasteboardType("org.nspasteboard.TransientType")

    public init(pasteboard: NSPasteboard = .general,
                isTrusted: @escaping () -> Bool = { AXIsProcessTrusted() },
                postPaste: @escaping () -> Void = PasteTextInserter.postCommandV,
                restoreDelay: Duration = .milliseconds(300)) {
        self.pasteboard = pasteboard
        self.isTrusted = isTrusted
        self.postPaste = postPaste
        self.restoreDelay = restoreDelay
    }

    /// Pastes `text` as given. Without Accessibility trust the text is left on the clipboard
    /// (so the user can paste it by hand) and `InsertError.accessibilityDenied` is thrown.
    public func insert(_ text: String) async throws {
        let snapshot = PasteboardSnapshot(capturing: pasteboard)
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)
        // Convention honoured by clipboard managers: skip this transient entry.
        pasteboard.setData(Data(), forType: Self.transientType)
        let changeCountAfterWrite = pasteboard.changeCount

        guard isTrusted() else { throw InsertError.accessibilityDenied }

        postPaste()
        // Give the target app time to read the pasteboard before we put the old contents back.
        try? await Task.sleep(for: restoreDelay)

        // If the count moved, the user (or another app) copied something; theirs wins.
        guard pasteboard.changeCount == changeCountAfterWrite else { return }
        snapshot.restore(to: pasteboard)
    }

    /// Posts ⌘V (virtual key 9) key-down and key-up to the HID event tap.
    public nonisolated static func postCommandV() {
        let source = CGEventSource(stateID: .combinedSessionState)
        for keyDown in [true, false] {
            let event = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: keyDown)
            event?.flags = .maskCommand
            event?.post(tap: .cghidEventTap)
        }
    }
}
