import FluidAudio
import Foundation

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
/// Call `prepare` once at launch: it downloads the model on first run, loads it, and reports
/// progress through `onState`. `transcribe` never downloads anything; before a successful
/// `prepare` it throws `ParakeetTranscriberError.notPrepared`.
public actor ParakeetTranscriber: Transcribing {
    private let modelsDirectory: URL
    private var manager: AsrManager?

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

    public init(modelsDirectory: URL = ParakeetTranscriber.defaultDirectory) {
        self.modelsDirectory = modelsDirectory
    }

    /// Downloads (if needed) and loads the model. Never throws: failure is reported as
    /// `.failed`, and calling `prepare` again retries.
    public func prepare(onState: @escaping @Sendable (ModelState) -> Void) async {
        if manager != nil {
            onState(.ready)
            return
        }
        onState(.downloading(nil))
        do {
            // FluidAudio keeps each model repo in a folder inside the directory's parent, so
            // `to:` names the repo folder inside our models directory.
            let repoFolder = AsrModels.defaultCacheDirectory(for: .v2).lastPathComponent
            let throttle = ProgressThrottle()
            let models = try await AsrModels.downloadAndLoad(
                to: modelsDirectory.appendingPathComponent(repoFolder, isDirectory: true),
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
