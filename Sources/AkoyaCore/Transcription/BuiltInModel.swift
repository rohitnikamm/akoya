import FluidAudio
import Foundation
import OSLog

/// The speech model that ships inside the app bundle, and clean-up of copies downloaded by older versions.
public enum BuiltInModel {
    /// FluidAudio's repo folder for v2. The bundle, the download and the clean-up all use this name
    /// (`modelsExist` expects it).
    public static let folderName = AsrModels.defaultCacheDirectory(for: .v2).lastPathComponent

    private static let logger = Logger(subsystem: "com.rohitnikamm.akoya", category: "BuiltInModel")

    /// The bundled model folder, or nil when there is no bundle or the model in it is incomplete.
    public static func directory(in resources: URL?) -> URL? {
        guard let resources else { return nil }
        let directory = resources
            .appendingPathComponent("Models", isDirectory: true)
            .appendingPathComponent(folderName, isDirectory: true)
        return AsrModels.modelsExist(at: directory, version: .v2) ? directory : nil
    }

    /// Deletes the model downloaded by earlier versions (and the `Models` folder if that leaves it empty) and the
    /// pre-rename `Silk/Models` folder. Missing paths are ignored; failures are logged, never thrown.
    /// Anything equal to or containing `builtInDirectory` is never removed.
    public static func removeDownloadedCopies(
        modelsDirectory: URL, legacyDirectory: URL, builtInDirectory: URL?, fileManager: FileManager = .default
    ) {
        let protected = builtInDirectory?.standardizedFileURL.pathComponents
        func remove(_ url: URL) {
            if let protected, protected.starts(with: url.standardizedFileURL.pathComponents) { return }
            guard fileManager.fileExists(atPath: url.path) else { return }
            do {
                try fileManager.removeItem(at: url)
            } catch {
                logger.error("Could not remove \(url.path, privacy: .public): \(error.localizedDescription)")
            }
        }
        remove(modelsDirectory.appendingPathComponent(folderName, isDirectory: true))
        if let rest = try? fileManager.contentsOfDirectory(atPath: modelsDirectory.path), rest.isEmpty {
            remove(modelsDirectory)
        }
        remove(legacyDirectory)
    }
}
