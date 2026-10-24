import CoreGraphics
import Testing
@testable import AkoyaCore

private let barSize = CGSize(width: 77, height: 22)

@Test func columnsFillActiveItemWidthWithPadding() {
    #expect(WaveformLayout.columnX(index: 0, count: 22, width: 77) == 6)
    // Last column's right edge sits 6 pt from the item's right edge.
    #expect(WaveformLayout.columnX(index: 21, count: 22, width: 77) + 2 == 71)
}

@Test func pixelColumnStacksCellsWithGapCenteredVertically() {
    let rects = WaveformLayout.columnRects(index: 0, count: 22, level: 8, look: .pixel, in: barSize)
    #expect(rects.count == 7)
    #expect(rects.allSatisfy { $0.width == 2 && $0.height == 2 })
    #expect(rects.first?.minY == 1 && rects.last?.maxY == 21)  // 20 pt tall, centered in 22
    #expect(zip(rects, rects.dropFirst()).allSatisfy { $1.minY - $0.maxY == 1 })
}

@Test func silentPixelColumnIsSingleCell() {
    let rects = WaveformLayout.columnRects(index: 3, count: 22, level: 1, look: .pixel, in: barSize)
    #expect(rects == [CGRect(x: 15, y: 10, width: 2, height: 2)])
}

@Test func solidColumnIsOneBarOfLevelTimesTwo() {
    let rects = WaveformLayout.columnRects(index: 0, count: 22, level: 5, look: .solid, in: barSize)
    #expect(rects == [CGRect(x: 6, y: 6, width: 2, height: 10)])
}

@Test func scannerColumnsAreTwoCellsTallInPixelLook() {
    let rects = WaveformLayout.scannerRects(index: 0, count: 22, look: .pixel, in: barSize)
    #expect(rects == [CGRect(x: 6, y: 8.5, width: 2, height: 2), CGRect(x: 6, y: 11.5, width: 2, height: 2)])
    #expect((rects.last!.maxY - rects.first!.minY) == 5)
}

@Test func scannerColumnIsFivePointBarInSolidLook() {
    #expect(WaveformLayout.scannerRects(index: 0, count: 22, look: .solid, in: barSize)
        == [CGRect(x: 6, y: 8.5, width: 2, height: 5)])
}

@Test func idleGlyphIsFiveBarsInSixteenPointBox() {
    let rects = WaveformLayout.idleBarRects(in: CGSize(width: 28, height: 22))
    #expect(rects.map(\.height) == [4, 8, 12, 8, 4])
    #expect(rects.allSatisfy { $0.width == 2 && $0.midY == 11 })
    #expect(rects.first?.minX == 6 && rects.last?.maxX == 22)  // 16 pt wide, centered in 28
}

@Test func cubicBezierHitsEndpointsAndIsMonotonic() {
    #expect(WaveformLayout.expandEase(0) == 0)
    #expect(WaveformLayout.expandEase(1) == 1)
    #expect(WaveformLayout.expandEase(-2) == 0)  // clamped
    #expect(WaveformLayout.expandEase(3) == 1)
    let samples = (0...200).map { WaveformLayout.expandEase(Double($0) / 200) }
    #expect(zip(samples, samples.dropFirst()).allSatisfy { $1 >= $0 })
}

@Test func expandEaseMatchesCSSCurve() {
    // Reference values for CSS cubic-bezier(0.2, 0.8, 0.2, 1), solved by bisection.
    #expect(abs(WaveformLayout.expandEase(0.5) - 0.94608) < 1e-4)
    #expect(abs(WaveformLayout.expandEase(0.25) - 0.76728) < 1e-4)
    #expect(abs(WaveformLayout.expandEase(0.1) - 0.39669) < 1e-4)
}

@Test func cubicBezierWithLinearControlPointsIsIdentity() {
    for t in stride(from: 0.0, through: 1.0, by: 0.05) {
        #expect(abs(WaveformLayout.cubicBezier(x1: 1.0 / 3, y1: 1.0 / 3, x2: 2.0 / 3, y2: 2.0 / 3, at: t) - t) < 1e-6)
    }
}

@Test func tweenLengthRoundsToWholePointsAndHitsEndpoints() {
    #expect(WaveformLayout.tweenLength(from: 28, to: 77, progress: 0) == 28)
    #expect(WaveformLayout.tweenLength(from: 28, to: 77, progress: 1) == 77)
    #expect(WaveformLayout.tweenLength(from: 28, to: 77, progress: 3) == 77)  // clamped
    #expect(WaveformLayout.tweenLength(from: 77, to: 28, progress: -1) == 77)
    for i in 0...100 {
        let length = WaveformLayout.tweenLength(from: 28, to: 77, progress: Double(i) / 100)
        #expect(length == length.rounded())
    }
}

@Test func itemTweenResizesThenSettles() {
    let tween = ItemTween(fromLength: 28, toLength: 77, reduceMotion: false)
    #expect(tween.resizes)
    #expect(tween.length(at: 0) == 28)
    #expect(tween.length(at: ItemTween.resizeDuration) == 77)
    #expect(tween.fadeProgress(at: 0) == 0)
    #expect(tween.fadeProgress(at: ItemTween.resizeDuration * ItemTween.resizeFadeFraction) == 1)
    #expect(!tween.isFinished(at: 0.1) && tween.isFinished(at: ItemTween.resizeDuration))
}

@Test func itemTweenWithoutWidthChangeOrWithReduceMotionOnlyFades() {
    let same = ItemTween(fromLength: 77, toLength: 77, reduceMotion: false)
    #expect(!same.resizes && same.duration == ItemTween.fadeOnlyDuration)
    let reduced = ItemTween(fromLength: 28, toLength: 77, reduceMotion: true)
    #expect(!reduced.resizes)
    #expect(reduced.length(at: 0) == 77)  // jumps
    #expect(reduced.fadeProgress(at: ItemTween.fadeOnlyDuration / 2) == 0.5)
}

@Test func contentIsPinnedToTrailingEdgeOnWholePixelsEveryFrame() {
    // Sample a 120 Hz expand: columns never move on screen (the item's trailing edge is fixed)
    // and land on whole pixels at 2x.
    let tween = ItemTween(fromLength: 28, toLength: 77, reduceMotion: false)
    let finalX = (0..<22).map { WaveformLayout.columnX(index: $0, count: 22, width: 77) - 77 }
    for frame in 0...27 {
        let width = tween.length(at: Double(frame) / 120)
        let origin = WaveformLayout.trailingOriginX(naturalWidth: StatusItemContent.waveform.naturalWidth, in: width)
        let xs = (0..<22).map { origin + WaveformLayout.columnX(index: $0, count: 22, width: 77) }
        #expect(xs.map { $0 - width } == finalX)  // same offset from the trailing edge
        #expect(xs.allSatisfy { ($0 * 2) == ($0 * 2).rounded() })
        let glyphOrigin = WaveformLayout.trailingOriginX(naturalWidth: StatusItemContent.glyph.naturalWidth, in: width)
        #expect(WaveformLayout.idleBarRects(in: CGSize(width: 28, height: 22))
            .allSatisfy { (($0.minX + glyphOrigin) * 2).rounded() == ($0.minX + glyphOrigin) * 2 })
    }
}

@Test func itemContentFollowsPhase() {
    #expect(StatusItemContent(phase: .idle, hideIdleIcon: false) == .glyph)
    #expect(StatusItemContent(phase: .idle, hideIdleIcon: true) == StatusItemContent.none)
    #expect(StatusItemContent(phase: .error, hideIdleIcon: true) == .glyph)
    #expect(StatusItemContent(phase: .recording, hideIdleIcon: false) == .waveform)
    #expect(StatusItemContent(phase: .transcribing, hideIdleIcon: false) == .scanner)
    #expect(StatusItemContent.glyph.naturalWidth == 28 && StatusItemContent.scanner.naturalWidth == 77)
}




@Test func waveformTintFollowsRedWhileRecording() {
    #expect(WaveformLayout.tint(for: .waveform, redTint: false) == .primary)
    #expect(WaveformLayout.tint(for: .waveform, redTint: true) == .red)
}

@Test func scannerTurnsAmberOnlyWithRedWhileRecording() {
    #expect(WaveformLayout.tint(for: .scanner, redTint: false) == .primary)
    #expect(WaveformLayout.tint(for: .scanner, redTint: true) == .amber)
}

@Test func idleGlyphIgnoresRedWhileRecording() {
    #expect(WaveformLayout.tint(for: .glyph, redTint: true) == .primary)
}

@Test func menuBarStaysIdleInPillMode() {
    for phase in [DictationPhase.idle, .recording, .transcribing, .error] {
        #expect(WaveformGeometry.menuBarPhase(phase, indicator: .pill) == .idle)
        #expect(WaveformGeometry.menuBarPhase(phase, indicator: .menuBar) == phase)
    }
}
