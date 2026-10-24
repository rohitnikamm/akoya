import Foundation
import Testing
@testable import AkoyaCore

private func dot(_ dots: [Double], _ column: Int, _ row: Int) -> Double { dots[column * PillDots.rows + row] }
private func near(_ a: Double, _ b: Double) -> Bool { abs(a - b) < 1e-9 }

@Test func quietColumnDimsMiddleRows() { #expect(PillDots.listening([1]) == [0.12, 0.35, 0.35, 0.12]) }
@Test func step2LightsMiddleRowsOnly() { #expect(PillDots.listening([2]) == [0.12, 0.95, 0.95, 0.12]) }
@Test func steps3And4KeepOuterRowsFaint() {
    #expect(PillDots.listening([3]) == [0.12, 0.95, 0.95, 0.12])
    #expect(PillDots.listening([4]) == [0.12, 0.95, 0.95, 0.12])
}
@Test func outerRowsRiseFromStep5() {
    let d = PillDots.listening([5, 6])
    #expect(near(dot(d, 0, 0), 0.12 + 0.83 * 1 / 3) && near(dot(d, 0, 3), 0.12 + 0.83 / 3))
    #expect(near(dot(d, 1, 0), 0.12 + 0.83 * 2 / 3) && dot(d, 1, 1) == 0.95)
}
@Test func step8LightsAllRows() { #expect(PillDots.listening([8]) == [0.95, 0.95, 0.95, 0.95]) }
@Test func listeningIsColumnMajor() {
    let d = PillDots.listening([1, 8])
    #expect(d.count == 8 && d[1] == 0.35 && d[4] == 0.95)
    #expect(PillDots.listening([]).isEmpty)
}
@Test func scannerLengthIsColumnsTimesRows() {
    #expect(PillDots.scanner(elapsed: 0).count == 23 * 4)
    #expect(PillDots.scanner(elapsed: 0, columns: 10).count == 40)
}
@Test func scannerHeadStartsAtColumnZero() {
    let d = PillDots.scanner(elapsed: 0)
    #expect(d[1] == 1 && d[2] == 1 && d[0] == 0.18 && d[22 * 4 + 1] == 0.18)
    for (c, e) in [1.0, 0.7, 0.48, 0.32, 0.18].enumerated() {
        #expect(dot(d, c, 1) == e && dot(d, c, 2) == e && dot(d, c, 0) == 0.18 && dot(d, c, 3) == 0.18)
    }
}
@Test func scannerHeadReachesLastColumnAtHalfPeriod() { #expect(PillDots.scanner(elapsed: 0.7)[22 * 4 + 1] == 1) }
@Test func scannerMidwayHeadIsMiddleColumn() { #expect(dot(PillDots.scanner(elapsed: 0.35), 11, 2) == 1) }
@Test func scannerRepeatsEveryPeriod() {
    let a = PillDots.scanner(elapsed: 0.2), b = PillDots.scanner(elapsed: 0.2 + 1.4 * 3)
    #expect(zip(a, b).allSatisfy { near($0, $1) })
}
@Test func geometryIs3PointDotsTwoApart() {
    #expect(PillDots.dotSize == 3 && PillDots.gap == 2 && PillDots.columns == 23 && PillDots.rows == 4)
}
@Test func solidHeightIsOneDotToFourRows() { #expect(PillDots.solidHeight(step: 1) == 3 && PillDots.solidHeight(step: 8) == 18) }
@Test func solidHeightClampsAndIsLinear() {
    #expect(PillDots.solidHeight(step: 0) == 3 && PillDots.solidHeight(step: -4) == 3 && PillDots.solidHeight(step: 12) == 18)
    for s in 2...8 { #expect(near(Double(PillDots.solidHeight(step: s) - PillDots.solidHeight(step: s - 1)), 15.0 / 7)) }
}
@Test func solidListeningOneBrightBarPerColumn() {
    let bars = PillDots.solidListening([1, 4, 8])
    #expect(bars.count == 3 && bars[0].height == PillDots.solidHeight(step: 1) && bars[2].height == 18)
    #expect(bars.allSatisfy { $0.opacity == 0.95 })
    #expect(PillDots.solidListening([]).isEmpty)
}
@Test func solidScannerIsCentreTwoRowsTall() {
    let bars = PillDots.solidScanner(elapsed: 0.3), dots = PillDots.scanner(elapsed: 0.3)
    #expect(bars.count == 23)
    for c in 0..<23 { #expect(bars[c].height == 8 && bars[c].opacity == dot(dots, c, 1)) }
    #expect(PillDots.solidScanner(elapsed: 0, columns: 10).count == 10)
}
@Test func scannerLitIsHeadAndTailOnly() {
    let d = PillDots.scanner(elapsed: 0)
    let lit = (0..<23).filter { PillDots.isScannerLit(dot(d, $0, 1)) }
    #expect(lit == [0, 1, 2, 3])
    #expect(!PillDots.isScannerLit(0.18) && PillDots.isScannerLit(0.32))
}
