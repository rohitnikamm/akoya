import Testing
@testable import SilkCore

@Test func pixelCellsMap() { #expect((1...8).map(WaveformGeometry.pixelCells) == [1,1,3,3,5,5,7,7]) }
@Test func pixelHeights() { #expect(WaveformGeometry.columnHeight(level: 1, look: .pixel) == 2); #expect(WaveformGeometry.columnHeight(level: 8, look: .pixel) == 20) }
@Test func solidHeights() { #expect(WaveformGeometry.columnHeight(level: 8, look: .solid) == 16) }
@Test func scannerAtStart() { #expect(Array(WaveformGeometry.scannerOpacities(elapsed: 0).prefix(5)) == [1, 0.7, 0.48, 0.32, 0.18]) }
@Test func scannerAtFarEnd() { #expect(WaveformGeometry.scannerOpacities(elapsed: 0.7).last == 1) }
@Test func scannerMidway() { let o = WaveformGeometry.scannerOpacities(elapsed: 0.35); #expect(o[10] == 1 && o[11] == 1) }
@Test func widths() {
    #expect(WaveformGeometry.itemWidth(phase: .recording, hideIdleIcon: false) == 77)
    #expect(WaveformGeometry.itemWidth(phase: .transcribing, hideIdleIcon: false) == 77)
    #expect(WaveformGeometry.itemWidth(phase: .idle, hideIdleIcon: false) == 28)
    #expect(WaveformGeometry.itemWidth(phase: .idle, hideIdleIcon: true) == 0)
    #expect(WaveformGeometry.itemWidth(phase: .error, hideIdleIcon: true) == 28)  // error cue must be visible
}
