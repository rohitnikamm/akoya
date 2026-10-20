using Akoya.Core.Mics;
using Xunit;

namespace Akoya.Core.Tests;

public class MicrophoneResolverTests
{
    private static readonly NamedMic Laptop = new("laptop", "This PC's microphone", false, true);
    private static readonly NamedMic Hyperx = new("hyperx", "HyperX Cloud III", false, false);
    private static readonly NamedMic Mix = new("mix", "Stereo Mix", true, false);
    private static readonly NamedMic[] All = { Laptop, Hyperx, Mix };

    [Fact]
    public void NullSavedId_FollowsTheDefault()
    {
        var choice = MicrophoneResolver.Resolve(null, All);
        Assert.Equal(Laptop, choice.Mic);
        Assert.False(choice.Missing);
    }

    [Fact]
    public void NullSavedId_WithNoDefault_IsMissing()
    {
        var choice = MicrophoneResolver.Resolve(null, new[] { Hyperx });
        Assert.Null(choice.Mic);
        Assert.True(choice.Missing);
    }

    [Fact]
    public void NullSavedId_WithNoMicsAtAll_IsMissing()
    {
        var choice = MicrophoneResolver.Resolve(null, Array.Empty<NamedMic>());
        Assert.Null(choice.Mic);
        Assert.True(choice.Missing);
    }

    [Fact]
    public void SavedIdPresent_ChoosesIt()
    {
        var choice = MicrophoneResolver.Resolve("hyperx", All);
        Assert.Equal(Hyperx, choice.Mic);
        Assert.False(choice.Missing);
    }

    [Fact]
    public void SavedIdInMore_ChoosesIt() =>
        Assert.Equal(Mix, MicrophoneResolver.Resolve("mix", All).Mic);

    [Fact]
    public void SavedIdAbsent_IsMissingWithoutFallback()
    {
        var choice = MicrophoneResolver.Resolve("gone", All);
        Assert.Null(choice.Mic);
        Assert.True(choice.Missing);
    }

    [Fact]
    public void SavedIdAbsent_WithNoMics_IsMissing()
    {
        var choice = MicrophoneResolver.Resolve("gone", Array.Empty<NamedMic>());
        Assert.Null(choice.Mic);
        Assert.True(choice.Missing);
    }
}
