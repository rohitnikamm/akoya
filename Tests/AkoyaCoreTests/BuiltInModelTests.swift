import Foundation
import Testing
@testable import AkoyaCore

private func makeRoot() throws -> URL {
    let root = FileManager.default.temporaryDirectory
        .appendingPathComponent("akoya-builtin-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    return root
}

private func touch(_ url: URL) throws {
    try FileManager.default.createDirectory(
        at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
    try Data("x".utf8).write(to: url)
}

private func exists(_ url: URL) -> Bool { FileManager.default.fileExists(atPath: url.path) }

@Test func completeBundleIsUsed() throws {
    let root = try makeRoot()
    defer { try? FileManager.default.removeItem(at: root) }
    let model = root.appendingPathComponent("Models/parakeet-tdt-0.6b-v2", isDirectory: true)
    for name in ["Preprocessor.mlmodelc", "Encoder.mlmodelc", "Decoder.mlmodelc", "JointDecision.mlmodelc",
                 "parakeet_vocab.json"] {
        try touch(model.appendingPathComponent(name))
    }
    #expect(BuiltInModel.directory(in: root)?.standardizedFileURL == model.standardizedFileURL)
}

@Test func incompleteBundleIsNotUsed() throws {
    let root = try makeRoot()
    defer { try? FileManager.default.removeItem(at: root) }
    try touch(root.appendingPathComponent("Models/parakeet-tdt-0.6b-v2/config.json"))
    #expect(BuiltInModel.directory(in: root) == nil)
}

@Test func missingResourcesIsNotUsed() {
    #expect(BuiltInModel.directory(in: nil) == nil)
}

@Test func removesTheDownloadedRepoFolderAndTheEmptyModelsFolder() throws {
    let root = try makeRoot()
    defer { try? FileManager.default.removeItem(at: root) }
    let models = root.appendingPathComponent("Akoya/Models", isDirectory: true)
    try touch(models.appendingPathComponent("parakeet-tdt-0.6b-v2/x"))
    BuiltInModel.removeDownloadedCopies(
        modelsDirectory: models, legacyDirectory: root.appendingPathComponent("Silk/Models"), builtInDirectory: nil)
    #expect(!exists(models))
}

@Test func keepsOtherFilesInTheModelsFolder() throws {
    let root = try makeRoot()
    defer { try? FileManager.default.removeItem(at: root) }
    let models = root.appendingPathComponent("Akoya/Models", isDirectory: true)
    try touch(models.appendingPathComponent("parakeet-tdt-0.6b-v2/x"))
    try touch(models.appendingPathComponent("other"))
    BuiltInModel.removeDownloadedCopies(
        modelsDirectory: models, legacyDirectory: root.appendingPathComponent("Silk/Models"), builtInDirectory: nil)
    #expect(!exists(models.appendingPathComponent("parakeet-tdt-0.6b-v2")))
    #expect(exists(models.appendingPathComponent("other")))
}

@Test func removesTheLegacySilkModels() throws {
    let root = try makeRoot()
    defer { try? FileManager.default.removeItem(at: root) }
    let legacy = root.appendingPathComponent("Silk/Models", isDirectory: true)
    try touch(legacy.appendingPathComponent("parakeet-tdt-0.6b-v2/x"))
    BuiltInModel.removeDownloadedCopies(
        modelsDirectory: root.appendingPathComponent("Akoya/Models"), legacyDirectory: legacy, builtInDirectory: nil)
    #expect(!exists(legacy))
    #expect(exists(root.appendingPathComponent("Silk")))
}

@Test func removingWhenNothingIsThereIsANoOp() throws {
    let root = try makeRoot()
    defer { try? FileManager.default.removeItem(at: root) }
    BuiltInModel.removeDownloadedCopies(
        modelsDirectory: root.appendingPathComponent("Akoya/Models"),
        legacyDirectory: root.appendingPathComponent("Silk/Models"), builtInDirectory: nil)
    #expect(try FileManager.default.contentsOfDirectory(atPath: root.path).isEmpty)
}

@Test func neverRemovesTheBuiltInFolder() throws {
    let root = try makeRoot()
    defer { try? FileManager.default.removeItem(at: root) }
    let models = root.appendingPathComponent("Models", isDirectory: true)
    let builtIn = models.appendingPathComponent(BuiltInModel.folderName, isDirectory: true)
    try touch(builtIn.appendingPathComponent("x"))
    BuiltInModel.removeDownloadedCopies(
        modelsDirectory: models, legacyDirectory: root.appendingPathComponent("Silk/Models"),
        builtInDirectory: builtIn)
    #expect(exists(builtIn.appendingPathComponent("x")))
    // A legacy folder that contains the built-in one is kept too.
    BuiltInModel.removeDownloadedCopies(
        modelsDirectory: root.appendingPathComponent("Other"), legacyDirectory: models, builtInDirectory: builtIn)
    #expect(exists(builtIn.appendingPathComponent("x")))
}
