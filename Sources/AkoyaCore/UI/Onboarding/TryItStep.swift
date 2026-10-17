import Combine
import SwiftUI

/// What the practice card shows: the live (then final) transcript, whether Done is ready,
/// and the "try again" hint after a missed take. Fed by practice-mode text and phase changes.
struct TryItTranscript: Equatable {
    private(set) var text = ""
    /// A non-empty final transcript has arrived at least once; Done becomes primary.
    private(set) var hasFinal = false
    /// The last recording produced nothing (silence, an empty or failed transcription).
    private(set) var showsRetryHint = false
    /// A recording started and has not ended in a result yet.
    private var awaitingResult = false

    mutating func receive(_ text: String, isFinal: Bool) {
        self.text = text
        if isFinal, !text.isEmpty {
            hasFinal = true
            awaitingResult = false
        }
    }

    mutating func phaseChanged(to phase: DictationPhase) {
        switch phase {
        case .recording:
            awaitingResult = true
            showsRetryHint = false
        case .transcribing:
            break
        case .error:
            // An error cue after a recording means the take was lost, and a failed final pass
            // never reports text, so drop the partial rather than leave it looking final. An
            // error without a recording (a press while the model loads) is not a missed take.
            if awaitingResult {
                text = ""
                showsRetryHint = true
            }
            awaitingResult = false
        case .idle:
            awaitingResult = false
        }
    }
}

/// Routes practice dictations into the try-it card while it is showing.
@MainActor
final class TryItSession: ObservableObject {
    @Published private(set) var transcript = TryItTranscript()

    private let coordinator: AppCoordinator
    private var phaseSubscription: AnyCancellable?

    init(coordinator: AppCoordinator, model: WaveformModel) {
        self.coordinator = coordinator
        // `@Published` emits in willSet, so use the delivered phase, not `model.phase`.
        phaseSubscription = model.$phase.sink { [weak self] phase in
            self?.transcript.phaseChanged(to: phase)
        }
    }

    /// Switches dictation to practice mode: text lands in this card instead of being pasted.
    /// The onboarding controller switches back to `.paste` however the window closes.
    func begin() {
        // The subscription also sees paste-mode takes made before this step, so drop any
        // retry hint or transcript left over from them.
        transcript = TryItTranscript()
        coordinator.destination = .practice { [weak self] text, isFinal in
            self?.transcript.receive(text, isFinal: isFinal)
        }
    }
}

/// Step 3: hold the hotkey and see the words appear, with the rim glow and grid following the
/// voice (docs/design/onboarding-screen-3-tryit-mockup.html, blue-glow-target.png).
struct TryItStep: View {
    @ObservedObject var session: TryItSession
    @ObservedObject var model: WaveformModel
    @StateObject private var glow: VoiceGlowDriver
    let hotkeyLabel: String
    let mode: DictationMode
    let modelState: ModelState
    let retryModel: () -> Void
    let done: () -> Void
    let skip: () -> Void

    init(
        session: TryItSession, model: WaveformModel, hotkeyLabel: String, mode: DictationMode,
        modelState: ModelState, retryModel: @escaping () -> Void, done: @escaping () -> Void,
        skip: @escaping () -> Void
    ) {
        self.session = session
        self.model = model
        self.hotkeyLabel = hotkeyLabel
        self.mode = mode
        self.modelState = modelState
        self.retryModel = retryModel
        self.done = done
        self.skip = skip
        _glow = StateObject(wrappedValue: VoiceGlowDriver(model: model))
    }

    var body: some View {
        let transcript = session.transcript
        VStack(spacing: 18) {
            Text("TRY IT")
                .font(AkoyaFonts.outfit(12, .medium))
                .kerning(0.96)
                .foregroundStyle(AkoyaTheme.textEyebrow)
            HotkeyHint(verb: mode == .hold ? "Hold" : "Tap", keycap: hotkeyLabel)
                // The coordinator ignores presses until the model is ready.
                .opacity(modelState == .ready ? 1 : 0.4)
            TryItWaveform(columns: model.columns, live: model.phase == .recording)
                .padding(.top, 8)
            TranscriptBox(transcript: transcript)
            Spacer(minLength: 0)
            if let line = modelState.onboardingStatusLine {
                ModelStatusLine(text: line, showsRetry: modelState.onboardingShowsRetry, retry: retryModel)
            }
            // Quiet and inert until the first transcript (not `.disabled`, which would dim it
            // below the approved quiet look).
            OnboardingButton(title: "Done", style: transcript.hasFinal ? .light : .quiet) {
                if transcript.hasFinal { done() }
            }
            .accessibilityHint(transcript.hasFinal ? "" : "Available after your first dictation")
            // Return finishes once Done is the primary button.
            .keyboardShortcut(transcript.hasFinal ? .defaultAction : nil)
            PageDots(current: OnboardingStep.tryIt.pageIndex)
                .frame(maxWidth: .infinity)
                .overlay(alignment: .trailing) {
                    Button("Skip", action: skip)
                        .buttonStyle(.plain)
                        .font(AkoyaFonts.outfit(13))
                        .foregroundStyle(AkoyaTheme.textEyebrow)
                }
        }
        .padding(EdgeInsets(top: 40, leading: 32, bottom: 28, trailing: 32))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        // Grid above the glow, both above the card's V1 fill; the glow is not built at rest.
        .akoyaGrid(opacity: 0.4 + 0.6 * RimGlowScale.layerOpacity(intensity: glow.opacity))
        .background {
            if glow.opacity > 0 {
                Color.clear.akoyaRimGlow(intensity: glow.opacity, cornerRadius: AkoyaTheme.window, scale: .card)
            }
        }
        .onAppear { session.begin() }
    }
}

/// "Hold [Right ⌥] and say anything".
private struct HotkeyHint: View {
    let verb: String
    let keycap: String

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        // Word spacing either side of the keycap, as in the inline blue target.
        HStack(spacing: 4) {
            Text(verb)
            Text(keycap)
                .font(AkoyaFonts.outfit(14, .semibold))
                .foregroundStyle(AkoyaTheme.textPrimary)
                // CSS padding 3px 10px plus the 1px border. The keycap is inline in the
                // mockup, so its vertical padding overhangs the line instead of growing it.
                .padding(.horizontal, 11)
                .background {
                    shape.fill(AkoyaTheme.keycapFill)
                        .overlay(shape.strokeBorder(AkoyaTheme.keycapBorder, lineWidth: 1))
                        .padding(.vertical, -4)
                }
            Text("and say anything")
        }
        .font(AkoyaFonts.outfit(15))
        .foregroundStyle(AkoyaTheme.textTryItHint)
        .accessibilityElement(children: .combine)
    }
}

/// The live dot waveform: one column of 5 pt dots per level-meter column, brighter while
/// recording.
struct TryItWaveform: View {
    let columns: [Int]
    let live: Bool

    /// Tallest column: 9 dots (69 pt) fit the 70 pt row. The mockup's 13 overflow it and
    /// would run into the transcript card.
    nonisolated static let maxDots = 9

    /// Dots for a level step (1…8): 1…9, always odd so the column stays centred.
    nonisolated static func dotCount(step: Int) -> Int {
        let level = Double(min(max(step, 1), 8) - 1) / 7
        return max(1, Int((level * Double(maxDots)).rounded())) | 1
    }

    var body: some View {
        HStack(alignment: .center, spacing: 5) {
            ForEach(Array(columns.enumerated()), id: \.offset) { _, step in
                VStack(spacing: 3) {
                    ForEach(0..<Self.dotCount(step: step), id: \.self) { _ in
                        Circle().fill(AkoyaTheme.accentLightSoft).frame(width: 5, height: 5)
                    }
                }
            }
        }
        .opacity(live ? 1 : 0.45)
        .frame(height: 70)
        .accessibilityHidden(true)
    }
}

/// The card the words appear in.
private struct TranscriptBox: View {
    let transcript: TryItTranscript

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 22, style: .continuous)
        Group {
            if !transcript.text.isEmpty {
                Text(transcript.text).foregroundStyle(AkoyaTheme.textPrimary)
            } else if transcript.showsRetryHint {
                Text("Didn’t catch that — try again").foregroundStyle(AkoyaTheme.textOnboardingBody)
            } else {
                Text("Your words will appear here.").foregroundStyle(AkoyaTheme.textPlaceholder)
            }
        }
        .font(AkoyaFonts.outfit(16))
        // CSS line-height 1.45 (23.2 pt lines against Outfit's 20.2): the extra leading goes
        // between lines and, halved, above the first and below the last.
        .lineSpacing(3)
        .padding(.vertical, 1.5)
        .lineLimit(6)
        .truncationMode(.head)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .padding(.vertical, 14)
        .padding(.horizontal, 16)
        .frame(minHeight: 96, alignment: .topLeading)
        .background(AkoyaTheme.inner, in: shape)
        .animation(nil, value: transcript.text)
    }
}
