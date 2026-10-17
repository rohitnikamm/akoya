import Testing
@testable import AkoyaCore

@Test func waitsForSpeech() {
    var scheduler = LivePreviewScheduler()
    let quiet = scheduler.shouldRun(now: 0, audioSeconds: 2, loudestStep: 2, inFlight: false)
    #expect(!quiet)
    let spoken = scheduler.shouldRun(now: 0, audioSeconds: 2, loudestStep: 3, inFlight: false)
    #expect(spoken)
}

@Test func waitsForMinAudio() {
    var scheduler = LivePreviewScheduler()
    let tooShort = scheduler.shouldRun(now: 0, audioSeconds: 0.4, loudestStep: 8, inFlight: false)
    #expect(!tooShort)
    let longEnough = scheduler.shouldRun(now: 0.1, audioSeconds: 0.5, loudestStep: 8, inFlight: false)
    #expect(longEnough)
}

@Test func runsOnInterval() {
    var scheduler = LivePreviewScheduler()
    let first = scheduler.shouldRun(now: 0, audioSeconds: 1, loudestStep: 8, inFlight: false)
    let tooSoon = scheduler.shouldRun(now: 0.3, audioSeconds: 1.3, loudestStep: 8, inFlight: false)
    let next = scheduler.shouldRun(now: 0.61, audioSeconds: 1.6, loudestStep: 8, inFlight: false)
    #expect(first)
    #expect(!tooSoon)
    #expect(next)
}

@Test func skipsWhileInFlight() {
    var scheduler = LivePreviewScheduler()
    let busy = scheduler.shouldRun(now: 0, audioSeconds: 1, loudestStep: 8, inFlight: true)
    // A skipped tick does not consume the interval: the next free tick runs immediately.
    let free = scheduler.shouldRun(now: 0.1, audioSeconds: 1.1, loudestStep: 8, inFlight: false)
    #expect(!busy)
    #expect(free)
}
