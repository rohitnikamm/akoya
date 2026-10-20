using Akoya.Core.Hotkey;
using Xunit;

namespace Akoya.Core.Tests;

public class HotkeyDecoderTests
{
    private const int VkRMenu = 0xA5;
    private const int VkRControl = 0xA3;
    private const int VkLControl = 0xA2;
    private const int VkLWin = 0x5B;
    private const int VkLShift = 0xA0;
    private const int VkF23 = 0x86;
    private const int VkEscape = 0x1B;

    private static RawKey Down(int vk, int scan = 0, bool extended = false, bool injected = false) =>
        new(vk, scan, extended, injected, true);

    private static RawKey Up(int vk, int scan = 0, bool extended = false, bool injected = false) =>
        new(vk, scan, extended, injected, false);

    private static readonly DecodeResult Pass = new(false, null);
    private static readonly DecodeResult SwallowOnly = new(true, null);

    [Fact]
    public void RightAlt_DownUp_PressedThenReleased_BothSwallowed()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRMenu, 0x38, true)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkRMenu, 0x38, true)));
    }

    [Fact]
    public void RightAlt_MatchesWithoutExtendedFlag()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRMenu, extended: false)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkRMenu, extended: false)));
    }

    [Fact]
    public void RightAlt_Repeat_IgnoredButSwallowed()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        d.Decode(Down(VkRMenu, 0x38, true));
        Assert.Equal(SwallowOnly, d.Decode(Down(VkRMenu, 0x38, true)));
        Assert.Equal(SwallowOnly, d.Decode(Down(VkRMenu, 0x38, true)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkRMenu, 0x38, true)));
    }

    [Fact]
    public void RightAlt_LeftAltAndOtherKeys_PassThrough()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(Pass, d.Decode(Down(0xA4, 0x38)));
        Assert.Equal(Pass, d.Decode(Down(0x41, 0x1E)));
        Assert.Equal(Pass, d.Decode(Up(0x41, 0x1E)));
        Assert.Equal(Pass, d.Decode(Down(VkRControl, 0x1D, true)));
    }

    [Fact]
    public void AltGr_FakeLeftCtrl_Swallowed()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(SwallowOnly, d.Decode(Down(VkLControl, 0x21D)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRMenu, 0x38, true)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkRMenu, 0x38, true)));
        Assert.Equal(SwallowOnly, d.Decode(Up(VkLControl, 0x21D)));
    }

    [Fact]
    public void RealLeftCtrl_PassesThrough()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(Pass, d.Decode(Down(VkLControl, 0x1D)));
        Assert.Equal(Pass, d.Decode(Up(VkLControl, 0x1D)));
    }

    [Fact]
    public void AltGrFakeCtrlRule_AppliesOnlyToRightAlt()
    {
        foreach (var key in new[] { HotkeyKey.RightCtrl, HotkeyKey.Copilot })
        {
            var d = new HotkeyDecoder(key);
            Assert.Equal(Pass, d.Decode(Down(VkLControl, 0x21D)));
            Assert.Equal(Pass, d.Decode(Up(VkLControl, 0x21D)));
        }
    }

    [Fact]
    public void RightCtrl_Hotkey_RightAltPassesThrough()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightCtrl);
        Assert.Equal(Pass, d.Decode(Down(VkRMenu, 0x38, true)));
        Assert.Equal(Pass, d.Decode(Up(VkRMenu, 0x38, true)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRControl, 0x1D, true)));
        Assert.Equal(SwallowOnly, d.Decode(Down(VkRControl, 0x1D, true)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkRControl, 0x1D, true)));
    }

    [Fact]
    public void RightAlt_Hotkey_RightCtrlPassesThrough()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(Pass, d.Decode(Down(VkRControl, 0x1D, true)));
        Assert.Equal(Pass, d.Decode(Up(VkRControl, 0x1D, true)));
    }

    [Fact]
    public void Copilot_F23WithWinShift_PressedReleased()
    {
        var d = new HotkeyDecoder(HotkeyKey.Copilot);
        Assert.Equal(Pass, d.Decode(Down(VkLWin, 0x5B, true)));
        Assert.Equal(Pass, d.Decode(Down(VkLShift, 0x2A)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed, true), d.Decode(Down(VkF23, 0x6E)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkF23, 0x6E)));
        Assert.Equal(Pass, d.Decode(Up(VkLShift, 0x2A)));
        Assert.Equal(Pass, d.Decode(Up(VkLWin, 0x5B, true)));
    }

    [Fact]
    public void Copilot_F23Repeat_SwallowedWithoutNewPressOrMask()
    {
        var d = new HotkeyDecoder(HotkeyKey.Copilot);
        d.Decode(Down(VkLWin));
        d.Decode(Down(VkLShift));
        d.Decode(Down(VkF23));
        Assert.Equal(SwallowOnly, d.Decode(Down(VkF23)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkF23)));
    }

    [Fact]
    public void Copilot_ModifiersReleasedBeforeF23_StillReleases()
    {
        var d = new HotkeyDecoder(HotkeyKey.Copilot);
        d.Decode(Down(VkLWin));
        d.Decode(Down(VkLShift));
        d.Decode(Down(VkF23));
        d.Decode(Up(VkLShift));
        d.Decode(Up(VkLWin));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkF23)));
    }

    [Fact]
    public void F23_WithoutWinShift_PassesThrough()
    {
        var d = new HotkeyDecoder(HotkeyKey.Copilot);
        Assert.Equal(Pass, d.Decode(Down(VkF23)));
        Assert.Equal(Pass, d.Decode(Up(VkF23)));

        d.Decode(Down(VkLWin)); // Win alone is not enough
        Assert.Equal(Pass, d.Decode(Down(VkF23)));
        Assert.Equal(Pass, d.Decode(Up(VkF23)));
        d.Decode(Up(VkLWin));

        d.Decode(Down(VkLShift)); // Shift alone is not enough
        Assert.Equal(Pass, d.Decode(Down(VkF23)));
        Assert.Equal(Pass, d.Decode(Up(VkF23)));
    }

    [Fact]
    public void F23_PassesThrough_WhenHotkeyIsNotCopilot()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        d.Decode(Down(VkLWin));
        d.Decode(Down(VkLShift));
        Assert.Equal(Pass, d.Decode(Down(VkF23)));
        Assert.Equal(Pass, d.Decode(Up(VkF23)));
    }

    [Fact]
    public void Escape_WhileRecording_CancelSwallowed()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt) { RecordingActive = true };
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Cancel), d.Decode(Down(VkEscape, 0x01)));
        Assert.Equal(SwallowOnly, d.Decode(Up(VkEscape, 0x01)));
    }

    [Fact]
    public void Escape_WhileRecording_RepeatDoesNotCancelAgain()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt) { RecordingActive = true };
        d.Decode(Down(VkEscape));
        Assert.Equal(SwallowOnly, d.Decode(Down(VkEscape)));
        Assert.Equal(SwallowOnly, d.Decode(Up(VkEscape)));
    }

    [Fact]
    public void Escape_WhenIdle_PassesThrough()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(Pass, d.Decode(Down(VkEscape)));
        Assert.Equal(Pass, d.Decode(Up(VkEscape)));
    }

    [Fact]
    public void Escape_UpSwallowedOnlyIfDownWas()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);

        // Down passed (idle), recording starts, up arrives: up must pass.
        d.Decode(Down(VkEscape));
        d.RecordingActive = true;
        Assert.Equal(Pass, d.Decode(Up(VkEscape)));

        // Down swallowed (recording), recording ends, up arrives: up must still be swallowed.
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Cancel), d.Decode(Down(VkEscape)));
        d.RecordingActive = false;
        Assert.Equal(SwallowOnly, d.Decode(Up(VkEscape)));

        // And the next idle Esc is unaffected.
        Assert.Equal(Pass, d.Decode(Down(VkEscape)));
        Assert.Equal(Pass, d.Decode(Up(VkEscape)));
    }

    [Fact]
    public void Escape_RepeatAfterRecordingEnds_PassesAndItsUpPasses()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt) { RecordingActive = true };
        d.Decode(Down(VkEscape));
        d.RecordingActive = false;
        Assert.Equal(Pass, d.Decode(Down(VkEscape))); // reaches the app
        Assert.Equal(Pass, d.Decode(Up(VkEscape)));   // so its up must too
    }

    [Fact]
    public void InjectedEvents_PassThrough()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt) { RecordingActive = true };
        Assert.Equal(Pass, d.Decode(Down(VkRMenu, 0x38, true, injected: true)));
        Assert.Equal(Pass, d.Decode(Up(VkRMenu, 0x38, true, injected: true)));
        Assert.Equal(Pass, d.Decode(Down(VkEscape, injected: true)));
        Assert.Equal(Pass, d.Decode(Down(VkLControl, 0x21D, injected: true)));

        // No state changed: the first real down is a Pressed, not a repeat.
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRMenu, 0x38, true)));
    }

    [Fact]
    public void InjectedEvents_DoNotFeedCopilotModifierTracking()
    {
        var d = new HotkeyDecoder(HotkeyKey.Copilot);
        d.Decode(Down(VkLWin, injected: true));
        d.Decode(Down(VkLShift, injected: true));
        Assert.Equal(Pass, d.Decode(Down(VkF23)));
    }

    [Fact]
    public void InjectedHeldKey_DoesNotReleaseRealHold()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        d.Decode(Down(VkRMenu, 0x38, true));
        Assert.Equal(Pass, d.Decode(Up(VkRMenu, 0x38, true, injected: true)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Released), d.Decode(Up(VkRMenu, 0x38, true)));
    }

    [Fact]
    public void Reset_ClearsHeldState()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRMenu, 0x38, true)));
        d.Reset();
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRMenu, 0x38, true)));
    }

    [Fact]
    public void Reset_ClearsCopilotModifiersAndEscape()
    {
        var d = new HotkeyDecoder(HotkeyKey.Copilot) { RecordingActive = true };
        d.Decode(Down(VkLWin));
        d.Decode(Down(VkLShift));
        d.Decode(Down(VkEscape));
        d.Reset();
        Assert.Equal(Pass, d.Decode(Down(VkF23)));      // modifiers forgotten
        Assert.Equal(Pass, d.Decode(Up(VkEscape)));     // swallowed-down forgotten
    }

    [Fact]
    public void UpWithoutDecodedDown_PassesThroughWithoutEvent()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(Pass, d.Decode(Up(VkRMenu, 0x38, true)));
        d = new HotkeyDecoder(HotkeyKey.RightCtrl);
        Assert.Equal(Pass, d.Decode(Up(VkRControl, 0x1D, true)));
    }

    [Fact]
    public void KeySwitchedToRightAltWhileItIsHeld_UpPasses()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightCtrl);
        Assert.Equal(Pass, d.Decode(Down(VkRMenu, 0x38, true)));
        d.Key = HotkeyKey.RightAlt;
        Assert.Equal(Pass, d.Decode(Up(VkRMenu, 0x38, true)));
    }

    [Fact]
    public void AltGr_FakeLeftCtrlUp_WithoutSwallowedDown_Passes()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(Pass, d.Decode(Up(VkLControl, 0x21D)));
    }

    [Fact]
    public void AltGr_FakeLeftCtrlUp_SwallowedOnlyOnce()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        Assert.Equal(SwallowOnly, d.Decode(Down(VkLControl, 0x21D)));
        Assert.Equal(SwallowOnly, d.Decode(Up(VkLControl, 0x21D)));
        Assert.Equal(Pass, d.Decode(Up(VkLControl, 0x21D)));
    }

    [Fact]
    public void AltGr_FakeLeftCtrlUp_AfterReset_Passes()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        d.Decode(Down(VkLControl, 0x21D));
        d.Reset();
        Assert.Equal(Pass, d.Decode(Up(VkLControl, 0x21D)));
    }

    [Fact]
    public void AltGr_FakeLeftCtrlDownPassedUnderOtherKey_UpPassesAfterSwitchToRightAlt()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightCtrl);
        Assert.Equal(Pass, d.Decode(Down(VkLControl, 0x21D)));
        d.Key = HotkeyKey.RightAlt;
        Assert.Equal(Pass, d.Decode(Up(VkLControl, 0x21D)));
    }

    [Fact]
    public void Reset_WhileCopilotF23Held_F23UpPassesWithNoEvent()
    {
        var d = new HotkeyDecoder(HotkeyKey.Copilot);
        d.Decode(Down(VkLWin));
        d.Decode(Down(VkLShift));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed, true), d.Decode(Down(VkF23)));
        d.Reset();
        Assert.Equal(Pass, d.Decode(Up(VkF23)));
    }

    [Fact]
    public void Reset_WithStaleSwallowedEsc_RecordingActive_EscUpPasses()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt) { RecordingActive = true };
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Cancel), d.Decode(Down(VkEscape)));
        d.Reset();
        Assert.True(d.RecordingActive);
        Assert.Equal(Pass, d.Decode(Up(VkEscape)));
    }

    [Fact]
    public void SettingKey_ResetsAndSwitchesHotkey()
    {
        var d = new HotkeyDecoder(HotkeyKey.RightAlt);
        d.Decode(Down(VkRMenu, 0x38, true));
        d.Key = HotkeyKey.RightCtrl;
        Assert.Equal(HotkeyKey.RightCtrl, d.Key);
        Assert.Equal(Pass, d.Decode(Up(VkRMenu, 0x38, true)));
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRControl, 0x1D, true)));
        d.Key = HotkeyKey.RightCtrl; // setting even the same value resets held state
        Assert.Equal(new DecodeResult(true, HotkeyEvent.Pressed), d.Decode(Down(VkRControl, 0x1D, true)));
    }
}
