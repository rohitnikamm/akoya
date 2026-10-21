using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class PillDotsTests
{
    private const double Tol = 1e-9;

    private static double Dot(double[] dots, int column, int row) => dots[column * PillDots.Rows + row];

    [Fact]
    public void Rows_IsFour() => Assert.Equal(4, PillDots.Rows);

    [Fact]
    public void Listening_LengthIsColumnsTimesFour()
    {
        Assert.Equal(23 * 4, PillDots.Listening(Enumerable.Repeat(1, 23).ToArray()).Length);
        Assert.Empty(PillDots.Listening(Array.Empty<int>()));
    }

    [Fact]
    public void Listening_StepOne_DimMiddleFaintOuter()
    {
        var d = PillDots.Listening(new[] { 1 });
        Assert.Equal(0.12, Dot(d, 0, 0), Tol);
        Assert.Equal(0.35, Dot(d, 0, 1), Tol);
        Assert.Equal(0.35, Dot(d, 0, 2), Tol);
        Assert.Equal(0.12, Dot(d, 0, 3), Tol);
    }

    [Fact]
    public void Listening_StepTwo_MiddleLit()
    {
        var d = PillDots.Listening(new[] { 2 });
        Assert.Equal(0.12, Dot(d, 0, 0), Tol);
        Assert.Equal(0.95, Dot(d, 0, 1), Tol);
        Assert.Equal(0.95, Dot(d, 0, 2), Tol);
        Assert.Equal(0.12, Dot(d, 0, 3), Tol);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Listening_StepsThreeAndFour_OuterStillFaint(int step)
    {
        var d = PillDots.Listening(new[] { step });
        Assert.Equal(0.12, Dot(d, 0, 0), Tol);
        Assert.Equal(0.95, Dot(d, 0, 1), Tol);
        Assert.Equal(0.95, Dot(d, 0, 2), Tol);
        Assert.Equal(0.12, Dot(d, 0, 3), Tol);
    }

    [Fact]
    public void Listening_StepFive_OuterStartsRising()
    {
        var d = PillDots.Listening(new[] { 5 });
        Assert.Equal(0.12 + 0.83 * (5 - 4) / 3, Dot(d, 0, 0), Tol);
        Assert.Equal(0.95, Dot(d, 0, 1), Tol);
        Assert.Equal(0.12 + 0.83 * (5 - 4) / 3, Dot(d, 0, 3), Tol);
    }

    [Fact]
    public void Listening_StepSix_OuterInterpolates()
    {
        var d = PillDots.Listening(new[] { 6 });
        var outer = 0.12 + 0.83 * (6 - 4) / 3;
        Assert.Equal(outer, Dot(d, 0, 0), Tol);
        Assert.Equal(0.95, Dot(d, 0, 1), Tol);
        Assert.Equal(0.95, Dot(d, 0, 2), Tol);
        Assert.Equal(outer, Dot(d, 0, 3), Tol);
    }

    [Fact]
    public void Listening_StepEight_AllFourLit()
    {
        var d = PillDots.Listening(new[] { 8 });
        for (var row = 0; row < 4; row++) Assert.Equal(0.95, Dot(d, 0, row), Tol);
    }

    [Fact]
    public void Listening_IsColumnMajor()
    {
        var d = PillDots.Listening(new[] { 1, 8 });
        Assert.Equal(8, d.Length);
        Assert.Equal(0.35, d[1], Tol); // column 0, row 1
        Assert.Equal(0.95, d[4], Tol); // column 1, row 0
    }

    [Fact]
    public void Scanner_LengthIsColumnsTimesFour()
    {
        Assert.Equal(23 * 4, PillDots.Scanner(0).Length);
        Assert.Equal(10 * 4, PillDots.Scanner(0, 10).Length);
    }

    [Fact]
    public void Scanner_AtStart_HeadAndTailOnTheCentreRows_OuterRowsAtRest()
    {
        var d = PillDots.Scanner(0);
        double[] expected = { 1, 0.7, 0.48, 0.32, 0.18 };
        for (var c = 0; c < expected.Length; c++)
        {
            Assert.Equal(expected[c], Dot(d, c, 1), Tol);
            Assert.Equal(expected[c], Dot(d, c, 2), Tol);
            Assert.Equal(0.18, Dot(d, c, 0), Tol);
            Assert.Equal(0.18, Dot(d, c, 3), Tol);
        }
        Assert.Equal(0.18, Dot(d, 22, 1), Tol);
    }

    [Fact]
    public void Scanner_AtFarEnd_LastColumnIsHead()
    {
        var d = PillDots.Scanner(0.7);
        Assert.Equal(1.0, Dot(d, 22, 1), Tol);
        Assert.Equal(1.0, Dot(d, 22, 2), Tol);
        Assert.Equal(0.18, Dot(d, 22, 0), Tol);
        Assert.Equal(0.18, Dot(d, 0, 1), Tol);
    }

    [Fact]
    public void Scanner_Midway_TwoColumnHead()
    {
        // Mac ran this with 22 columns: head lands at 10.5, so columns 10 and 11 are both lit.
        var d = PillDots.Scanner(0.35, 22);
        Assert.Equal(1.0, Dot(d, 10, 1), Tol);
        Assert.Equal(1.0, Dot(d, 11, 1), Tol);
    }

    [Fact]
    public void Scanner_Midway_With23Columns_HeadIsMiddleColumn()
    {
        var d = PillDots.Scanner(0.35);
        Assert.Equal(1.0, Dot(d, 11, 2), Tol);
    }

    [Fact]
    public void Scanner_RepeatsEveryPeriod()
    {
        Assert.Equal(PillDots.Scanner(0.2), PillDots.Scanner(0.2 + 1.4 * 3).Select(v => v).ToArray());
    }

    // ---- Solid look ----

    [Fact]
    public void Geometry_ThreePixelDotsTwoApart_FourRowsAre18Tall()
    {
        Assert.Equal(3, PillDots.DotSize);
        Assert.Equal(2, PillDots.Gap);
        Assert.Equal(18, PillDots.MaxBarHeight);
    }

    [Theory]
    [InlineData(1, 3.0)]   // one dot, like the Mac's single cell at level 1
    [InlineData(8, 18.0)]  // the four dot rows' full height
    [InlineData(0, 3.0)]
    [InlineData(-4, 3.0)]
    [InlineData(12, 18.0)]
    public void SolidHeight_OneDotAtRestToFullHeight(int step, double expected) =>
        Assert.Equal(expected, PillDots.SolidHeight(step), Tol);

    [Fact]
    public void SolidHeight_LinearInTheStep_AsOnTheMac()
    {
        var heights = Enumerable.Range(1, 8).Select(PillDots.SolidHeight).ToArray();
        for (var i = 1; i < heights.Length; i++) Assert.Equal(15 / 7.0, heights[i] - heights[i - 1], Tol);
    }

    [Fact]
    public void SolidListening_OneBarPerColumn_HeightFromItsStep_Bright()
    {
        var bars = PillDots.SolidListening([1, 4, 8]);

        Assert.Equal(3, bars.Length);
        Assert.Equal(PillDots.SolidHeight(1), bars[0].Height, Tol);
        Assert.Equal(PillDots.SolidHeight(4), bars[1].Height, Tol);
        Assert.Equal(18, bars[2].Height, Tol);
        Assert.All(bars, b => Assert.Equal(0.95, b.Opacity, Tol));
        Assert.Empty(PillDots.SolidListening([]));
    }

    [Fact]
    public void SolidScanner_CentreTwoRowsTallBars_WithTheScannersOpacities()
    {
        var bars = PillDots.SolidScanner(0.3);
        var dots = PillDots.Scanner(0.3);

        Assert.Equal(23, bars.Length);
        for (var c = 0; c < bars.Length; c++)
        {
            Assert.Equal(8, bars[c].Height, Tol);
            Assert.Equal(Dot(dots, c, 1), bars[c].Opacity, Tol);
        }
        Assert.Equal(10, PillDots.SolidScanner(0, 10).Length);
    }

    // ---- Amber sweep ----

    [Fact]
    public void ScannerLit_TheHeadAndItsTail_NotTheRestingColumns()
    {
        var d = PillDots.Scanner(0);
        var lit = Enumerable.Range(0, 23).Where(c => PillDots.IsScannerLit(Dot(d, c, 1))).ToArray();

        Assert.Equal([0, 1, 2, 3], lit);
        Assert.False(PillDots.IsScannerLit(0.18));
        Assert.True(PillDots.IsScannerLit(0.32));
    }
}
