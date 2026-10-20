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
    public void Scanner_AtStart_HeadAndTailOnAllRows()
    {
        var d = PillDots.Scanner(0);
        double[] expected = { 1, 0.7, 0.48, 0.32, 0.18 };
        for (var c = 0; c < expected.Length; c++)
            for (var row = 0; row < 4; row++)
                Assert.Equal(expected[c], Dot(d, c, row), Tol);
        Assert.Equal(1.0, Dot(d, 0, 0), Tol);
        Assert.Equal(0.18, Dot(d, 22, 0), Tol);
    }

    [Fact]
    public void Scanner_AtFarEnd_LastColumnIsHead()
    {
        var d = PillDots.Scanner(0.7);
        for (var row = 0; row < 4; row++) Assert.Equal(1.0, Dot(d, 22, row), Tol);
        Assert.Equal(0.18, Dot(d, 0, 0), Tol);
    }

    [Fact]
    public void Scanner_Midway_TwoColumnHead()
    {
        // Mac ran this with 22 columns: head lands at 10.5, so columns 10 and 11 are both lit.
        var d = PillDots.Scanner(0.35, 22);
        Assert.Equal(1.0, Dot(d, 10, 0), Tol);
        Assert.Equal(1.0, Dot(d, 11, 0), Tol);
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
}
