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

@Test func easedLengthHitsEndpointsAndMidpoint() {
    #expect(WaveformLayout.easedLength(from: 28, to: 77, progress: 0) == 28)
    #expect(WaveformLayout.easedLength(from: 28, to: 77, progress: 1) == 77)
    #expect(WaveformLayout.easedLength(from: 28, to: 77, progress: 0.5) == 52.5)
    #expect(WaveformLayout.easedLength(from: 28, to: 77, progress: 3) == 77)  // clamped
    #expect(WaveformLayout.easedLength(from: 77, to: 28, progress: -1) == 77)
}

@Test func selectionRectInsetsWidthAndCentersVertically() {
    let r = WaveformLayout.selectionRect(in: CGSize(width: 28, height: 24))
    #expect(r == CGRect(x: 2, y: 1, width: 24, height: 22))
}

@Test func selectionRectClampsHeightToBoundsMinusTwo() {
    let r = WaveformLayout.selectionRect(in: CGSize(width: 77, height: 22))
    #expect(r == CGRect(x: 2, y: 1, width: 73, height: 20))
    #expect(r.midY == 11)
}

@Test func selectionRectNeverGoesNegative() {
    let r = WaveformLayout.selectionRect(in: CGSize(width: 3, height: 1))
    #expect(r.width == 0 && r.height == 0)
}
