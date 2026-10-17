import Foundation
import Testing
@testable import AkoyaCore

struct ModelsFolderMigrationTests {
    private func makeRoot() throws -> URL {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("akoya-migration-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        return root
    }

    private func writeFile(_ text: String, named name: String, in directory: URL) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try text.write(to: directory.appendingPathComponent(name), atomically: true, encoding: .utf8)
    }

    private func readFile(named name: String, in directory: URL) -> String? {
        try? String(contentsOf: directory.appendingPathComponent(name), encoding: .utf8)
    }

    @Test func movesWhenOnlyOldExists() throws {
        let root = try makeRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let old = root.appendingPathComponent("Silk/Models", isDirectory: true)
        let new = root.appendingPathComponent("Akoya/Models", isDirectory: true)
        try writeFile("model", named: "weights.bin", in: old)
        try FileManager.default.createDirectory(
            at: new.deletingLastPathComponent(), withIntermediateDirectories: true)

        let result = ModelsFolderMigration.migrate(from: old, to: new)

        #expect(result == .moved)
        #expect(readFile(named: "weights.bin", in: new) == "model")
        #expect(!FileManager.default.fileExists(atPath: old.path))
    }

    @Test func keepsExistingTarget() throws {
        let root = try makeRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let old = root.appendingPathComponent("Silk/Models", isDirectory: true)
        let new = root.appendingPathComponent("Akoya/Models", isDirectory: true)
        try writeFile("old", named: "weights.bin", in: old)
        try writeFile("new", named: "weights.bin", in: new)

        let result = ModelsFolderMigration.migrate(from: old, to: new)

        #expect(result == .skippedTargetExists)
        #expect(readFile(named: "weights.bin", in: old) == "old")
        #expect(readFile(named: "weights.bin", in: new) == "new")
    }

    @Test func noSourceIsNoop() throws {
        let root = try makeRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let old = root.appendingPathComponent("Silk/Models", isDirectory: true)
        let new = root.appendingPathComponent("Akoya/Models", isDirectory: true)

        let result = ModelsFolderMigration.migrate(from: old, to: new)

        #expect(result == .skippedNoSource)
        #expect(!FileManager.default.fileExists(atPath: new.path))
        #expect(!FileManager.default.fileExists(atPath: new.deletingLastPathComponent().path))
    }

    @Test func createsParentOfTarget() throws {
        let root = try makeRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let old = root.appendingPathComponent("Silk/Models", isDirectory: true)
        let new = root.appendingPathComponent("Akoya/Models", isDirectory: true)
        try writeFile("model", named: "weights.bin", in: old)
        #expect(!FileManager.default.fileExists(atPath: new.deletingLastPathComponent().path))

        let result = ModelsFolderMigration.migrate(from: old, to: new)

        #expect(result == .moved)
        #expect(readFile(named: "weights.bin", in: new) == "model")
    }
}
