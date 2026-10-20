using Akoya.Core.Audio;
using Xunit;

namespace Akoya.Core.Tests;

public class LevelMeterTests
{
    private const int Columns = 22;

    [Fact]
    public void Silence_IsStepOne() => Assert.Equal(1, LevelMeter.Step(0));

    [Fact]
    public void FullScale_IsStepEight() => Assert.Equal(8, LevelMeter.Step(1.0f));

    [Fact]
    public void Minus30dB_IsStepFour() =>
        Assert.Equal(4, LevelMeter.Step((float)Math.Pow(10.0, -30.0 / 20.0)));

    [Fact]
    public void Starts_AllOnes() =>
        Assert.Equal(Enumerable.Repeat(1, Columns), new LevelMeter(Columns).Columns);

    [Fact]
    public void DefaultColumnCount_Is23() => Assert.Equal(23, new LevelMeter().Columns.Count);

    [Fact]
    public void Newest_IsOnRight()
    {
        var m = new LevelMeter(Columns);
        m.Push(1);
        Assert.Equal(8, m.Columns[^1]);
        Assert.Equal(Columns, m.Columns.Count);
    }

    [Fact]
    public void Release_FallsAtMostTwoSteps()
    {
        var m = new LevelMeter(Columns);
        m.Push(1);
        m.Push(0);
        Assert.Equal(6, m.Columns[^1]);
    }

    [Fact]
    public void Reset_ReturnsToAllOnes()
    {
        var m = new LevelMeter(Columns);
        m.Push(1);
        m.Reset();
        Assert.Equal(Enumerable.Repeat(1, Columns), m.Columns);
    }

    [Fact]
    public void ClampsAboveFullScale() => Assert.Equal(8, LevelMeter.Step(10));

    [Fact]
    public void Infinity_IsStepEight() => Assert.Equal(8, LevelMeter.Step(float.PositiveInfinity));

    [Fact]
    public void NaNAndNegative_AreStepOne()
    {
        Assert.Equal(1, LevelMeter.Step(float.NaN));
        Assert.Equal(1, LevelMeter.Step(-0.5f));
    }

    [Fact]
    public void Minus50dB_IsStepOne_AndMinus10dB_IsStepEight()
    {
        Assert.Equal(1, LevelMeter.Step((float)Math.Pow(10.0, -50.0 / 20.0) * 1.001f));
        Assert.Equal(8, LevelMeter.Step((float)Math.Pow(10.0, -10.0 / 20.0) * 1.001f));
    }
}
