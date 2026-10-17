import AppKit
import Foundation
import Testing
@testable import SilkCore

private let blobType = NSPasteboard.PasteboardType("com.test.blob")
private let shortDelay: Duration = .milliseconds(30)
private let settle: Duration = .milliseconds(150)

private func makePasteboard() -> NSPasteboard {
    NSPasteboard(name: .init("silk-test-\(UUID().uuidString)"))
}

@MainActor
private final class PasteRecorder {
    var count = 0
    var stringAtPaste: String?
}

@MainActor
@Suite struct PasteTextInserterTests {
    @Test func restoresAllTypes() async throws {
        let pb = makePasteboard()
        defer { pb.releaseGlobally() }
        let png = Data([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF])
        let blob = Data((0..<64).map { UInt8($0 &* 3) })
        let item = NSPasteboardItem()
        item.setString("original", forType: .string)
        item.setData(png, forType: .png)
        item.setData(blob, forType: blobType)
        pb.clearContents()
        #expect(pb.writeObjects([item]))

        let recorder = PasteRecorder()
        let inserter = PasteTextInserter(
            pasteboard: pb, isTrusted: { true },
            postPaste: { recorder.count += 1 }, restoreDelay: shortDelay)
        try await inserter.insert("hi")
        try await Task.sleep(for: settle)

        #expect(recorder.count == 1)
        #expect(pb.pasteboardItems?.count == 1)
        #expect(pb.string(forType: .string) == "original")
        #expect(pb.data(forType: .png) == png)
        #expect(pb.data(forType: blobType) == blob)
    }

    @Test func textIsOnPasteboardWhenPasteFires() async throws {
        let pb = makePasteboard()
        defer { pb.releaseGlobally() }
        pb.clearContents()
        pb.setString("original", forType: .string)

        let recorder = PasteRecorder()
        let inserter = PasteTextInserter(
            pasteboard: pb, isTrusted: { true },
            postPaste: { recorder.stringAtPaste = pb.string(forType: .string); recorder.count += 1 },
            restoreDelay: shortDelay)
        try await inserter.insert("hi")
        try await Task.sleep(for: settle)

        #expect(recorder.stringAtPaste == "hi")
        #expect(pb.string(forType: .string) == "original")
    }

    @Test func userCopyDuringWindowWins() async throws {
        let pb = makePasteboard()
        defer { pb.releaseGlobally() }
        pb.clearContents()
        pb.setString("original", forType: .string)

        let inserter = PasteTextInserter(
            pasteboard: pb, isTrusted: { true },
            postPaste: {
                // The user copies something while the restore window is open.
                Task { @MainActor in
                    try? await Task.sleep(for: .milliseconds(5))
                    pb.clearContents()
                    pb.setString("mine", forType: .string)
                }
            },
            restoreDelay: shortDelay)
        try await inserter.insert("hi")
        try await Task.sleep(for: settle)

        #expect(pb.string(forType: .string) == "mine")
    }

    @Test func emptyClipboardRestoresEmpty() async throws {
        let pb = makePasteboard()
        defer { pb.releaseGlobally() }
        pb.clearContents()

        let inserter = PasteTextInserter(
            pasteboard: pb, isTrusted: { true }, postPaste: {}, restoreDelay: shortDelay)
        try await inserter.insert("hi")
        try await Task.sleep(for: settle)

        #expect(pb.pasteboardItems?.isEmpty ?? true)
        #expect(pb.string(forType: .string) == nil)
    }

    @Test func untrustedLeavesTextAndThrows() async throws {
        let pb = makePasteboard()
        defer { pb.releaseGlobally() }
        pb.clearContents()
        pb.setString("original", forType: .string)

        let recorder = PasteRecorder()
        let inserter = PasteTextInserter(
            pasteboard: pb, isTrusted: { false },
            postPaste: { recorder.count += 1 }, restoreDelay: shortDelay)

        await #expect(throws: InsertError.accessibilityDenied) {
            try await inserter.insert("hi")
        }
        try await Task.sleep(for: settle)

        #expect(pb.string(forType: .string) == "hi")
        #expect(recorder.count == 0)
    }
}
