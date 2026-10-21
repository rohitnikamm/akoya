using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class CubicBezierTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public void Linear_IsIdentity(double t) => Assert.Equal(t, CubicBezier.Evaluate(0, 0, 1, 1, t), 6);

    [Fact]
    public void EaseInOut_IsSymmetric()
    {
        Assert.Equal(0.5, CubicBezier.Evaluate(0.42, 0, 0.58, 1, 0.5), 6);
        Assert.Equal(1, CubicBezier.Evaluate(0.42, 0, 0.58, 1, 0.2) + CubicBezier.Evaluate(0.42, 0, 0.58, 1, 0.8), 6);
        Assert.True(CubicBezier.Evaluate(0.42, 0, 0.58, 1, 0.2) < 0.2);
    }

    [Fact]
    public void EndsAreFixed_AndTimeIsClamped()
    {
        Assert.Equal(0, CubicBezier.Evaluate(0.2, 0.9, 0.3, 1.2, 0));
        Assert.Equal(1, CubicBezier.Evaluate(0.2, 0.9, 0.3, 1.2, 1));
        Assert.Equal(0, CubicBezier.Evaluate(0.2, 0.9, 0.3, 1.2, -3));
        Assert.Equal(1, CubicBezier.Evaluate(0.2, 0.9, 0.3, 1.2, 4));
    }

    [Fact]
    public void YAboveOne_Overshoots()
    {
        var peak = Enumerable.Range(1, 99).Max(i => CubicBezier.Evaluate(0.2, 0.9, 0.3, 1.2, i / 100.0));
        Assert.InRange(peak, 1.01, 1.1);
    }
}

public class PillPopInTests
{
    private const double W = 179; // listening pill, DIP
    private const double H = 34;

    [Fact]
    public void Duration_Is420Milliseconds() => Assert.Equal(TimeSpan.FromMilliseconds(420), PillPopIn.Duration);

    [Fact]
    public void Start_SmallInvisibleCircle()
    {
        var f = PillPopIn.At(0, W, H);

        Assert.Equal(H, f.Width);
        Assert.Equal(0.55, f.ScaleX, 6);
        Assert.Equal(0.55, f.ScaleY, 6);
        Assert.Equal(0, f.Opacity);
        Assert.Equal(0, f.ContentOpacity);
    }

    [Fact]
    public void At24Percent_FullSizeOpaqueCircle_ContentsStillHidden()
    {
        var f = PillPopIn.At(0.24, W, H);

        Assert.Equal(H, f.Width, 6);
        Assert.Equal(1, f.ScaleX, 6);
        Assert.Equal(1, f.ScaleY, 6);
        Assert.Equal(1, f.Opacity, 6);
        Assert.Equal(0, f.ContentOpacity);
    }

    [Fact]
    public void At64Percent_StretchedPastFullWidth_Squashed()
    {
        var f = PillPopIn.At(0.64, W, H);

        Assert.Equal(1.07 * W, f.Width, 6);
        Assert.Equal(1, f.ScaleX, 6);
        Assert.Equal(0.92, f.ScaleY, 6);
        Assert.Equal(1, f.Opacity, 6);
    }

    [Fact]
    public void At84Percent_SpringsBackNarrower_Taller()
    {
        var f = PillPopIn.At(0.84, W, H);

        Assert.Equal(0.985 * W, f.Width, 6);
        Assert.Equal(1, f.ScaleX, 6);
        Assert.Equal(1.03, f.ScaleY, 6);
    }

    [Fact]
    public void End_FinalWidthNoScale_EverythingVisible()
    {
        var f = PillPopIn.At(1, W, H);

        Assert.Equal(new PillPopInFrame(W, 1, 1, 1, 1), f);
    }

    [Fact]
    public void FirstSegment_OvershootsSlightly_AsTheMockupMeasures()
    {
        // The mockup's pill is 34.57 px wide (scale 1.017) 50 ms in.
        var f = PillPopIn.At(50 / 420.0, W, H);

        Assert.InRange(f.ScaleX, 1.005, 1.04);
        Assert.Equal(f.ScaleX, f.ScaleY);
        Assert.Equal(H, f.Width);
    }

    [Fact]
    public void Opacity_NeverAboveOne_EvenWhileScaleOvershoots()
    {
        for (var i = 0; i <= 100; i++) Assert.InRange(PillPopIn.At(i / 100.0, W, H).Opacity, 0, 1);
    }

    [Fact]
    public void SecondSegment_UsesItsDecelerateCurve()
    {
        // Halfway through 24..64 %: cubic-bezier(.3,.8,.4,1) is well past halfway.
        var f = PillPopIn.At(0.44, W, H);
        var expected = H + (1.07 * W - H) * CubicBezier.Evaluate(0.3, 0.8, 0.4, 1, 0.5);

        Assert.Equal(expected, f.Width, 6);
        Assert.True(f.Width > (H + 1.07 * W) / 2);
        Assert.Equal(1 - 0.08 * CubicBezier.Evaluate(0.3, 0.8, 0.4, 1, 0.5), f.ScaleY, 6);
    }

    [Fact]
    public void LastSegments_EaseInOut()
    {
        var third = PillPopIn.At(0.74, W, H); // halfway through 64..84 %
        Assert.Equal((1.07 * W + 0.985 * W) / 2, third.Width, 6);
        Assert.Equal((0.92 + 1.03) / 2, third.ScaleY, 6);

        var last = PillPopIn.At(0.92, W, H); // halfway through 84..100 %
        Assert.Equal((0.985 * W + W) / 2, last.Width, 6);
        Assert.Equal((1.03 + 1) / 2, last.ScaleY, 6);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.45, 0.0)]
    [InlineData(0.725, 0.5)]
    [InlineData(1.0, 1.0)]
    public void Contents_FadeInLinearlyFrom45Percent(double progress, double expected) =>
        Assert.Equal(expected, PillPopIn.At(progress, W, H).ContentOpacity, 6);

    [Fact]
    public void ProgressOutsideRange_IsClamped()
    {
        Assert.Equal(PillPopIn.At(0, W, H), PillPopIn.At(-0.5, W, H));
        Assert.Equal(PillPopIn.At(1, W, H), PillPopIn.At(3, W, H));
        Assert.Equal(PillPopIn.At(1, W, H), PillPopIn.At(double.NaN, W, H));
    }

    [Fact]
    public void Width_NeverBelowTheCircle()
    {
        for (var i = 0; i <= 100; i++) Assert.True(PillPopIn.At(i / 100.0, W, H).Width >= H - 1e-9);
        // A pill no wider than it is tall stays a circle.
        Assert.Equal(H, PillPopIn.At(0.64, 20, H).Width);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(210, 0.5)]
    [InlineData(420, 1.0)]
    [InlineData(900, 1.0)]
    [InlineData(-10, 0.0)]
    public void Progress_FromElapsedTime(int ms, double expected) =>
        Assert.Equal(expected, PillPopIn.Progress(TimeSpan.FromMilliseconds(ms)), 6);
}
