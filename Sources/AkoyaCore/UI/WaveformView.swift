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
    @Published public var expandAnimation = false
    /// True while the dropdown panel is open; draws the item's own selected background.
    @Published public var isSelected: Bool = false
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

/// Pure rectangle layout for the waveform, in a canvas whose origin is top-left.
/// Kept free of SwiftUI drawing so it can be unit tested.
public enum WaveformLayout {
    public static let glyphSize: CGFloat = 16
    public static let idleBarHeights: [CGFloat] = [4, 8, 12, 8, 4]
    /// Transcribing columns are two cells tall.
    public static let scannerCells = 2
    /// How long the error glyph stays red after entering `.error`.
    public static let errorFlashDuration: TimeInterval = 0.6

    public static let selectionHeight: CGFloat = 22
    public static let selectionInset: CGFloat = 2
    public static let selectionCornerRadius: CGFloat = 6

    /// The selected background: the item width minus `selectionInset` on each side, 22 pt tall
    /// (at most the item height minus 2 pt), centered vertically.
    public static func selectionRect(in size: CGSize) -> CGRect {
        let height = max(0, min(selectionHeight, size.height - 2))
        let width = max(0, size.width - 2 * selectionInset)
        return CGRect(x: selectionInset, y: (size.height - height) / 2, width: width, height: height)
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

    /// Item length partway through an expand/collapse, ease-in-out sine. `progress` is clamped to 0...1.
    public static func easedLength(from: CGFloat, to: CGFloat, progress: Double) -> CGFloat {
        let t = min(1, max(0, progress))
        let eased = (1 - cos(.pi * t)) / 2
        return from + (to - from) * CGFloat(eased)
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

/// Draws the idle glyph, live waveform, scanning bar, or error flash.
public struct WaveformView: View {
    @ObservedObject private var model: WaveformModel

    public init(model: WaveformModel) {
        self.model = model
    }

    public var body: some View {
        // Only the scanner and the error flash depend on elapsed time; other phases redraw
        // from model changes alone, so the timeline stays paused to keep an idle menu bar free.
        let animating = model.phase == .transcribing || model.phase == .error
        TimelineView(.animation(paused: !animating)) { timeline in
            Canvas { context, size in
                draw(into: &context, size: size, now: timeline.date)
            }
        }
    }

    private func draw(into context: inout GraphicsContext, size: CGSize, now: Date) {
        if model.isSelected {
            let pill = Path(
                roundedRect: WaveformLayout.selectionRect(in: size),
                cornerRadius: WaveformLayout.selectionCornerRadius, style: .continuous)
            context.fill(pill, with: .color(Color.primary.opacity(0.16)))
        }
        let elapsed = now.timeIntervalSince(model.phaseEnteredAt)
        switch model.phase {
        case .idle:
            guard !model.hideIdleIcon else { return }
            fillGlyph(into: &context, size: size, color: .primary)
        case .error:
            fillGlyph(into: &context, size: size, color: elapsed < WaveformLayout.errorFlashDuration ? .red : .primary)
        case .recording:
            let color: Color = model.redTint ? .red : .primary
            let count = model.columns.count
            var path = Path()
            for (i, level) in model.columns.enumerated() {
                for rect in WaveformLayout.columnRects(index: i, count: count, level: level, look: model.look, in: size) {
                    path.addPath(shape(rect))
                }
            }
            context.fill(path, with: .color(color))
        case .transcribing:
            let count = model.columns.count
            let opacities = WaveformGeometry.scannerOpacities(elapsed: max(0, elapsed), count: count)
            for i in 0..<count {
                var path = Path()
                for rect in WaveformLayout.scannerRects(index: i, count: count, look: model.look, in: size) {
                    path.addPath(shape(rect))
                }
                context.fill(path, with: .color(Color.primary.opacity(opacities[i])))
            }
        }
    }

    private func fillGlyph(into context: inout GraphicsContext, size: CGSize, color: Color) {
        var path = Path()
        for rect in WaveformLayout.idleBarRects(in: size) {
            path.addRoundedRect(in: rect, cornerSize: CGSize(width: 1, height: 1))
        }
        context.fill(path, with: .color(color))
    }

    /// Pixel cells are square; solid bars get the same 1 pt rounding as the idle glyph.
    private func shape(_ rect: CGRect) -> Path {
        model.look == .solid
            ? Path(roundedRect: rect, cornerRadius: 1)
            : Path(rect)
    }
}
