import CoreGraphics
import Foundation
import Testing
@testable import AkoyaCore

private let screen = CGRect(x: 0, y: 0, width: 1512, height: 982)
private let pill = CGSize(width: 180, height: 34)

@Test func bottomDockSits20AboveIt() {
    let p = PillPlacement.origin(screenFrame: screen, visibleFrame: CGRect(x: 0, y: 70, width: 1512, height: 887),
                                 dock: .init(edge: .bottom, autohide: false, thickness: 70), pillSize: pill)
    #expect(p == CGPoint(x: 666, y: 90))
}

@Test func autoHiddenBottomDockKeepsTheSameHeight() {
    let p = PillPlacement.origin(screenFrame: screen, visibleFrame: CGRect(x: 0, y: 0, width: 1512, height: 957),
                                 dock: .init(edge: .bottom, autohide: true, thickness: 70), pillSize: pill)
    #expect(p.y == 90)
}

@Test func screenWithoutTheDockReservesItsHeightToo() { // second display / full-screen Space: visibleFrame == frame
    let second = CGRect(x: 1512, y: 0, width: 1920, height: 1080)
    let p = PillPlacement.origin(screenFrame: second, visibleFrame: second,
                                 dock: .init(edge: .bottom, autohide: false, thickness: 70), pillSize: pill)
    #expect(p == CGPoint(x: 1512 + 870, y: 90))
}

@Test func sideDockSits20AboveTheBottomEdge() {
    let p = PillPlacement.origin(screenFrame: screen, visibleFrame: CGRect(x: 70, y: 0, width: 1442, height: 957),
                                 dock: .init(edge: .left, autohide: false, thickness: 70), pillSize: pill)
    #expect(p == CGPoint(x: 70 + 631, y: 20))
}
