import Foundation
import Testing
@testable import AkoyaCore

@Test func silenceIsStepOne() { #expect(LevelMeter.step(forRMS: 0) == 1) }
@Test func fullScaleIsStepEight() { #expect(LevelMeter.step(forRMS: 1.0) == 8) }
@Test func minus30dBIsStepFour() { #expect(LevelMeter.step(forRMS: Float(pow(10.0, -30.0 / 20.0))) == 4) }
@Test func startsAllOnes() { #expect(LevelMeter().columns == Array(repeating: 1, count: 22)) }
@Test func newestOnRight() {
    var m = LevelMeter()
    m.push(rms: 1)
    #expect(m.columns.last == 8)
    #expect(m.columns.count == 22)
}
@Test func releaseFallsAtMostTwoSteps() {
    var m = LevelMeter()
    m.push(rms: 1)
    m.push(rms: 0)
    #expect(m.columns.last == 6)
}
@Test func resetReturnsToAllOnes() {
    var m = LevelMeter()
    m.push(rms: 1)
    m.reset()
    #expect(m.columns == Array(repeating: 1, count: 22))
}
@Test func clampsAboveFullScale() { #expect(LevelMeter.step(forRMS: 10) == 8) }
@Test func meterKeepsRequestedColumnCount() {
    var m = LevelMeter(columnCount: 23)
    #expect(m.columns.count == 23)
    m.push(rms: 1)
    #expect(m.columns.count == 23)
    m.reset()
    #expect(m.columns == Array(repeating: 1, count: 23))
}
