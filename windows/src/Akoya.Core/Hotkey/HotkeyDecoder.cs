namespace Akoya.Core.Hotkey;

/// <summary>The physical key that acts as the push-to-talk key.</summary>
public enum HotkeyKey { RightAlt, RightCtrl, Copilot }

public enum HotkeyEvent { Pressed, Released, Cancel }

/// <summary>One low-level keyboard event (a distilled KBDLLHOOKSTRUCT).</summary>
public readonly record struct RawKey(int VkCode, int ScanCode, bool Extended, bool Injected, bool IsDown);

/// <summary>
/// What the hook should do with the event: <see cref="Swallow"/> hides it from the rest of the
/// system, <see cref="Event"/> is the push-to-talk event to raise (if any), and
/// <see cref="InjectMaskKey"/> asks the hook to inject a mask key so Windows does not open Start
/// when the Win key is released.
/// </summary>
public readonly record struct DecodeResult(bool Swallow, HotkeyEvent? Event, bool InjectMaskKey = false);

/// <summary>
/// Pure translation of raw low-level keyboard events into push-to-talk events. Only the chosen
/// hotkey (and, on AltGr layouts, the fake Left Ctrl that precedes Right Alt) and Esc while
/// recording are ever swallowed. Not thread-safe: call from the single hook-processing thread.
/// </summary>
public sealed class HotkeyDecoder
{
    private const int VkEscape = 0x1B;
    private const int VkLWin = 0x5B;
    private const int VkLShift = 0xA0;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkRMenu = 0xA5;
    private const int VkF23 = 0x86;

    /// <summary>Scan code KBDLLHOOKSTRUCT reports for the fake Left Ctrl Windows injects for AltGr.</summary>
    private const int AltGrFakeCtrlScanCode = 0x21D;

    private static readonly DecodeResult Pass = new(false, null);
    private static readonly DecodeResult SwallowOnly = new(true, null);

    private HotkeyKey _key;
    private bool _hotkeyHeld;
    private bool _escSwallowed;
    private bool _fakeCtrlSwallowed;
    private bool _lWinDown;
    private bool _lShiftDown;

    public HotkeyDecoder(HotkeyKey key)
    {
        _key = key;
    }

    /// <summary>The chosen hotkey. Setting it resets all held state.</summary>
    public HotkeyKey Key
    {
        get => _key;
        set
        {
            _key = value;
            Reset();
        }
    }

    /// <summary>Esc is only intercepted while this is true.</summary>
    public bool RecordingActive { get; set; }

    /// <summary>Forgets all held-key state (e.g. after the hook is re-installed).</summary>
    public void Reset()
    {
        _hotkeyHeld = false;
        _escSwallowed = false;
        _fakeCtrlSwallowed = false;
        _lWinDown = false;
        _lShiftDown = false;
    }

    public DecodeResult Decode(RawKey key)
    {
        // Akoya's own SendInput events must never be decoded or change state.
        if (key.Injected) return Pass;

        switch (key.VkCode)
        {
            case VkLWin:
                _lWinDown = key.IsDown;
                return Pass;
            case VkLShift:
                _lShiftDown = key.IsDown;
                return Pass;
            case VkEscape:
                return DecodeEscape(key.IsDown);
        }

        switch (_key)
        {
            case HotkeyKey.RightAlt:
                if (key.VkCode == VkRMenu) return DecodeHotkey(key.IsDown, inject: false);
                // AltGr layouts: swallow the fake LCtrl so apps never see a stray Ctrl.
                // A real Left Ctrl has scan code 0x1D and passes through. The up is swallowed only
                // if its down was, so a down that reached apps is never left stranded.
                if (key.VkCode == VkLControl && key.ScanCode == AltGrFakeCtrlScanCode)
                {
                    if (key.IsDown)
                    {
                        _fakeCtrlSwallowed = true;
                        return SwallowOnly;
                    }
                    if (!_fakeCtrlSwallowed) return Pass;
                    _fakeCtrlSwallowed = false;
                    return SwallowOnly;
                }
                return Pass;

            case HotkeyKey.RightCtrl:
                return key.VkCode == VkRControl ? DecodeHotkey(key.IsDown, inject: false) : Pass;

            case HotkeyKey.Copilot:
                if (key.VkCode != VkF23) return Pass;
                // The Copilot key is LWin + LShift + F23. A lone F23 is an ordinary key.
                if (key.IsDown && !_hotkeyHeld && !(_lWinDown && _lShiftDown)) return Pass;
                // An F23 up whose down we did not decode as the hotkey was not ours to swallow.
                if (!key.IsDown && !_hotkeyHeld) return Pass;
                return DecodeHotkey(key.IsDown, inject: true);

            default:
                return Pass;
        }
    }

    private DecodeResult DecodeHotkey(bool isDown, bool inject)
    {
        if (isDown)
        {
            // Auto-repeat while held: swallowed, but not a new press.
            if (_hotkeyHeld) return SwallowOnly;
            _hotkeyHeld = true;
            return new DecodeResult(true, HotkeyEvent.Pressed, inject);
        }

        // An up whose down we did not decode must pass: the down may have reached apps (hook gap,
        // Key switched while held), and swallowing the up would strand the key as down system-wide.
        if (!_hotkeyHeld) return Pass;
        _hotkeyHeld = false;
        return new DecodeResult(true, HotkeyEvent.Released);
    }

    private DecodeResult DecodeEscape(bool isDown)
    {
        if (isDown)
        {
            if (!RecordingActive)
            {
                _escSwallowed = false; // this down reaches the app, so its up must too
                return Pass;
            }
            // Auto-repeat of an Esc we already turned into a Cancel: swallow, don't cancel again.
            if (_escSwallowed) return SwallowOnly;
            _escSwallowed = true;
            return new DecodeResult(true, HotkeyEvent.Cancel);
        }

        // Esc up is swallowed only if its down was.
        if (!_escSwallowed) return Pass;
        _escSwallowed = false;
        return SwallowOnly;
    }
}
