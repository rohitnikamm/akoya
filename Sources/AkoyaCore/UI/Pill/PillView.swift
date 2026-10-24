import SwiftUI

/// What the pill window draws, kept by `PillPanelController`: the content (held while the pill hides),
/// whether it is shown, the pop-in under way, and the recording's start and end for the timer.
@MainActor
final class PillDisplay: ObservableObject {
    @Published var content: PillContent = .listening
    /// False while hiding (and once hidden): the pill fades, sinks and shrinks.
    @Published var isShown = false
    /// When the running pop-in began; nil once it has settled.
    @Published var popInStartedAt: Date?
    /// True while the panel is ordered in; nothing animates while it is off screen.
    @Published var isOnScreen = false
    /// Read at each show: the pop-in becomes a plain fade.
    var reduceMotion = false
    var recordingStartedAt = Date()
    /// When transcribing began, so the timer stops at the recording's length.
    var recordingEndedAt: Date?
}

/// The floating dictation pill: a 34 pt glass capsule with a blue (or, for alerts, red) rim glow, showing
/// the live waveform and timer, the transcribing scanner, "Pasted", or a message. Drawn centred in its
/// window, which leaves room around it for the glow.
struct PillView: View {
    static let height: CGFloat = 34
    static let padding: CGFloat = 15
    static let spacing: CGFloat = 10
    /// Width changes between contents (e.g. the waveform shrinking to "Pasted").
    static let resize = Animation.timingCurve(0.2, 0.8, 0.2, 1, duration: 0.22)

    @ObservedObject var display: PillDisplay
    @ObservedObject var model: WaveformModel

    var body: some View {
        // Only the waveform, the scanner, the timer and the pop-in change with time.
        let ticking = display.isOnScreen
            && (display.content == .listening || display.content == .transcribing || display.popInStartedAt != nil)
        TimelineView(.animation(paused: !ticking)) { timeline in
            pill(now: timeline.date)
        }
        .opacity(display.isShown ? 1 : 0)
        .scaleEffect(display.isShown ? 1 : PillHide.scale)
        .offset(y: display.isShown ? 0 : PillHide.sink)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private func pill(now: Date) -> some View {
        let progress = popInProgress(now: now)
        let frame = PillPopIn.frame(progress: progress ?? 1, finalWidth: Self.height, height: Self.height)
        return PillWidthLayout(popInProgress: progress) {
            row(now: now)
                .fixedSize()
                .padding(.horizontal, Self.padding)
                .opacity(frame.contentOpacity)
        }
        .clipShape(Capsule())
        .background(PillBackground(isAlert: isAlert))
        .scaleEffect(x: frame.scaleX, y: frame.scaleY)
        .opacity(frame.opacity * fadeOpacity(now: now))
    }

    private var isAlert: Bool {
        if case .message(let message) = display.content { message.isAlert } else { false }
    }

    /// The pop-in's progress (0...1), or nil when settled or when Reduce Motion fades instead.
    private func popInProgress(now: Date) -> Double? {
        guard let start = display.popInStartedAt, !display.reduceMotion else { return nil }
        return PillPopIn.progress(elapsed: now.timeIntervalSince(start))
    }

    /// The Reduce Motion fade-in.
    private func fadeOpacity(now: Date) -> Double {
        guard let start = display.popInStartedAt, display.reduceMotion else { return 1 }
        return min(1, max(0, now.timeIntervalSince(start) / PillPanelController.reduceMotionFade))
    }

    @ViewBuilder
    private func row(now: Date) -> some View {
        HStack(spacing: Self.spacing) {
            switch display.content {
            case .listening:
                PillWaveform(look: model.look, wave: .listening(model.pillColumns), redTint: model.redTint)
                PillTimer(seconds: now.timeIntervalSince(display.recordingStartedAt))
            case .transcribing:
                let end = display.recordingEndedAt ?? now
                PillWaveform(look: model.look, wave: .scanner(elapsed: now.timeIntervalSince(end)), redTint: model.redTint)
                PillTimer(seconds: end.timeIntervalSince(display.recordingStartedAt))
            case .pasted:
                PillBadge(isAlert: false)
                PillLabel(text: "Pasted")
            case .message(let message):
                if message.isAlert { PillBadge(isAlert: true) }
                PillLabel(text: message.text)
            }
        }
    }
}

/// Sizes the pill to its content, 34 pt tall; during the pop-in the width follows `PillPopIn` towards the
/// content's width, with the content centred at its own size (the capsule clips it).
private struct PillWidthLayout: Layout {
    let popInProgress: Double?

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let natural = max(PillView.height, subviews.first?.sizeThatFits(.unspecified).width ?? 0)
        let width = popInProgress.map {
            PillPopIn.frame(progress: $0, finalWidth: natural, height: PillView.height).width
        } ?? natural
        return CGSize(width: width, height: PillView.height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for subview in subviews {
            subview.place(at: CGPoint(x: bounds.midX, y: bounds.midY), anchor: .center, proposal: .unspecified)
        }
    }
}

/// The pill's colours.
enum PillColors {
    static let dot = Color(hex: 0xDBE6FF)
    static let red = Color(hex: 0xFF6B6B)
    static let amber = AkoyaTheme.sweepAmber
    static let blue = Color(hex: 0x2F6BFF)
    static let alert = Color(hex: 0xEF4444)
}

/// The capsule: dark glass, an inner rim lit from below, and a soft glow around it, from the approved
/// CSS (docs/design/mac-pill-mockup.html, `.pill` and `.pill.alert`).
private struct PillBackground: View {
    let isAlert: Bool

    var body: some View {
        ZStack {
            outerShadows
            Capsule()
                .fill(LinearGradient(
                    colors: [Color(rgba: 14, 18, 34, 0.94), Color(rgba: 9, 13, 30, 0.96)],
                    startPoint: .top, endPoint: .bottom))
            InsetShadows(layers: insetLayers, cornerRadius: PillView.height / 2, style: .circular)
        }
    }

    private var insetLayers: [InsetShadow] {
        isAlert
            ? [
                InsetShadow(blur: 0, spread: 1, color: Color(rgba: 255, 150, 150, 0.22)),
                InsetShadow(y: -8, blur: 14, spread: -7, color: Color(rgba: 239, 68, 68, 0.85)),
                InsetShadow(y: -2, blur: 3, spread: -1, color: Color(rgba: 255, 170, 170, 0.85)),
            ]
            : [
                InsetShadow(blur: 0, spread: 1, color: Color(rgba: 140, 170, 255, 0.18)),
                InsetShadow(y: -8, blur: 14, spread: -7, color: Color(rgba: 47, 107, 255, 0.85)),
                InsetShadow(y: -2, blur: 3, spread: -1, color: Color(rgba: 150, 185, 255, 0.9)),
            ]
    }

    /// CSS `0 12px 40px` glow over `0 8px 24px` black, drawn only outside the capsule as CSS draws
    /// outer box-shadows (the glass is slightly see-through). Shadow radius is half the CSS blur.
    private var outerShadows: some View {
        let margin = PillPanelController.glowMargin
        let glow = isAlert ? Color(rgba: 239, 68, 68, 0.28) : Color(rgba: 47, 107, 255, 0.32)
        return ZStack {
            Capsule().fill(.black).shadow(color: .black.opacity(0.5), radius: 12, x: 0, y: 8)
            Capsule().fill(.black).shadow(color: glow, radius: 20, x: 0, y: 12)
        }
        .padding(margin)
        .mask(OutsideCapsule(inset: margin).fill(style: FillStyle(eoFill: true)))
        .padding(-margin)
    }
}

/// The rect minus a capsule inset by `inset` (fill with `eoFill`).
private struct OutsideCapsule: Shape {
    let inset: CGFloat

    func path(in rect: CGRect) -> Path {
        var path = Path(rect)
        path.addPath(Capsule().path(in: rect.insetBy(dx: inset, dy: inset)))
        return path
    }
}

/// The 23 x 4 dot waveform (Pixel) or its bars (Solid).
private struct PillWaveform: View {
    enum Wave {
        case listening([Int])
        case scanner(elapsed: TimeInterval)
    }

    let look: WaveLook
    let wave: Wave
    let redTint: Bool

    private static let pitch = PillDots.dotSize + PillDots.gap

    var body: some View {
        Canvas { context, _ in
            switch look {
            case .pixel: drawDots(into: context)
            case .solid: drawBars(into: context)
            }
        }
        .frame(width: CGFloat(PillDots.columns) * Self.pitch - PillDots.gap, height: PillDots.maxBarHeight)
    }

    private func drawDots(into context: GraphicsContext) {
        let dots: [Double]
        switch wave {
        case .listening(let steps): dots = PillDots.listening(steps)
        case .scanner(let elapsed): dots = PillDots.scanner(elapsed: max(0, elapsed))
        }
        let size = PillDots.dotSize
        for (i, opacity) in dots.enumerated() {
            let column = i / PillDots.rows, row = i % PillDots.rows
            let rect = CGRect(x: CGFloat(column) * Self.pitch, y: CGFloat(row) * Self.pitch, width: size, height: size)
            let middle = row == 1 || row == 2
            context.fill(Path(ellipseIn: rect), with: .color(color(lit: middle && PillDots.isScannerLit(opacity)).opacity(opacity)))
        }
    }

    private func drawBars(into context: GraphicsContext) {
        let bars: [PillBar]
        switch wave {
        case .listening(let steps): bars = PillDots.solidListening(steps)
        case .scanner(let elapsed): bars = PillDots.solidScanner(elapsed: max(0, elapsed))
        }
        let size = PillDots.dotSize
        for (column, bar) in bars.enumerated() {
            let rect = CGRect(x: CGFloat(column) * Self.pitch, y: (PillDots.maxBarHeight - bar.height) / 2,
                              width: size, height: bar.height)
            let path = Path(roundedRect: rect, cornerRadius: size / 2)
            context.fill(path, with: .color(color(lit: PillDots.isScannerLit(bar.opacity)).opacity(bar.opacity)))
        }
    }

    /// Red while recording turns the live waveform red and the scanner's lit columns amber.
    private func color(lit: Bool) -> Color {
        guard redTint else { return PillColors.dot }
        switch wave {
        case .listening: return PillColors.red
        case .scanner: return lit ? PillColors.amber : PillColors.dot
        }
    }
}

/// Recording length as "m:ss".
private struct PillTimer: View {
    let seconds: TimeInterval

    var body: some View {
        let whole = max(0, Int(seconds))
        Text(String(format: "%d:%02d", whole / 60, whole % 60))
            .font(AkoyaFonts.outfit(11.5).monospacedDigit())
            .foregroundStyle(.white.opacity(0.5))
    }
}

private struct PillLabel: View {
    let text: String

    var body: some View {
        Text(text)
            .font(AkoyaFonts.outfit(13.5))
            .foregroundStyle(.white.opacity(0.85))
            .lineLimit(1)
    }
}

/// An 18 pt glowing disc: a blue check for "Pasted", a red "!" for alerts.
private struct PillBadge: View {
    let isAlert: Bool

    var body: some View {
        ZStack {
            Circle()
                .fill(isAlert ? PillColors.alert : PillColors.blue)
                .shadow(color: isAlert ? Color(rgba: 239, 68, 68, 0.7) : Color(rgba: 47, 107, 255, 0.8), radius: 6)
            if isAlert {
                Text("!")
                    .font(AkoyaFonts.outfit(12, .bold))
                    .foregroundStyle(.white)
            } else {
                Check()
                    .stroke(.white, style: StrokeStyle(lineWidth: 1.6, lineCap: .round, lineJoin: .round))
                    .frame(width: 10, height: 10)
            }
        }
        .frame(width: 18, height: 18)
    }

    /// The mockup's `M2 5.2 4.1 7.3 8 3` in a 10 x 10 box.
    private struct Check: Shape {
        func path(in rect: CGRect) -> Path {
            let s = rect.width / 10
            var path = Path()
            path.move(to: CGPoint(x: rect.minX + 2 * s, y: rect.minY + 5.2 * s))
            path.addLine(to: CGPoint(x: rect.minX + 4.1 * s, y: rect.minY + 7.3 * s))
            path.addLine(to: CGPoint(x: rect.minX + 8 * s, y: rect.minY + 3 * s))
            return path
        }
    }
}
