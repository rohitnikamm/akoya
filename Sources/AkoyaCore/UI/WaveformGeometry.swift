import CoreGraphics
import Foundation

/// Pure sizing and animation math behind the menu bar waveform and scanning bar.
public enum WaveformGeometry {
    public static let columnWidth: CGFloat = 2, gap: CGFloat = 1, padding: CGFloat = 6, cell: CGFloat = 2
    public static let idleItemWidth: CGFloat = 28, activeItemWidth: CGFloat = 77

    /// Number of stacked cells for a pixel-look column: always odd, 1…7.
    public static func pixelCells(level: Int) -> Int {
        2 * max(1, Int((Double(level) / 2).rounded())) - 1
    }

    public static func columnHeight(level: Int, look: WaveLook) -> CGFloat {
        switch look {
        case .pixel: CGFloat(pixelCells(level: level) * 3 - 1)
        case .solid: CGFloat(level * 2)
        }
    }

    /// Per-column opacities for the transcribing scanner: a bright head that
    /// eases back and forth across `count` columns with a fading tail.
    public static func scannerOpacities(elapsed: TimeInterval, count: Int = 22) -> [Double] {
        let p = elapsed.truncatingRemainder(dividingBy: 1.4) / 1.4
        let leg = p < 0.5 ? p * 2 : 2 - p * 2
        let head = (1 - cos(.pi * leg)) / 2 * Double(count - 1)
        return (0..<count).map { i in
            let d = abs(Double(i) - head)
            switch d {
            case ..<1: return 1
            case ..<2: return 0.7
            case ..<3: return 0.48
            case ..<4: return 0.32
            default: return 0.18
            }
        }
    }

    public static func itemWidth(phase: DictationPhase, hideIdleIcon: Bool) -> CGFloat {
        switch phase {
        case .recording, .transcribing: activeItemWidth
        case .idle: hideIdleIcon ? 0 : idleItemWidth
        case .error: idleItemWidth  // error cue must stay visible
        }
    }
}
