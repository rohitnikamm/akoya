import CoreGraphics
import Testing
@testable import SilkCore

@Test func rightOptionDown() { #expect(HotkeyDecoder.decode(type: .flagsChanged, keyCode: 61, flags: 0x40 | 0x80000, key: .rightOption, isRecording: false, isHeld: false) == .init(event: .pressed, swallow: false)) }
@Test func rightOptionUp() { #expect(HotkeyDecoder.decode(type: .flagsChanged, keyCode: 61, flags: 0, key: .rightOption, isRecording: true, isHeld: true).event == .released) }
@Test func leftOptionIgnored() { #expect(HotkeyDecoder.decode(type: .flagsChanged, keyCode: 58, flags: 0x20 | 0x80000, key: .rightOption, isRecording: false, isHeld: false).event == nil) }
@Test func escSwallowedOnlyWhileRecording() {
    #expect(HotkeyDecoder.decode(type: .keyDown, keyCode: 53, flags: 0, key: .rightOption, isRecording: true, isHeld: true) == .init(event: .cancel, swallow: true))
    #expect(HotkeyDecoder.decode(type: .keyDown, keyCode: 53, flags: 0, key: .rightOption, isRecording: false, isHeld: false) == .init(event: nil, swallow: false))
}
@Test func missedKeyUpRecovered() { #expect(HotkeyDecoder.decode(type: .flagsChanged, keyCode: 56, flags: 0x02, key: .rightOption, isRecording: true, isHeld: true).event == .released) }
@Test func repeatDownNotDoubled() { #expect(HotkeyDecoder.decode(type: .flagsChanged, keyCode: 61, flags: 0x40, key: .rightOption, isRecording: true, isHeld: true).event == nil) }
@Test func keyCodesAndMasks() {
    #expect(HotkeyKey.rightOption.keyCode == 61 && HotkeyKey.rightOption.deviceMask == 0x40)
    #expect(HotkeyKey.rightCommand.keyCode == 54 && HotkeyKey.rightCommand.deviceMask == 0x10)
    #expect(HotkeyKey.rightControl.keyCode == 62 && HotkeyKey.rightControl.deviceMask == 0x2000)
}
