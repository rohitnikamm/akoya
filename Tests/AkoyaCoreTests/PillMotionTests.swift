import CoreGraphics
import Foundation
import Testing
@testable import AkoyaCore

private let w: CGFloat = 179 // listening pill, points
private let h: CGFloat = 34
private func near(_ a: some BinaryFloatingPoint, _ b: some BinaryFloatingPoint, _ tol: Double = 1e-4) -> Bool {
    abs(Double(a) - Double(b)) < tol
}
private func frame(_ p: Double, width: CGFloat = w) -> PillPopInFrame {
    PillPopIn.frame(progress: p, finalWidth: width, height: h)
}

@Test func popInDurationIs420Milliseconds() { #expect(PillPopIn.duration == 0.42) }
@Test func hideConstants() {
    #expect(PillHide.duration == 0.28 && PillHide.sink == 10 && PillHide.scale == 0.94)
}

@Test func popInStartsAsSmallFaintCircle() {
    let f = frame(0)
    #expect(f.width == h && f.scaleX == 0.55 && f.scaleY == 0.55 && f.opacity == 0 && f.contentOpacity == 0)
}
@Test func popInAt24PercentIsFullCircleWithContentHidden() {
    let f = frame(0.24)
    #expect(near(f.width, h) && near(f.scaleX, 1) && near(f.scaleY, 1) && near(f.opacity, 1) && f.contentOpacity == 0)
}
@Test func popInStretchesTo107PercentAt64Percent() {
    let f = frame(0.64)
    #expect(near(f.width, 1.07 * w) && near(f.scaleX, 1) && near(f.scaleY, 0.92) && near(f.opacity, 1))
}
@Test func popInSpringsBackAt84Percent() {
    let f = frame(0.84)
    #expect(near(f.width, 0.985 * w) && near(f.scaleX, 1) && near(f.scaleY, 1.03))
}
@Test func popInEndsSettled() {
    #expect(frame(1) == PillPopInFrame(width: w, scaleX: 1, scaleY: 1, opacity: 1, contentOpacity: 1))
}
@Test func firstSegmentOvershootsSlightly() {
    let f = frame(50 / 420.0)
    #expect(f.scaleX > 1.005 && f.scaleX < 1.04 && f.scaleX == f.scaleY && f.width == h)
}
@Test func opacityNeverAboveOneWhileScaleOvershoots() {
    for i in 0...100 { let o = frame(Double(i) / 100).opacity; #expect(o >= 0 && o <= 1) }
}
@Test func secondSegmentUsesItsDecelerateCurve() {
    let e = WaveformLayout.cubicBezier(x1: 0.3, y1: 0.8, x2: 0.4, y2: 1, at: 0.5)
    let f = frame(0.44)
    #expect(near(f.width, h + (1.07 * w - h) * CGFloat(e)))
    #expect(f.width > (h + 1.07 * w) / 2)
    #expect(near(f.scaleY, 1 - 0.08 * e))
}
@Test func lastSegmentsEaseInOut() {
    let third = frame(0.74)
    #expect(near(third.width, (1.07 * w + 0.985 * w) / 2) && near(third.scaleY, (0.92 + 1.03) / 2))
    let last = frame(0.92)
    #expect(near(last.width, (0.985 * w + w) / 2) && near(last.scaleY, (1.03 + 1) / 2))
}
@Test func contentsFadeInLinearlyFrom45Percent() {
    for (p, expected) in [(0.0, 0.0), (0.45, 0.0), (0.725, 0.5), (1.0, 1.0)] {
        #expect(near(frame(p).contentOpacity, expected, 1e-9))
    }
}
@Test func progressOutsideRangeIsClamped() {
    #expect(frame(-0.5) == frame(0))
    #expect(frame(3) == frame(1))
    #expect(frame(.nan) == frame(1))
    #expect(frame(.nan).width == w)
}
@Test func widthNeverBelowTheCircle() {
    for i in 0...100 { #expect(frame(Double(i) / 100).width >= h - 1e-9) }
    #expect(frame(0.64, width: 20).width == h)
}
@Test func progressFromElapsedTime() {
    for (s, expected) in [(0.0, 0.0), (0.21, 0.5), (0.42, 1.0), (0.9, 1.0), (-0.01, 0.0)] {
        #expect(near(PillPopIn.progress(elapsed: s), expected, 1e-9))
    }
}

// The Windows CubicBezier tests, against WaveformLayout.cubicBezier.
@Test func linearBezierIsIdentity() {
    for t in [0.0, 0.25, 0.7, 1.0] { #expect(near(WaveformLayout.cubicBezier(x1: 0, y1: 0, x2: 1, y2: 1, at: t), t, 1e-6)) }
}
@Test func easeInOutIsSymmetric() {
    func e(_ t: Double) -> Double { WaveformLayout.cubicBezier(x1: 0.42, y1: 0, x2: 0.58, y2: 1, at: t) }
    #expect(near(e(0.5), 0.5, 1e-6) && near(e(0.2) + e(0.8), 1, 1e-6) && e(0.2) < 0.2)
}
@Test func bezierEndsAreFixedAndTimeIsClamped() {
    func e(_ t: Double) -> Double { WaveformLayout.cubicBezier(x1: 0.2, y1: 0.9, x2: 0.3, y2: 1.2, at: t) }
    #expect(e(0) == 0 && e(1) == 1 && e(-3) == 0 && e(4) == 1)
}
@Test func bezierYAboveOneOvershoots() {
    let peak = (1...99).map { WaveformLayout.cubicBezier(x1: 0.2, y1: 0.9, x2: 0.3, y2: 1.2, at: Double($0) / 100) }.max()!
    #expect(peak > 1.01 && peak < 1.1)
}
