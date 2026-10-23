import SwiftUI

/// Observable state behind the menu bar item. The coordinator mutates it; `WaveformView`
/// and `StatusItemController` observe it.
@MainActor
public final class WaveformModel: ObservableObject {
    @Published public var phase: DictationPhase = .idle {
        didSet { if phase != oldValue { phaseEnteredAt = Date() } }
    }
    @Published public var columns: [Int] = Array(repeating: 1, count: 22)
    @Published public var look: WaveLook = .pixel
    @Published public var redTint = false
    @Published public var hideIdleIcon = false
    /// Where dictation shows. Defaults to `.menuBar` so the model alone behaves as it always
    /// did; the app binds `AkoyaSettings.indicator` (default `.pill`) to it.
    @Published public var indicator: DictationIndicator = .menuBar
    /// Set by `StatusItemController` while the item cross-fades from one content to another;
    /// nil otherwise.
    @Published public var fade: ContentFade?
    /// Current voice loudness, 0...1 (`(level step - 1) / 7` of the latest RMS); 0 when not
    /// recording. Drives the onboarding rim glow.
    @Published public var voiceLevel: Double = 0
    /// The text of the last successful paste-mode dictation. In memory only.
    @Published public var lastDictation: String?

    /// When `phase` last changed. Drives the scanner sweep and the error flash.
    /// Deliberately not `@Published`: it only matters while the timeline is ticking.
    public private(set) var phaseEnteredAt = Date()

    public init() {}
}

/// A cross-fade in progress: `from` fades out as the current phase's content fades in.
public struct ContentFade: Equatable, Sendable {
    public var from: StatusItemContent
    /// 0 shows only `from`, 1 only the new content.
    public var progress: Double
    /// When the outgoing phase began, so a fading scanner keeps its sweep position.
    public var fromPhaseEnteredAt: Date

    public init(from: StatusItemContent, progress: Double, fromPhaseEnteredAt: Date) {
        self.from = from
        self.progress = progress
        self.fromPhaseEnteredAt = fromPhaseEnteredAt
    }
}

/// Colours the menu bar item draws in, besides the error flash.
public enum WaveTint: Equatable, Sendable {
    case primary, red, amber

    /// Amber `#FBBF24`, the Windows pill's transcribing colour.
    static let amberColor = Color(red: 0xFB / 255, green: 0xBF / 255, blue: 0x24 / 255)

    var color: Color {
        switch self {
        case .primary: .primary
        case .red: .red
        case .amber: Self.amberColor
        }
    }
}

/// Pure rectangle layout for the waveform, in a canvas whose origin is top-left.
/// Kept free of SwiftUI drawing so it can be unit tested.
public enum WaveformLayout {
    public static let glyphSize: CGFloat = 16
    public static let idleBarHeights: [CGFloat] = [4, 8, 12, 8, 4]
    /// Transcribing columns are two cells tall.
    public static let scannerCells = 2
    /// How long the error glyph stays red after entering `.error`.
    public static let errorFlashDuration: TimeInterval = 0.6

    /// The colour for `content`. "Red while recording" also turns the transcribing sweep amber,
    /// matching the Windows pill; the idle glyph stays the menu bar colour.
    public static func tint(for content: StatusItemContent, redTint: Bool) -> WaveTint {
        switch content {
        case .waveform: redTint ? .red : .primary
        case .scanner: redTint ? .amber : .primary
        case .none, .glyph: .primary
        }
    }

    /// The five-bar idle glyph: 16 pt wide, bars 2 pt wide spread evenly, centered in `size`.
    public static func idleBarRects(in size: CGSize) -> [CGRect] {
        let barWidth = WaveformGeometry.columnWidth
        let pitch = (glyphSize - barWidth) / CGFloat(idleBarHeights.count - 1)
        let originX = (size.width - glyphSize) / 2
        return idleBarHeights.enumerated().map { i, h in
            CGRect(x: originX + CGFloat(i) * pitch, y: (size.height - h) / 2, width: barWidth, height: h)
        }
    }

    /// Left edge of column `index` when `count` columns are centered in `width`.
    public static func columnX(index: Int, count: Int, width: CGFloat) -> CGFloat {
        let pitch = WaveformGeometry.columnWidth + WaveformGeometry.gap
        let total = CGFloat(count) * pitch - WaveformGeometry.gap
        return (width - total) / 2 + CGFloat(index) * pitch
    }

    /// Rectangles making up one recording column, vertically centered in `size`.
    /// Pixel look: stacked cells (2 pt, 1 pt gap). Solid look: a single bar.
    public static func columnRects(index: Int, count: Int, level: Int, look: WaveLook, in size: CGSize) -> [CGRect] {
        let x = columnX(index: index, count: count, width: size.width)
        switch look {
        case .pixel:
            return cellRects(x: x, cells: WaveformGeometry.pixelCells(level: level), midY: size.height / 2)
        case .solid:
            return [barRect(x: x, height: WaveformGeometry.columnHeight(level: level, look: .solid), midY: size.height / 2)]
        }
    }

    /// Rectangles making up one transcribing column: two cells tall (5 pt) in either look.
    public static func scannerRects(index: Int, count: Int, look: WaveLook, in size: CGSize) -> [CGRect] {
        let x = columnX(index: index, count: count, width: size.width)
        switch look {
        case .pixel:
            return cellRects(x: x, cells: scannerCells, midY: size.height / 2)
        case .solid:
            let height = CGFloat(scannerCells) * WaveformGeometry.cell + CGFloat(scannerCells - 1) * WaveformGeometry.gap
            return [barRect(x: x, height: height, midY: size.height / 2)]
        }
    }

    /// Left edge of content laid out at `naturalWidth` and pinned to the trailing edge of an
    /// item `width` wide. Negative while the item is narrower than the content (the overflow
    /// is clipped at the leading edge, so the content reveals as the item grows).
    public static func trailingOriginX(naturalWidth: CGFloat, in width: CGFloat) -> CGFloat {
        width - naturalWidth
    }

    /// The expand/collapse easing, CSS `cubic-bezier(0.2, 0.8, 0.2, 1)`. `t` is clamped to 0...1.
    public static func expandEase(_ t: Double) -> Double {
        cubicBezier(x1: 0.2, y1: 0.8, x2: 0.2, y2: 1, at: t)
    }

    /// CSS-style cubic-bezier timing function through (0,0), (x1,y1), (x2,y2), (1,1): solves
    /// x(s) = t for the curve parameter, then returns y(s). `t` is clamped to 0...1; x1 and x2
    /// must be in 0...1 so x(s) is monotonic.
    public static func cubicBezier(x1: Double, y1: Double, x2: Double, y2: Double, at t: Double) -> Double {
        let t = min(1, max(0, t))
        if t == 0 || t == 1 { return t }
        // Polynomial coefficients for x(s) and y(s) = a s^3 + b s^2 + c s.
        let cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx
        let cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by
        func x(_ s: Double) -> Double { ((ax * s + bx) * s + cx) * s }
        func dx(_ s: Double) -> Double { (3 * ax * s + 2 * bx) * s + cx }
        // Newton's method first; fall back to bisection where the slope is too flat.
        var s = t
        for _ in 0..<8 {
            let error = x(s) - t
            if abs(error) < 1e-7 { return ((ay * s + by) * s + cy) * s }
            let slope = dx(s)
            if abs(slope) < 1e-6 { break }
            s -= error / slope
        }
        var lo = 0.0, hi = 1.0
        s = t
        for _ in 0..<60 {
            let value = x(s)
            if abs(value - t) < 1e-7 { break }
            if value < t { lo = s } else { hi = s }
            s = (lo + hi) / 2
        }
        return ((ay * s + by) * s + cy) * s
    }

    /// Item length partway through an expand/collapse with `expandEase`, rounded to whole
    /// points so the content never lands on fractional positions. `progress` is clamped to 0...1.
    public static func tweenLength(from: CGFloat, to: CGFloat, progress: Double) -> CGFloat {
        let eased = expandEase(progress)
        return (from + (to - from) * CGFloat(eased)).rounded()
    }

    private static func cellRects(x: CGFloat, cells: Int, midY: CGFloat) -> [CGRect] {
        let cell = WaveformGeometry.cell, gap = WaveformGeometry.gap
        let total = CGFloat(cells) * cell + CGFloat(cells - 1) * gap
        let top = midY - total / 2
        return (0..<cells).map { i in
            CGRect(x: x, y: top + CGFloat(i) * (cell + gap), width: WaveformGeometry.columnWidth, height: cell)
        }
    }

    private static func barRect(x: CGFloat, height: CGFloat, midY: CGFloat) -> CGRect {
        CGRect(x: x, y: midY - height / 2, width: WaveformGeometry.columnWidth, height: height)
    }
}

/// Timing for one change of the menu bar item: the length tween (when the width changes and
/// motion is allowed) and the content cross-fade. Pure, so frames can be computed in tests.
public struct ItemTween: Equatable, Sendable {
    /// Expand/collapse duration.
    public static let resizeDuration: TimeInterval = 0.22
    /// The cross-fade runs over this leading fraction of a resize.
    public static let resizeFadeFraction = 0.35
    /// Cross-fade length when the width does not animate (same width, or Reduce Motion).
    public static let fadeOnlyDuration: TimeInterval = 0.12

    public let fromLength: CGFloat
    public let toLength: CGFloat
    /// True when the length animates; false when it jumps straight to `toLength`.
    public let resizes: Bool

    public init(fromLength: CGFloat, toLength: CGFloat, reduceMotion: Bool) {
        self.fromLength = fromLength
        self.toLength = toLength
        resizes = fromLength != toLength && !reduceMotion
    }

    public var duration: TimeInterval { resizes ? Self.resizeDuration : Self.fadeOnlyDuration }
    public var fadeDuration: TimeInterval {
        resizes ? Self.resizeDuration * Self.resizeFadeFraction : Self.fadeOnlyDuration
    }

    /// Whole-point item length `elapsed` seconds in.
    public func length(at elapsed: TimeInterval) -> CGFloat {
        resizes
            ? WaveformLayout.tweenLength(from: fromLength, to: toLength, progress: elapsed / Self.resizeDuration)
            : toLength
    }

    /// Cross-fade progress `elapsed` seconds in, 0...1 (linear in time).
    public func fadeProgress(at elapsed: TimeInterval) -> Double {
        min(1, max(0, elapsed / fadeDuration))
    }

    public func isFinished(at elapsed: TimeInterval) -> Bool { elapsed >= duration }
}

/// Draws the idle glyph, live waveform, scanning bar, or error flash. Each content is laid out
/// at its natural width, pinned to the trailing edge and clipped to the current size, so while
/// the item resizes the content stays put on screen and is revealed or covered at the leading
/// edge. The selected background follows the current size.
public struct WaveformView: View {
    @ObservedObject private var model: WaveformModel

    public init(model: WaveformModel) {
        self.model = model
    }

    public var body: some View {
        // Only the scanner, the error flash and a cross-fading scanner depend on elapsed time;
        // other states redraw from model changes alone, so the timeline stays paused to keep
        // an idle menu bar free.
        let phase = WaveformGeometry.menuBarPhase(model.phase, indicator: model.indicator)
        let animating = phase == .transcribing || phase == .error || model.fade?.from == .scanner
        TimelineView(.animation(paused: !animating)) { timeline in
            Canvas { context, size in
                draw(into: &context, size: size, now: timeline.date)
            }
        }
    }

    private func draw(into context: inout GraphicsContext, size: CGSize, now: Date) {
        context.clip(to: Path(CGRect(origin: .zero, size: size)))
        let phase = WaveformGeometry.menuBarPhase(model.phase, indicator: model.indicator)
        let current = StatusItemContent(phase: phase, hideIdleIcon: model.hideIdleIcon)
        let elapsed = now.timeIntervalSince(model.phaseEnteredAt)
        let glyphColor: Color =
            phase == .error && elapsed < WaveformLayout.errorFlashDuration ? .red : .primary
        if let fade = model.fade {
            drawContent(
                fade.from, opacity: 1 - fade.progress, elapsed: now.timeIntervalSince(fade.fromPhaseEnteredAt),
                glyphColor: .primary, into: context, size: size)
            drawContent(current, opacity: fade.progress, elapsed: elapsed, glyphColor: glyphColor, into: context, size: size)
        } else {
            drawContent(current, opacity: 1, elapsed: elapsed, glyphColor: glyphColor, into: context, size: size)
        }
    }

    private func drawContent(
        _ content: StatusItemContent, opacity: Double, elapsed: TimeInterval, glyphColor: Color,
        into parent: GraphicsContext, size: CGSize
    ) {
        guard content != .none, opacity > 0 else { return }
        var context = parent
        context.opacity = opacity
        let natural = content.naturalWidth
        context.translateBy(x: WaveformLayout.trailingOriginX(naturalWidth: natural, in: size.width), y: 0)
        let box = CGSize(width: natural, height: size.height)
        let count = model.columns.count
        switch content {
        case .none:
            return
        case .glyph:
            var path = Path()
            for rect in WaveformLayout.idleBarRects(in: box) {
                path.addRoundedRect(in: rect, cornerSize: CGSize(width: 1, height: 1))
            }
            context.fill(path, with: .color(glyphColor))
        case .waveform:
            var path = Path()
            for (i, level) in model.columns.enumerated() {
                for rect in WaveformLayout.columnRects(index: i, count: count, level: level, look: model.look, in: box) {
                    path.addPath(shape(rect))
                }
            }
            context.fill(path, with: .color(WaveformLayout.tint(for: .waveform, redTint: model.redTint).color))
        case .scanner:
            let opacities = WaveformGeometry.scannerOpacities(elapsed: max(0, elapsed), count: count)
            let tint = WaveformLayout.tint(for: .scanner, redTint: model.redTint).color
            for i in 0..<count {
                var path = Path()
                for rect in WaveformLayout.scannerRects(index: i, count: count, look: model.look, in: box) {
                    path.addPath(shape(rect))
                }
                context.fill(path, with: .color(tint.opacity(opacities[i])))
            }
        }
    }

    /// Pixel cells are square; solid bars get the same 1 pt rounding as the idle glyph.
    private func shape(_ rect: CGRect) -> Path {
        model.look == .solid
            ? Path(roundedRect: rect, cornerRadius: 1)
            : Path(rect)
    }
}
