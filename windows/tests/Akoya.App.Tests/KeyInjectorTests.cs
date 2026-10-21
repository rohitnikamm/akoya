using Akoya.App.Platform;
using Xunit;

namespace Akoya.App.Tests;

[Collection(DesktopCollection.Name)]
public class KeyInjectorTests
{
    /// <summary>
    /// The typing fallback into a real WPF TextBox: accents, a surrogate pair, a new line. Keys are only injected while
    /// the test's own window holds the foreground, so they can never land in another app; when Windows won't give
    /// it the foreground (a desktop in use) the check is skipped.
    /// </summary>
    [Fact]
    public void SendText_TypesAccentsEmojiAndNewLines_IntoARealTextBox()
    {
        UiThread.Run(() =>
        {
            var box = new System.Windows.Controls.TextBox { AcceptsReturn = true, AcceptsTab = true };
            var window = new System.Windows.Window { Width = 300, Height = 120, Content = box, Topmost = true, Title = "Akoya typing test" };
            window.Show();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            window.Activate();
            box.Focus();
            UiThread.Pump(TimeSpan.FromMilliseconds(200));
            try
            {
                if (Tray.TrayNative.GetForegroundWindow() != hwnd || !box.IsKeyboardFocused) return; // never type elsewhere

                const string text = "naïve café ünd 😀\nnext\tline ";
                KeyInjector.SendText(text);
                UiThread.PumpUntil(() => box.Text.Length >= text.Length || Tray.TrayNative.GetForegroundWindow() != hwnd, 5000);

                Assert.Equal(text.Replace("\n", "\r\n", StringComparison.Ordinal).Replace('\t', ' '), box.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private const ushort Ctrl = 0x11;
    private const ushort V = 0x56;

    [Fact]
    public void CtrlV_NoModifiersHeld_IsJustCtrlV()
    {
        var sequence = KeyInjector.BuildCtrlVSequence(_ => false);

        Assert.Equal(
            new[]
            {
                new KeyStroke(Ctrl, false, false),
                new KeyStroke(V, false, false),
                new KeyStroke(V, true, false),
                new KeyStroke(Ctrl, true, false),
            },
            sequence);
    }

    [Fact]
    public void CtrlV_ShiftHeld_ReleasesShiftFirst()
    {
        var sequence = KeyInjector.BuildCtrlVSequence(vk => vk == 0xA0);

        Assert.Equal(new KeyStroke(0xA0, true, false), sequence[0]);
        Assert.Equal(new KeyStroke(Ctrl, false, false), sequence[1]);
        Assert.Equal(5, sequence.Count);
    }

    [Fact]
    public void CtrlV_RightAltHeld_MasksThenReleasesWithExtendedFlag()
    {
        var sequence = KeyInjector.BuildCtrlVSequence(vk => vk == 0xA5);

        Assert.Equal(
            new[]
            {
                new KeyStroke(KeyInjector.MaskKey, false, false),
                new KeyStroke(KeyInjector.MaskKey, true, false),
                new KeyStroke(0xA5, true, true),
            },
            sequence.Take(3));
        Assert.Equal(new KeyStroke(Ctrl, false, false), sequence[3]);
    }

    [Fact]
    public void Typing_NoModifiersHeld_SendsEachCharacterAsUnicodeDownUp()
    {
        var sequence = KeyInjector.BuildTypingSequence("hé", _ => false);

        Assert.Equal(
            new[]
            {
                new KeyStroke(0, false, false, 'h'),
                new KeyStroke(0, true, false, 'h'),
                new KeyStroke(0, false, false, '\u00e9'),
                new KeyStroke(0, true, false, '\u00e9'),
            },
            sequence);
    }

    [Fact]
    public void Typing_ReleasesHeldModifiersFirst_LikeCtrlV()
    {
        var sequence = KeyInjector.BuildTypingSequence("a", vk => vk == 0xA5);

        Assert.Equal(
            new[]
            {
                new KeyStroke(KeyInjector.MaskKey, false, false),
                new KeyStroke(KeyInjector.MaskKey, true, false),
                new KeyStroke(0xA5, true, true),
                new KeyStroke(0, false, false, 'a'),
                new KeyStroke(0, true, false, 'a'),
            },
            sequence);
    }

    [Fact]
    public void Typing_SurrogatePairs_AreSentAsTwoUnits_AndNewlinesAsShiftEnter()
    {
        var sequence = KeyInjector.BuildTypingSequence("\U0001F600\r\n", _ => false);

        Assert.Equal(
            new[]
            {
                new KeyStroke(0, false, false, '\uD83D'),
                new KeyStroke(0, true, false, '\uD83D'),
                new KeyStroke(0, false, false, '\uDE00'),
                new KeyStroke(0, true, false, '\uDE00'),
                // Plain Enter would send a chat message or run a terminal line; Shift+Enter is a new line.
                new KeyStroke(0x10, false, false),
                new KeyStroke(0x0D, false, false),
                new KeyStroke(0x0D, true, false),
                new KeyStroke(0x10, true, false),
            },
            sequence);
    }

    [Fact]
    public void Typing_TabIsASpace_NotTheTabKey()
    {
        // The Tab key would move focus out of the field, and a tab sent as Unicode input is dropped by some apps (WPF).
        var sequence = KeyInjector.BuildTypingSequence("\t", _ => false);

        Assert.Equal(new[] { new KeyStroke(0, false, false, ' '), new KeyStroke(0, true, false, ' ') }, sequence);
    }
}
