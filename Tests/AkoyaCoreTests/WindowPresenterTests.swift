import Testing
@testable import AkoyaCore

@Test func hidesAppWhenNoOtherWindowIsVisible() {
    #expect(WindowPresenter.shouldHideApp(othersVisible: []))
    #expect(WindowPresenter.shouldHideApp(othersVisible: [false]))
}

@Test func keepsAppWhenAnotherWindowIsVisible() {
    #expect(!WindowPresenter.shouldHideApp(othersVisible: [true]))
    #expect(!WindowPresenter.shouldHideApp(othersVisible: [false, true]))
}
