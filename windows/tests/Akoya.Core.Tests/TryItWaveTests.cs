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
    public void Opacities_DimAtRest()
    {
        var opacities = TryItWave.Opacities([1], live: false);

        Assert.Equal(TryItWave.RestOpacity, opacities[4]);
        Assert.Equal(0, opacities[3]);
    }
}
