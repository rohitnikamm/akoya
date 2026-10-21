using System.Windows;
using System.Windows.Media;
using Akoya.App.Tests.Snapshots;
using Xunit;

namespace Akoya.App.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData("Akoya.Surface", "#FF03060F")]
    [InlineData("Akoya.Accent", "#FF2F6BFF")]
    [InlineData("Akoya.AccentDark", "#FF1D4ED8")]
    [InlineData("Akoya.AccentLight", "#FF93C5FD")]
    [InlineData("Akoya.Amber", "#FFFBBF24")]
    [InlineData("Akoya.Red", "#FFEF4444")]
    [InlineData("Akoya.GreenText", "#FF86EFAC")]
    [InlineData("Akoya.GreenDot", "#FF4ADE80")]
    [InlineData("Akoya.TextPrimary", "#FFFFFFFF")]
    [InlineData("Akoya.TextBody", "#E6FFFFFF")]
    [InlineData("Akoya.TextMuted", "#99FFFFFF")]
    [InlineData("Akoya.TextEyebrow", "#80FFFFFF")]
    public void Brushes_HaveThePaletteColours(string key, string color)
    {
        SnapshotHarness.RunSta(() =>
        {
            var brush = Assert.IsType<SolidColorBrush>(PillSnapshots.Theme()[key]);
            Assert.Equal(color, brush.Color.ToString());
        });
    }

    [Fact]
    public void FontFamily_IsTheEmbeddedOutfit()
    {
        SnapshotHarness.RunSta(() =>
        {
            var family = Assert.IsType<FontFamily>(PillSnapshots.Theme()["Akoya.FontFamily"]);
            foreach (var weight in new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold })
            {
                var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
                Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), $"Outfit {weight} did not load");
                Assert.Contains("Outfit", glyphs.FamilyNames.Values.First(), StringComparison.Ordinal);
            }

            // 400 and 700 are different faces, not one face drawn twice: bold is wider, and real rather than simulated.
            var regular = Measure(family, FontWeights.Normal, out var regularFace);
            var bold = Measure(family, FontWeights.Bold, out var boldFace);
            Assert.True(bold > regular * 1.015, $"Outfit 700 ({bold:F2}) is not wider than 400 ({regular:F2})");
            Assert.Equal(StyleSimulations.None, regularFace.StyleSimulations);
            Assert.Equal(StyleSimulations.None, boldFace.StyleSimulations);
        });
    }

    private static double Measure(FontFamily family, FontWeight weight, out GlyphTypeface face)
    {
        var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
        Assert.True(typeface.TryGetGlyphTypeface(out face));
        var text = new FormattedText("Microphone not detected", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 13, Brushes.White, 1.0);
        return text.WidthIncludingTrailingWhitespace;
    }
}
