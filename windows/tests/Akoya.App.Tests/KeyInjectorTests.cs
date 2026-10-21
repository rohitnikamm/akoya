using Akoya.App.Platform;
using Xunit;

namespace Akoya.App.Tests;

public class KeyInjectorTests
{
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
    public void Typing_SurrogatePairs_AreSentAsTwoUnits_AndNewlinesAsEnter()
    {
        var sequence = KeyInjector.BuildTypingSequence("\U0001F600\r\n", _ => false);

        Assert.Equal(
            new[]
            {
                new KeyStroke(0, false, false, '\uD83D'),
                new KeyStroke(0, true, false, '\uD83D'),
                new KeyStroke(0, false, false, '\uDE00'),
                new KeyStroke(0, true, false, '\uDE00'),
                new KeyStroke(0x0D, false, false),
                new KeyStroke(0x0D, true, false),
            },
            sequence);
    }
}
