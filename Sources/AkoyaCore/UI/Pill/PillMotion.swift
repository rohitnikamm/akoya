import CoreGraphics
import Foundation

/// One frame of the pill's pop-in: the drawn `width` (its corner radius stays half its height, so at the
/// height it is a circle), the scale about its centre, its opacity, and the opacity of its contents
/// (dots, timer, label).
public struct PillPopInFrame: Equatable {
    public var width: CGFloat
    public var scaleX: CGFloat
    public var scaleY: CGFloat
    public var opacity: Double
    public var contentOpacity: Double
}

/// The "jelly" pop-in, ported from the Windows app: over 420 ms the pill grows from a small faint circle
/// to a full circle (0-24 %, a slight overshoot), stretches to 107 % of its width while squashing to
/// 92 % height (24-64 %), springs back to 98.5 % width and 103 % height (64-84 %), and settles
/// (84-100 %). Its contents fade in from 45 % to the end.
public enum PillPopIn {
    public static let duration: TimeInterval = 0.42

    private static let circleEnd = 0.24
    private static let stretchEnd = 0.64
    private static let springEnd = 0.84
    private static let contentStart = 0.45

    private static let startScale = 0.55
    private static let stretchWidth = 1.07
    private static let stretchScaleY = 0.92
    private static let springWidth = 0.985
    private static let springScaleY = 1.03

    /// Progress 0...1 after `elapsed` seconds (clamped).
    public static func progress(elapsed: TimeInterval) -> Double {
        min(1, max(0, elapsed / duration))
    }

    /// The frame at `progress` (0...1, clamped; NaN counts as the end) for a pill `finalWidth` x `height`
    /// points when settled.
    public static func frame(progress: Double, finalWidth: CGFloat, height: CGFloat) -> PillPopInFrame {
        let p = progress.isNaN ? 1 : min(1, max(0, progress))
        let content = min(1, max(0, (p - contentStart) / (1 - contentStart)))
        let circle = Double(height), full = Double(finalWidth)

        var width: Double, scaleX = 1.0, scaleY = 1.0, opacity = 1.0
        if p < circleEnd {
            // cubic-bezier(.2,.9,.3,1.2): scale overshoots a touch past 1; opacity is clamped like CSS clamps it.
            let e = WaveformLayout.cubicBezier(x1: 0.2, y1: 0.9, x2: 0.3, y2: 1.2, at: p / circleEnd)
            width = circle
            scaleX = startScale + (1 - startScale) * e
            scaleY = scaleX
            opacity = min(1, max(0, e))
        } else if p < stretchEnd {
            let e = WaveformLayout.cubicBezier(x1: 0.3, y1: 0.8, x2: 0.4, y2: 1, at: (p - circleEnd) / (stretchEnd - circleEnd))
            width = lerp(circle, stretchWidth * full, e)
            scaleY = lerp(1, stretchScaleY, e)
        } else if p < springEnd {
            let e = easeInOut((p - stretchEnd) / (springEnd - stretchEnd))
            width = lerp(stretchWidth * full, springWidth * full, e)
            scaleY = lerp(stretchScaleY, springScaleY, e)
        } else if p < 1 {
            let e = easeInOut((p - springEnd) / (1 - springEnd))
            width = lerp(springWidth * full, full, e)
            scaleY = lerp(springScaleY, 1, e)
        } else {
            width = full
        }

        return PillPopInFrame(width: CGFloat(max(circle, width)), scaleX: CGFloat(scaleX), scaleY: CGFloat(scaleY),
                              opacity: opacity, contentOpacity: content)
    }

    private static func easeInOut(_ t: Double) -> Double {
        WaveformLayout.cubicBezier(x1: 0.42, y1: 0, x2: 0.58, y2: 1, at: t)
    }

    private static func lerp(_ from: Double, _ to: Double, _ t: Double) -> Double { from + (to - from) * t }
}

/// The pill's hide: over 280 ms it fades while sinking `sink` points and shrinking to `scale`.
public enum PillHide {
    public static let duration: TimeInterval = 0.28
    public static let sink: CGFloat = 10
    public static let scale: CGFloat = 0.94
}
