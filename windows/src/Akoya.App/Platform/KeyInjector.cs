using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Akoya.App.Platform;

/// <summary>One injected key transition. A non-zero <paramref name="Unicode"/> is a UTF-16 unit sent as KEYEVENTF_UNICODE (Vk 0).</summary>
public readonly record struct KeyStroke(ushort Vk, bool KeyUp, bool Extended, char Unicode = '\0');

/// <summary>Synthesises keyboard input with <c>SendInput</c>.</summary>
public static class KeyInjector
{
    /// <summary>Unassigned virtual key used to stop Windows treating Alt or Win alone as a menu or Start press.</summary>
    public const ushort MaskKey = 0xE8;

    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;

    private static readonly (ushort Vk, bool Extended, bool OpensMenuOrStart)[] Modifiers =
    {
        (0xA0, false, false), // left shift
        (0xA1, false, false), // right shift
        (0xA2, false, false), // left ctrl
        (0xA3, true, false),  // right ctrl
        (0xA4, false, true),  // left alt
        (0xA5, true, true),   // right alt
        (0x5B, true, true),   // left win
        (0x5C, true, true),   // right win
    };

    /// <summary>
    /// The injected sequence for Ctrl+V: any modifier the user is still physically holding is released
    /// first (so the target sees plain Ctrl+V; held state is not faked back afterwards), then
    /// Ctrl down, V down, V up, Ctrl up. Before releasing Alt or Win a mask key is tapped so the
    /// release does not activate a menu bar or the Start menu.
    /// </summary>
    public static IReadOnlyList<KeyStroke> BuildCtrlVSequence(Func<ushort, bool> isPhysicallyDown)
    {
        ArgumentNullException.ThrowIfNull(isPhysicallyDown);

        var strokes = ReleaseHeldModifiers(isPhysicallyDown);
        strokes.Add(new KeyStroke(VkControl, false, false));
        strokes.Add(new KeyStroke(VkV, false, false));
        strokes.Add(new KeyStroke(VkV, true, false));
        strokes.Add(new KeyStroke(VkControl, true, false));
        return strokes;
    }

    /// <summary>
    /// The injected sequence that types <paramref name="text"/> without the clipboard: held modifiers are released
    /// first, as for Ctrl+V, then each UTF-16 unit goes down and up as KEYEVENTF_UNICODE input (a surrogate pair is
    /// two units, which Windows recombines). Line breaks are Enter and tabs Tab, which apps expect as keys.
    /// </summary>
    public static IReadOnlyList<KeyStroke> BuildTypingSequence(string text, Func<ushort, bool> isPhysicallyDown)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(isPhysicallyDown);

        var strokes = ReleaseHeldModifiers(isPhysicallyDown);
        for (var i = 0; i < text.Length; i++)
        {
            var unit = text[i];
            if (unit == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                continue; // CRLF is one Enter
            }

            if (unit is '\r' or '\n' or '\t')
            {
                var vk = unit == '\t' ? VkTab : VkReturn;
                strokes.Add(new KeyStroke(vk, false, false));
                strokes.Add(new KeyStroke(vk, true, false));
                continue;
            }

            strokes.Add(new KeyStroke(0, false, false, unit));
            strokes.Add(new KeyStroke(0, true, false, unit));
        }

        return strokes;
    }

    /// <summary>Types text into the foreground window. Throws <see cref="Win32Exception"/> if the input was blocked.</summary>
    public static void SendText(string text) =>
        Send(BuildTypingSequence(text, vk => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0));

    /// <summary>Sends Ctrl+V to the foreground window. Throws <see cref="Win32Exception"/> if the input was blocked.</summary>
    public static void SendCtrlV() =>
        Send(BuildCtrlVSequence(vk => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0));

    /// <summary>Taps the mask key (down, up). Best effort.</summary>
    internal static void SendMaskKey()
    {
        try
        {
            Send(new[] { new KeyStroke(MaskKey, false, false), new KeyStroke(MaskKey, true, false) });
        }
        catch (Win32Exception)
        {
            // Nothing sensible to do: at worst Start opens when the Copilot key's Win is released.
        }
    }

    /// <summary>Releases what the user still holds (masking Alt and Win first so no menu or Start opens).</summary>
    private static List<KeyStroke> ReleaseHeldModifiers(Func<ushort, bool> isPhysicallyDown)
    {
        var held = Modifiers.Where(m => isPhysicallyDown(m.Vk)).ToList();
        var strokes = new List<KeyStroke>();
        if (held.Any(m => m.OpensMenuOrStart))
        {
            strokes.Add(new KeyStroke(MaskKey, false, false));
            strokes.Add(new KeyStroke(MaskKey, true, false));
        }

        strokes.AddRange(held.Select(m => new KeyStroke(m.Vk, true, m.Extended)));
        return strokes;
    }

    private static void Send(IReadOnlyList<KeyStroke> strokes)
    {
        var inputs = new NativeMethods.Input[strokes.Count];
        for (var i = 0; i < inputs.Length; i++)
        {
            var stroke = strokes[i];
            var unicode = stroke.Unicode != '\0';
            var flags = (stroke.KeyUp ? NativeMethods.KeyEventFKeyUp : 0u)
                        | (stroke.Extended ? NativeMethods.KeyEventFExtendedKey : 0u)
                        | (unicode ? NativeMethods.KeyEventFUnicode : 0u);
            inputs[i] = new NativeMethods.Input
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeybdInput
                    {
                        Vk = unicode ? (ushort)0 : stroke.Vk,
                        Scan = unicode ? stroke.Unicode : (ushort)NativeMethods.MapVirtualKey(stroke.Vk, 0),
                        Flags = flags,
                    },
                },
            };
        }

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput was blocked or only partly accepted.");
        }
    }
}
