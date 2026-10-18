import Foundation
import Testing
@testable import AkoyaCore

private final class FakeInstance: RunningInstance {
    let processIdentifier: pid_t
    private(set) var terminateCount = 0
    init(pid: pid_t) { processIdentifier = pid }
    func terminate() -> Bool {
        terminateCount += 1
        return true
    }
}

@Suite struct OtherInstancesTests {
    @Test func quitsEveryOtherAkoyaButNotItself() {
        let me = FakeInstance(pid: 100)
        let dev = FakeInstance(pid: 200)
        let installed = FakeInstance(pid: 300)
        var asked: [String] = []
        let count = OtherInstances.quitRunning(bundleIdentifier: "com.rohitnikamm.akoya", ownPID: 100) { bundleID in
            asked.append(bundleID)
            return [me, dev, installed]
        }
        #expect(asked == ["com.rohitnikamm.akoya"])
        #expect(count == 2)
        #expect(me.terminateCount == 0)
        #expect(dev.terminateCount == 1)
        #expect(installed.terminateCount == 1)
    }

    @Test func aloneIsANoOp() {
        let me = FakeInstance(pid: 100)
        #expect(OtherInstances.quitRunning(bundleIdentifier: "com.rohitnikamm.akoya", ownPID: 100) { _ in [me] } == 0)
        #expect(me.terminateCount == 0)
    }

    @Test func missingBundleIdentifierIsANoOp() {
        let other = FakeInstance(pid: 200)
        #expect(OtherInstances.quitRunning(bundleIdentifier: nil, ownPID: 100) { _ in [other] } == 0)
        #expect(other.terminateCount == 0)
    }
}
