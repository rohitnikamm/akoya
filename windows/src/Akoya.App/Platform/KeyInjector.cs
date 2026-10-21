using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Akoya.App.Platform;

/// <summary>One injected key transition.</summary>
public readonly record struct KeyStroke(ushort Vk, bool KeyUp, bool Extended);

/// <summary>Synthesises keyboard input with <c>SendInput</c>.</summary>
public static class KeyInjector
{
    /// <summary>Unassigned virtual key used to stop Windows treating Alt or Win alone as a menu or Start press.</summary>
    public const ushort MaskKey = 0xE8;

    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

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

        var held = Modifiers.Where(m => isPhysicallyDown(m.Vk)).ToList();
        var strokes = new List<KeyStroke>();
        if (held.Any(m => m.OpensMenuOrStart))
        {
            strokes.Add(new KeyStroke(MaskKey, false, false));
            strokes.Add(new KeyStroke(MaskKey, true, false));
        }

        strokes.AddRange(held.Select(m => new KeyStroke(m.Vk, true, m.Extended)));
        strokes.Add(new KeyStroke(VkControl, false, false));
        strokes.Add(new KeyStroke(VkV, false, false));
        strokes.Add(new KeyStroke(VkV, true, false));
        strokes.Add(new KeyStroke(VkControl, true, false));
        return strokes;
    }

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

    private static void Send(IReadOnlyList<KeyStroke> strokes)
    {
        var inputs = new NativeMethods.Input[strokes.Count];
        for (var i = 0; i < inputs.Length; i++)
        {
            var stroke = strokes[i];
            var flags = (stroke.KeyUp ? NativeMethods.KeyEventFKeyUp : 0u)
                        | (stroke.Extended ? NativeMethods.KeyEventFExtendedKey : 0u);
            inputs[i] = new NativeMethods.Input
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeybdInput
                    {
                        Vk = stroke.Vk,
                        Scan = (ushort)NativeMethods.MapVirtualKey(stroke.Vk, 0),
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
