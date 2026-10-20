using Akoya.Core.Audio;
using Xunit;

namespace Akoya.Core.Tests;

public class RmsWindowerTests
{
    private static float[] Samples(int count, float value = 0.5f) =>
        Enumerable.Repeat(value, count).ToArray();

    [Fact]
    public void WindowsOf640()
    {
        var w = new RmsWindower();
        Assert.Equal(new[] { 0.5f }, w.Append(Samples(1000)));
        Assert.Single(w.Append(Samples(280)));
    }

    [Fact]
    public void PartialWindow_ReturnsNothing()
    {
        var w = new RmsWindower();
        Assert.Empty(w.Append(Samples(639)));
        Assert.Single(w.Append(Samples(1)));
    }

    [Fact]
    public void SeveralWindowsInOneAppend()
    {
        var w = new RmsWindower();
        Assert.Equal(3, w.Append(Samples(640 * 3)).Count);
    }
}
