import FluidAudio
import Foundation
import OSLog

/// The speech model that ships inside the app bundle, and clean-up of copies downloaded by older versions.
public enum BuiltInModel {
    /// Folder name under `Resources/Models`; it matches FluidAudio's repo folder for v2, which `modelsExist` expects.
    public static let folderName = "parakeet-tdt-0.6b-v2"

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
    public static func removeDownloadedCopies(
        modelsDirectory: URL, legacyDirectory: URL, fileManager: FileManager = .default
    ) {
        func remove(_ url: URL) {
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
