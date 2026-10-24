import FluidAudio
import Foundation
import OSLog

public enum ModelState: Equatable, Sendable {
    case notReady
    /// Model files are being fetched and compiled. `nil` means progress is not known yet.
    case downloading(Double?)
    case ready
    case failed(String)
}

public enum ParakeetTranscriberError: Error, Equatable, Sendable {
    /// `transcribe` was called before `prepare` finished successfully.
    case notPrepared
}

extension ParakeetTranscriberError: LocalizedError {
    public var errorDescription: String? {
        switch self {
        case .notPrepared: return "The speech model is not ready yet."
        }
    }
}

/// On-device speech-to-text using Parakeet TDT v2 (English) through FluidAudio.
///
/// Call `prepare` once at launch: it loads the model built into the app (downloading it only if that is missing
/// or fails to load), and reports
/// progress through `onState`. `transcribe` never downloads anything; before a successful
/// `prepare` it throws `ParakeetTranscriberError.notPrepared`.
public actor ParakeetTranscriber: Transcribing {
    private let modelsDirectory: URL
    private let legacyDirectory: URL
    private let builtInDirectory: URL?
    private var manager: AsrManager?
    private static let logger = Logger(subsystem: "com.rohitnikamm.akoya", category: "ParakeetTranscriber")

    /// `~/Library/Application Support/Akoya/Models`
    public static var defaultDirectory: URL {
        FileManager.default
            .urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Akoya", isDirectory: true)
            .appendingPathComponent("Models", isDirectory: true)
    }

    /// `~/Library/Application Support/Silk/Models`, where models lived before the rename to Akoya.
    public static var legacyDirectory: URL {
        FileManager.default
            .urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Silk", isDirectory: true)
            .appendingPathComponent("Models", isDirectory: true)
    }

    /// `builtInDirectory` is the model inside the app bundle, if it is there and complete.
    public init(
        modelsDirectory: URL = ParakeetTranscriber.defaultDirectory,
        legacyDirectory: URL = ParakeetTranscriber.legacyDirectory,
        builtInDirectory: URL? = BuiltInModel.directory(in: Bundle.main.resourceURL)
    ) {
        self.modelsDirectory = modelsDirectory
        self.legacyDirectory = legacyDirectory
        self.builtInDirectory = builtInDirectory
    }

    /// Loads the model built into the app and, once that works, deletes any copy downloaded by older versions.
    /// If there is no built-in model, or it fails to load, downloads the model (if needed) and loads that instead.
    /// Never throws: failure is reported as `.failed`, and calling `prepare` again retries.
    public func prepare(onState: @escaping @Sendable (ModelState) -> Void) async {
        if manager != nil {
            onState(.ready)
            return
        }
        if let builtInDirectory {
            do {
                let models = try AsrModels.loadLocal(from: builtInDirectory, version: .v2)
                let asr = AsrManager(config: .default)
                try await asr.loadModels(models)
                manager = asr
                onState(.ready)
                BuiltInModel.removeDownloadedCopies(
                    modelsDirectory: modelsDirectory, legacyDirectory: legacyDirectory,
                    builtInDirectory: builtInDirectory)
                return
            } catch {
                Self.logger.error("Built-in model failed to load, downloading instead: \(error.localizedDescription)")
            }
        }
        onState(.downloading(nil))
        do {
            // FluidAudio keeps each model repo in a folder inside the directory's parent, so
            // `to:` names the repo folder inside our models directory.
            let throttle = ProgressThrottle()
            let models = try await AsrModels.downloadAndLoad(
                to: modelsDirectory.appendingPathComponent(BuiltInModel.folderName, isDirectory: true),
                version: .v2,
                progressHandler: { progress in
                    if let fraction = throttle.nextFraction(progress.fractionCompleted) {
                        onState(.downloading(fraction))
                    }
                }
            )
            let asr = AsrManager(config: .default)
            try await asr.loadModels(models)
            manager = asr
            onState(.ready)
        } catch {
            onState(.failed(error.localizedDescription))
        }
    }

    public func transcribe(_ samples: [Float]) async throws -> String {
        guard let manager else { throw ParakeetTranscriberError.notPrepared }
        // Decoder state carries context between calls; each dictation is independent.
        var state = TdtDecoderState.make(decoderLayers: await manager.decoderLayerCount)
        let result = try await manager.transcribe(samples, decoderState: &state)
        return result.text.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

/// FluidAudio reports progress thousands of times per download; pass on only whole-percent steps that move forward
/// (the compile phase reports 0.5 then 1.0 repeatedly, once per model file).
private final class ProgressThrottle: @unchecked Sendable {
    private let lock = NSLock()
    private var lastPercent = -1

    func nextFraction(_ raw: Double) -> Double? {
        let fraction = min(max(raw, 0), 1)
        let percent = Int(fraction * 100)
        lock.lock()
        defer { lock.unlock() }
        guard percent > lastPercent else { return nil }
        lastPercent = percent
        return fraction
    }
}
