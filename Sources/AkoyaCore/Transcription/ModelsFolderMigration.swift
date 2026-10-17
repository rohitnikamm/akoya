import Foundation

/// One-time move of the speech model folder from the pre-rename `Silk` location to `Akoya`,
/// so existing users do not download the model again.
public enum ModelsFolderMigration {
    public enum Result: Equatable, Sendable {
        case moved
        case skippedTargetExists
        case skippedNoSource
        case failed(String)
    }

    /// Moves `old` to `new` only when `old` exists and `new` does not. Creates `new`'s parent.
    public static func migrate(
        from old: URL, to new: URL, fileManager: FileManager = .default
    ) -> Result {
        guard fileManager.fileExists(atPath: old.path) else { return .skippedNoSource }
        guard !fileManager.fileExists(atPath: new.path) else { return .skippedTargetExists }
        do {
            try fileManager.createDirectory(
                at: new.deletingLastPathComponent(), withIntermediateDirectories: true)
            try fileManager.moveItem(at: old, to: new)
            return .moved
        } catch {
            return .failed(error.localizedDescription)
        }
    }
}
