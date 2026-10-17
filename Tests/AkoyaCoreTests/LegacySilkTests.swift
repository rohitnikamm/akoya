import Testing
@testable import AkoyaCore

private final class FakeApp: TerminableApp {
    private(set) var terminateCount = 0
    func terminate() -> Bool {
        terminateCount += 1
        return true
    }
}

@Suite struct LegacySilkTests {
    @Test func terminatesSilkInstancesOnly() {
        let silkA = FakeApp()
        let silkB = FakeApp()
        let akoya = FakeApp()
        let other = FakeApp()
        var asked: [String] = []
        let count = LegacySilk.quitRunning { bundleID in
            asked.append(bundleID)
            switch bundleID {
            case "com.rohitnikamm.silk": return [silkA, silkB]
            case "com.rohitnikamm.akoya": return [akoya]
            default: return [other]
            }
        }
        #expect(asked == ["com.rohitnikamm.silk"])
        #expect(count == 2)
        #expect(silkA.terminateCount == 1)
        #expect(silkB.terminateCount == 1)
        #expect(akoya.terminateCount == 0)
        #expect(other.terminateCount == 0)
    }

    @Test func nothingRunningIsANoOp() {
        #expect(LegacySilk.quitRunning { _ in [] } == 0)
    }
}
