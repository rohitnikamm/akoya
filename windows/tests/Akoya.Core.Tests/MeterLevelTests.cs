using Akoya.Core.Mics;
using Xunit;

namespace Akoya.Core.Tests;

public class MeterLevelTests
{
    [Fact]
    public void StepOne_LightsNothing() => Assert.Equal(0, MeterLevel.LitDots(1));

    [Fact]
    public void StepEight_LightsAllTen() => Assert.Equal(10, MeterLevel.LitDots(8));

    [Fact]
    public void StepFour_LightsFour() => Assert.Equal(4, MeterLevel.LitDots(4));

    [Fact]
    public void OutOfRangeSteps_Clamp()
    {
        Assert.Equal(0, MeterLevel.LitDots(0));
        Assert.Equal(10, MeterLevel.LitDots(99));
    }

    [Fact]
    public void CustomDotCount() => Assert.Equal(7, MeterLevel.LitDots(8, 7));
}
