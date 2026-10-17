import Testing
@testable import AkoyaCore

@Test func windowsOf640() {
    var w = RMSWindower()
    #expect(w.append(Array(repeating: 0.5, count: 1000)) == [0.5])
    #expect(w.append(Array(repeating: 0.5, count: 280)).count == 1)
}
@Test func partialWindowReturnsNothing() {
    var w = RMSWindower()
    #expect(w.append(Array(repeating: 0.5, count: 639)).isEmpty)
    #expect(w.append([0.5]).count == 1)
}
