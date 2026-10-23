import Foundation
import Testing
@testable import AkoyaCore

@MainActor private final class Slept { var durations: [TimeInterval] = [] }

@MainActor private func makePresenter(_ slept: Slept = Slept()) -> PillPresenter {
    PillPresenter(sleep: { slept.durations.append($0) })
}

@MainActor @Test func recordingShowsListeningThenTranscribing() {
    let p = makePresenter()
    #expect(p.content == nil)
    p.phaseChanged(.recording, isPractice: false, usesPill: true)
    #expect(p.content == .listening)
    p.phaseChanged(.transcribing, isPractice: false, usesPill: true)
    #expect(p.content == .transcribing)
    p.phaseChanged(.idle, isPractice: false, usesPill: true)
    #expect(p.content == .transcribing) // outcomes drive hiding
}

@MainActor @Test func pastedHidesAfter0_9s() async {
    let slept = Slept()
    let p = makePresenter(slept)
    p.phaseChanged(.recording, isPractice: false, usesPill: true)
    p.outcome(.pasted)
    #expect(p.content == .pasted)
    await p.pendingHide?.value
    #expect(slept.durations == [0.9])
    #expect(p.content == nil)
}

@MainActor @Test func messageHidesAfter1_6s() async {
    let slept = Slept()
    let p = makePresenter(slept)
    p.phaseChanged(.recording, isPractice: false, usesPill: true)
    p.outcome(.message(.micMissing))
    #expect(p.content == .message(.micMissing))
    await p.pendingHide?.value
    #expect(slept.durations == [1.6])
    #expect(p.content == nil)
}

@MainActor @Test func dismissedHidesAtOnce() {
    let p = makePresenter()
    p.phaseChanged(.recording, isPractice: false, usesPill: true)
    #expect(p.content == .listening)
    p.outcome(.dismissed)
    #expect(p.content == nil)
}

@MainActor @Test func newRecordingDuringPastedCancelsTheHide() async {
    var release: CheckedContinuation<Void, Never>?
    let p = PillPresenter(sleep: { _ in await withCheckedContinuation { release = $0 } })
    p.phaseChanged(.recording, isPractice: false, usesPill: true)
    p.outcome(.pasted)
    let stale = p.pendingHide
    await Task.yield()
    p.phaseChanged(.recording, isPractice: false, usesPill: true)
    #expect(p.content == .listening)
    release?.resume()
    await stale?.value
    #expect(p.content == .listening)
    #expect(p.pendingHide == nil)
}

@MainActor @Test func practiceNeverShows() {
    let p = makePresenter()
    p.phaseChanged(.recording, isPractice: true, usesPill: true)
    #expect(p.content == nil)
    p.phaseChanged(.transcribing, isPractice: true, usesPill: true)
    #expect(p.content == nil)
}

@MainActor @Test func menuBarModeNeverShows() {
    let p = makePresenter()
    p.phaseChanged(.recording, isPractice: false, usesPill: false)
    #expect(p.content == nil)
    p.phaseChanged(.transcribing, isPractice: false, usesPill: false)
    #expect(p.content == nil)
}

@MainActor @Test func outcomeOfAHiddenDictationDoesNothing() {
    let p = makePresenter()
    p.phaseChanged(.recording, isPractice: true, usesPill: true)
    p.outcome(.pasted)
    #expect(p.content == nil)
    #expect(p.pendingHide == nil)
    p.phaseChanged(.recording, isPractice: false, usesPill: false)
    p.outcome(.message(.micBlocked))
    #expect(p.content == nil)
}
