import AppKit
import Combine
import SwiftUI

/// What the dropdown's controls do. The app supplies these; the view only calls them.
public struct DropdownActions {
    public var openSettings: () -> Void
    public var quit: () -> Void
    public var retryModel: () -> Void
    public var finishSetup: () -> Void
    public var copyLast: (String) -> Void

    public init(
        openSettings: @escaping () -> Void, quit: @escaping () -> Void,
        retryModel: @escaping () -> Void, finishSetup: @escaping () -> Void,
        copyLast: @escaping (String) -> Void
    ) {
        self.openSettings = openSettings
        self.quit = quit
        self.retryModel = retryModel
        self.finishSetup = finishSetup
        self.copyLast = copyLast
    }
}

/// The menu bar dropdown (docs/design/dropdown-mockup.html): status card, mode switch, last
/// dictation, and a footer with Settings and Quit. Dark only, 320 pt wide, height to content.
public struct DropdownView: View {
    static let width: CGFloat = 320

    @ObservedObject private var model: WaveformModel
    @ObservedObject private var settings: AkoyaSettings
    @StateObject private var glow: StatusGlowDriver
    private let modelState: ModelState
    private let permissionsGranted: Bool
    private let actions: DropdownActions

    public init(
        model: WaveformModel, settings: AkoyaSettings, modelState: ModelState,
        permissionsGranted: Bool, actions: DropdownActions
    ) {
        self.model = model
        self.settings = settings
        self.modelState = modelState
        self.permissionsGranted = permissionsGranted
        self.actions = actions
        _glow = StateObject(wrappedValue: StatusGlowDriver(model: model))
    }

    public var body: some View {
        VStack(spacing: 12) {
            StatusCard(
                state: .from(phase: model.phase, model: modelState, permissionsGranted: permissionsGranted),
                hotkey: settings.hotkey, mode: settings.mode, glowOpacity: glow.opacity,
                retry: actions.retryModel, finishSetup: actions.finishSetup)
            ModeSwitch(selection: $settings.mode)
            if let text = model.lastDictation {
                LastDictationCard(text: text, copy: actions.copyLast)
            }
            Footer(openSettings: actions.openSettings, quit: actions.quit)
        }
        .padding(14)
        .frame(width: Self.width)
        .background {
            ZStack {
                LinearGradient(
                    colors: [AkoyaTheme.panelTop, AkoyaTheme.panelBottom], startPoint: .top, endPoint: .bottom)
                InsetShadows(
                    layers: [InsetShadow(y: 1, blur: 0, spread: 0, color: AkoyaTheme.topHighlight)],
                    cornerRadius: AkoyaTheme.panel)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: AkoyaTheme.panel, style: .continuous))
        .environment(\.colorScheme, .dark)
        .focusEffectDisabled()
    }
}

// MARK: - Status card

private struct StatusCard: View {
    let state: StatusCardState
    let hotkey: HotkeyKey
    let mode: DictationMode
    /// Rim glow opacity (0 at rest); see `StatusGlowDriver`.
    let glowOpacity: Double
    let retry: () -> Void
    let finishSetup: () -> Void

    var body: some View {
        VStack(spacing: 12) {
            HStack {
                Text("akoya")
                    .font(AkoyaFonts.outfit(14, .semibold))
                    .kerning(0.14)
                    .foregroundStyle(AkoyaTheme.textPrimary)
                Spacer()
                StatusChip(title: state.chipTitle)
            }
            DotLogo(dotSize: 6)
                .frame(height: 56)
            HStack(spacing: 8) {
                Text(mode == .hold ? "Hold" : "Tap")
                Keycap(label: hotkey.keycapLabel)
                Text("to talk")
            }
            .font(AkoyaFonts.outfit(13))
            .foregroundStyle(AkoyaTheme.textHint)
            .accessibilityElement(children: .combine)
            switch state {
            case .downloading(let fraction?):
                DownloadProgress(fraction: fraction)
            case .modelFailed:
                PrimaryButton(title: "Retry", action: retry)
            case .setupNeeded:
                PrimaryButton(title: "Finish setup", action: finishSetup)
            case .ready, .listening, .transcribing, .downloading(nil):
                EmptyView()
            }
        }
        .padding(EdgeInsets(top: 18, leading: 16, bottom: 16, trailing: 16))
        .frame(maxWidth: .infinity)
        .akoyaDotGrid(pitch: 10, opacity: 0.08)
        // Layers, bottom to top: V1 card, navy base (cross-fades in with the glow), rim glow,
        // dot grid, content. Every kit modifier draws as a background, so the glow and the
        // navy base are applied before `akoyaV1Card` to sit above its opaque fill. Neither is
        // built at rest.
        .background {
            if glowOpacity > 0 {
                Color.clear.akoyaRimGlow(intensity: glowOpacity, cornerRadius: AkoyaTheme.card, scale: .compact)
            }
        }
        .background {
            if glowOpacity > 0 {
                // Fully navy once the glow reaches its listening floor (0.2).
                AkoyaTheme.glowBase.opacity(min(1, glowOpacity / 0.2))
            }
        }
        .akoyaV1Card(cornerRadius: AkoyaTheme.card)
    }
}

private struct StatusChip: View {
    let title: String

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(AkoyaTheme.lavender)
                .frame(width: 6, height: 6)
                .shadow(color: AkoyaTheme.chipGlow, radius: 4)
            Text(title)
                .font(AkoyaFonts.outfit(11))
                .foregroundStyle(AkoyaTheme.textHint)
                .monospacedDigit()
        }
        .accessibilityElement(children: .combine)
    }
}

private struct Keycap: View {
    let label: String

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 7, style: .continuous)
        Text(label)
            .font(AkoyaFonts.outfit(12, .semibold))
            .foregroundStyle(AkoyaTheme.textPrimary)
            // CSS padding 2px 9px plus the 1px border.
            .padding(.vertical, 3)
            .padding(.horizontal, 10)
            .background(AkoyaTheme.keycapFill, in: shape)
            .overlay(shape.strokeBorder(AkoyaTheme.keycapBorder, lineWidth: 1))
    }
}

private struct DownloadProgress: View {
    let fraction: Double

    var body: some View {
        Capsule()
            .fill(AkoyaTheme.progressTrack)
            .overlay(alignment: .leading) {
                GeometryReader { proxy in
                    Capsule()
                        .fill(AkoyaTheme.lavender)
                        .frame(width: proxy.size.width * min(max(fraction, 0), 1))
                }
            }
            .frame(width: 180, height: 3)
            .accessibilityElement()
            .accessibilityLabel("Model download")
            .accessibilityValue("\(Int((min(max(fraction, 0), 1) * 100).rounded())) percent")
    }
}

private struct PrimaryButton: View {
    let title: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(AkoyaFonts.outfit(13, .semibold))
                .foregroundStyle(AkoyaTheme.textPrimary)
                .padding(.vertical, 7)
                .padding(.horizontal, 18)
                .background(IndigoGradient(), in: Capsule())
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }
}

private struct IndigoGradient: ShapeStyle {
    func resolve(in environment: EnvironmentValues) -> LinearGradient {
        LinearGradient(colors: [AkoyaTheme.indigoTop, AkoyaTheme.indigoBottom], startPoint: .top, endPoint: .bottom)
    }
}

// MARK: - Mode switch

private struct ModeSwitch: View {
    @Binding var selection: DictationMode

    var body: some View {
        HStack(spacing: 4) {
            ForEach(DictationMode.allCases, id: \.self) { mode in
                let selected = mode == selection
                Button {
                    selection = mode
                } label: {
                    Text(mode.segmentTitle)
                        .font(AkoyaFonts.outfit(13, selected ? .semibold : .regular))
                        .foregroundStyle(selected ? AkoyaTheme.textPrimary : AkoyaTheme.textMuted)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 7)
                        .background {
                            if selected { Capsule().fill(IndigoGradient()) }
                        }
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(selected ? .isSelected : [])
            }
        }
        .padding(4)
        .background(AkoyaTheme.pill, in: Capsule())
    }
}

// MARK: - Last dictation

private struct LastDictationCard: View {
    let text: String
    let copy: (String) -> Void
    @StateObject private var feedback = CopyFeedback()

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 20, style: .continuous)
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("LAST DICTATION")
                    .font(AkoyaFonts.outfit(11, .medium))
                    .kerning(0.44)
                    .foregroundStyle(AkoyaTheme.textCaption)
                Spacer()
                Button(feedback.isShowing ? "Copied" : "Copy") {
                    copy(text)
                    feedback.flash()
                }
                .font(AkoyaFonts.outfit(11))
                .foregroundStyle(AkoyaTheme.lavender)
                .buttonStyle(.plain)
                .accessibilityLabel("Copy last dictation")
            }
            Text(text)
                .font(AkoyaFonts.outfit(13))
                .foregroundStyle(AkoyaTheme.textBody)
                // CSS line-height 1.4 (18.2 pt lines): the extra leading goes between lines
                // and, halved, above the first and below the last.
                .lineSpacing(2.3)
                .padding(.vertical, 0.7)
                .lineLimit(4)
                .frame(maxWidth: .infinity, alignment: .leading)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.vertical, 12)
        .padding(.horizontal, 14)
        .background(
            LinearGradient(
                colors: [AkoyaTheme.innerCardTop, AkoyaTheme.innerCardBottom], startPoint: .top, endPoint: .bottom),
            in: shape)
        .overlay(shape.strokeBorder(AkoyaTheme.hairline, lineWidth: 1))
    }
}

/// Shows "Copied" for a moment after Copy. A class rather than `@State`: the `@State` macro
/// plugin is missing from the Command Line Tools toolchain.
@MainActor
private final class CopyFeedback: ObservableObject {
    @Published private(set) var isShowing = false
    private var resetTask: Task<Void, Never>?

    func flash() {
        isShowing = true
        resetTask?.cancel()
        resetTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(1.5))
            guard !Task.isCancelled else { return }
            self?.isShowing = false
        }
    }
}

// MARK: - Footer

private struct Footer: View {
    let openSettings: () -> Void
    let quit: () -> Void

    var body: some View {
        HStack(spacing: 8) {
            Button(action: openSettings) {
                Text("Settings")
                    .font(AkoyaFonts.outfit(13))
                    .foregroundStyle(AkoyaTheme.textControl)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 9)
                    .background(AkoyaTheme.pill, in: Capsule())
                    .contentShape(Capsule())
            }
            .buttonStyle(.plain)
            Button(action: quit) {
                PowerGlyph()
                    .stroke(AkoyaTheme.quitGlyph, style: StrokeStyle(lineWidth: 1.6, lineCap: .round))
                    .frame(width: 14, height: 14)
                    .frame(width: 36, height: 36)
                    .background(AkoyaTheme.quitFill, in: Circle())
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Quit Akoya")
        }
    }
}

/// The mockup's 14×14 power icon: `M7 1.8v4.6` and `M4.2 3.6a4.6 4.6 0 1 0 5.6 0`.
private struct PowerGlyph: Shape {
    func path(in rect: CGRect) -> Path {
        let unit = min(rect.width, rect.height) / 14
        func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint {
            CGPoint(x: rect.minX + x * unit, y: rect.minY + y * unit)
        }
        var path = Path()
        path.move(to: point(7, 1.8))
        path.addLine(to: point(7, 6.4))
        // The arc's ends sit at (4.2, 3.6) and (9.8, 3.6); its centre is below the chord.
        let radius: CGFloat = 4.6
        let drop = (radius * radius - 2.8 * 2.8).squareRoot()
        let center = point(7, 3.6 + drop)
        let startAngle = Angle(radians: atan2(-drop, -2.8))
        let endAngle = Angle(radians: atan2(-drop, 2.8))
        // The long way round, through the bottom.
        path.move(to: point(4.2, 3.6))
        path.addArc(center: center, radius: radius * unit, startAngle: startAngle, endAngle: endAngle, clockwise: true)
        return path
    }
}

// MARK: - Labels

extension DictationMode {
    /// Segment titles in the dropdown's mode switch.
    fileprivate var segmentTitle: String {
        switch self {
        case .hold: "Hold to talk"
        case .toggle: "Tap to toggle"
        }
    }
}

extension HotkeyKey {
    /// Short keycap label, e.g. "Right ⌘" (the mockup's form of `displayName`).
    fileprivate var keycapLabel: String {
        switch self {
        case .rightOption: "Right ⌥"
        case .rightCommand: "Right ⌘"
        case .rightControl: "Right ⌃"
        }
    }
}

// MARK: - Glow driver

/// Smooths the status card's rim-glow opacity with `GlowEnvelope` at about 60 Hz, ticking only
/// while recording and while the glow fades out afterwards. Starts settled at the model's
/// current state, so a panel opened mid-recording glows at once.
@MainActor
final class StatusGlowDriver: ObservableObject {
    private static let tickInterval: TimeInterval = 1.0 / 60.0
    /// Below this the fade-out is finished and the glow is dropped.
    private static let restThreshold = 0.002

    @Published private(set) var opacity: Double = 0

    private weak var model: WaveformModel?
    private var envelope = GlowEnvelope()
    private var timer: Timer?
    private var lastTick = Date()
    private var phaseSubscription: AnyCancellable?

    init(model: WaveformModel) {
        self.model = model
        // A step far longer than either time constant lands on the target.
        opacity = envelope.step(target: Self.target(for: model), dt: 60)
        // `@Published` emits in willSet, so use the delivered phase, not `model.phase`.
        phaseSubscription = model.$phase.sink { [weak self] phase in
            if phase == .recording { self?.startTicking() }
        }
    }

    private static func target(for model: WaveformModel) -> Double {
        GlowEnvelope.opacity(
            level: model.voiceLevel, recording: model.phase == .recording,
            reduceMotion: NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
    }

    private func startTicking() {
        guard timer == nil else { return }
        lastTick = Date()
        let timer = Timer(timeInterval: Self.tickInterval, repeats: true) { [weak self] timer in
            // Stop when the panel's view (and this driver with it) has gone.
            guard let self else { return timer.invalidate() }
            MainActor.assumeIsolated { self.tick() }
        }
        // .common so the glow keeps moving during event tracking.
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func tick() {
        guard let model else { return stopTicking() }
        let now = Date()
        let dt = now.timeIntervalSince(lastTick)
        lastTick = now
        let target = Self.target(for: model)
        // Reduce Motion: a fixed glow while recording, switched rather than breathing.
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        var value = envelope.step(target: target, dt: reduceMotion ? 60 : dt)
        if target == 0, value < Self.restThreshold {
            value = envelope.step(target: 0, dt: 60)
            stopTicking()
        }
        if value != opacity { opacity = value }
    }

    private func stopTicking() {
        timer?.invalidate()
        timer = nil
    }
}
