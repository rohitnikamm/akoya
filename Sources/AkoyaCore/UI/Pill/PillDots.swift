import CoreGraphics
import Foundation

/// One column of the Solid waveform: a rounded bar `height` points tall, centred on the dot rows.
public struct PillBar: Equatable {
    public var height: CGFloat
    public var opacity: Double
}

/// The pill's waveform, ported from the Windows app. Pixel look: dot opacities, column-major arrays
/// (index = column * rows + row), row 0 at the top. Solid look: one `PillBar` per column,
/// `dotSize` wide with fully rounded ends.
public enum PillDots {
    public static let rows = 4
    public static let columns = 23

    /// Dot diameter (and Solid bar width), points.
    public static let dotSize: CGFloat = 3

    /// Space between dots, rows and columns alike, points.
    public static let gap: CGFloat = 2

    /// The four dot rows' height: the tallest Solid bar.
    public static let maxBarHeight: CGFloat = CGFloat(rows) * dotSize + CGFloat(rows - 1) * gap

    /// The centre two dot rows' height: the Solid scanner's bars.
    public static let scannerBarHeight: CGFloat = 2 * dotSize + gap

    /// A transcribing column at rest (away from the scanner's head and tail).
    public static let scannerRest = 0.18

    private static let solidOpacity = 0.95
    private static let faint = 0.12
    private static let dim = 0.35
    private static let lit = 0.95

    /// Listening waveform. Step 1 dims the middle rows (1, 2); from step 2 the middle rows are fully lit
    /// and the outer rows (0, 3) rise from step 4 to 7 so step 8 lights all four.
    public static func listening(_ steps: [Int]) -> [Double] {
        var dots = [Double](repeating: 0, count: steps.count * rows)
        for (c, raw) in steps.enumerated() {
            let step = min(8, max(1, raw))
            let middle: Double
            let outer: Double
            if step <= 1 {
                middle = dim
                outer = faint
            } else {
                middle = lit
                outer = faint + (lit - faint) * min(1, max(0, Double(step - 4) / 3))
            }
            dots[c * rows + 0] = outer
            dots[c * rows + 1] = middle
            dots[c * rows + 2] = middle
            dots[c * rows + 3] = outer
        }
        return dots
    }

    /// Transcribing scanner: a bright head eases back and forth across the columns (period 1.4 s)
    /// with a fading tail, lighting the centre two rows (1, 2); the outer rows stay at rest.
    public static func scanner(elapsed: TimeInterval, columns: Int = PillDots.columns) -> [Double] {
        let period = 1.4
        let p = elapsed.truncatingRemainder(dividingBy: period) / period
        let leg = p < 0.5 ? p * 2 : 2 - p * 2
        let head = (1 - cos(Double.pi * leg)) / 2 * Double(columns - 1)
        var dots = [Double](repeating: 0, count: columns * rows)
        for c in 0..<columns {
            let d = abs(Double(c) - head)
            let opacity = d < 1 ? 1.0 : d < 2 ? 0.7 : d < 3 ? 0.48 : d < 4 ? 0.32 : scannerRest
            dots[c * rows + 0] = scannerRest
            dots[c * rows + 1] = opacity
            dots[c * rows + 2] = opacity
            dots[c * rows + 3] = scannerRest
        }
        return dots
    }

    /// A Solid bar's height for a level step (1...8, clamped): one dot at step 1 rising linearly to the
    /// four rows' height at step 8.
    public static func solidHeight(step: Int) -> CGFloat {
        let s = min(8, max(1, step))
        return dotSize + CGFloat(s - 1) / 7 * (maxBarHeight - dotSize)
    }

    /// Listening waveform, Solid look: a bright bar per column, its height from the column's step.
    public static func solidListening(_ steps: [Int]) -> [PillBar] {
        steps.map { PillBar(height: solidHeight(step: $0), opacity: solidOpacity) }
    }

    /// Transcribing scanner, Solid look: bars the centre two rows tall (the extent the dot scanner lights)
    /// at the dot scanner's opacities.
    public static func solidScanner(elapsed: TimeInterval, columns: Int = PillDots.columns) -> [PillBar] {
        let dots = scanner(elapsed: elapsed, columns: columns)
        return (0..<columns).map { PillBar(height: scannerBarHeight, opacity: dots[$0 * rows + 1]) }
    }

    /// A scanner column brighter than at rest: the head and its fading tail, the columns that turn amber
    /// when Red while recording is on.
    public static func isScannerLit(_ opacity: Double) -> Bool { opacity > scannerRest + 1e-9 }
}
