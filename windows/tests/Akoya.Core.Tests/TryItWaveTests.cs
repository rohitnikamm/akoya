using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class TryItWaveTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 7)]
    [InlineData(7, 9)]
    [InlineData(8, 9)]
    [InlineData(12, 9)]
    public void DotCount_IsOddAndAtMostNine(int step, int expected) => Assert.Equal(expected, TryItWave.DotCount(step));

    [Fact]
    public void Opacities_CentreEachColumn()
    {
        var opacities = TryItWave.Opacities([1, 8, 3], live: true);

        Assert.Equal(3 * TryItWave.MaxDots, opacities.Length);
        Assert.Equal([0, 0, 0, 0, 1, 0, 0, 0, 0], opacities[..9]);
        Assert.All(opacities[9..18], o => Assert.Equal(1, o));
        Assert.Equal([0, 0, 0, 1, 1, 1, 0, 0, 0], opacities[18..27]);
    }

    [Fact]
    public void Loading_FillsTheCentreLineLeftToRight()
    {
        var opacities = TryItWave.Loading(23, 0.62);

        Assert.Equal(23 * TryItWave.MaxDots, opacities.Length);
        var line = Enumerable.Range(0, 23).Select(c => opacities[c * TryItWave.MaxDots + 4]).ToArray();
        Assert.All(line[..14], o => Assert.Equal(TryItWave.LiveOpacity, o)); // 62% of 23 is 14 lit
        Assert.All(line[14..], o => Assert.Equal(TryItWave.LoadingOpacity, o));
        Assert.All(Enumerable.Range(0, 23 * TryItWave.MaxDots).Where(i => i % TryItWave.MaxDots != 4), i => Assert.Equal(0, opacities[i]));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 23)]
    [InlineData(1.7, 23)]
    public void Loading_UnknownProgressLightsNone_AndItNeverOverflows(double? fraction, int lit)
    {
        var opacities = TryItWave.Loading(23, fraction);

        Assert.Equal(lit, Enumerable.Range(0, 23).Count(c => opacities[c * TryItWave.MaxDots + 4] == TryItWave.LiveOpacity));
    }

    [Fact]
    public void Opacities_DimAtRest()
    {
        var opacities = TryItWave.Opacities([1], live: false);

        Assert.Equal(TryItWave.RestOpacity, opacities[4]);
        Assert.Equal(0, opacities[3]);
    }
}
