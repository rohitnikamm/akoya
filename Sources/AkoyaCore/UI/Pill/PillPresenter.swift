import Foundation

/// What the pill shows.
public enum PillContent: Equatable {
    case listening, transcribing, pasted
    case message(PillMessage)
}

/// Decides what the pill shows and when it hides. `content` is nil while the pill is hidden.
/// Phases drive listening and transcribing; the outcome of each dictation drives the rest, so idle and
/// error phases change nothing. Whether a dictation's phases are shown is decided at `.recording` (not for
/// practice or menu bar mode). Outcomes always apply: the coordinator only reports them in pill mode.
@MainActor
public final class PillPresenter: ObservableObject {
    static let pastedDuration: TimeInterval = 0.9
    static let messageDuration: TimeInterval = 1.6

    @Published public private(set) var content: PillContent?
    /// The scheduled hide, kept so tests can await it.
    var pendingHide: Task<Void, Never>?

    private let sleep: (TimeInterval) async -> Void
    private var showsThisDictation = true
    private var generation = 0

    public init(sleep: @escaping (TimeInterval) async -> Void = { try? await Task.sleep(for: .seconds($0)) }) {
        self.sleep = sleep
    }

    public func phaseChanged(_ phase: DictationPhase, isPractice: Bool, usesPill: Bool) {
        switch phase {
        case .recording:
            cancelHide()
            showsThisDictation = usesPill && !isPractice
            content = showsThisDictation ? .listening : nil
        case .transcribing:
            if showsThisDictation { content = .transcribing }
        case .idle, .error:
            break
        }
    }

    public func outcome(_ outcome: DictationOutcome) {
        switch outcome {
        case .pasted:
            content = .pasted
            hide(after: Self.pastedDuration)
        case .message(let message):
            content = .message(message)
            hide(after: Self.messageDuration)
        case .dismissed:
            cancelHide()
            content = nil
        }
    }

    private func cancelHide() {
        generation += 1
        pendingHide?.cancel()
        pendingHide = nil
    }

    private func hide(after seconds: TimeInterval) {
        cancelHide()
        let mine = generation
        pendingHide = Task { [weak self, sleep] in
            await sleep(seconds)
            guard let self, !Task.isCancelled, self.generation == mine else { return }
            self.content = nil
            self.pendingHide = nil
        }
    }
}
