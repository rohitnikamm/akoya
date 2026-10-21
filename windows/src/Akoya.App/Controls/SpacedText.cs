using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Akoya.App.Controls;

/// <summary>
/// One line of text with CSS <c>letter-spacing</c> (<see cref="Tracking"/> DIP after every character), which
/// <c>TextBlock</c> can't do; for the mockups' small uppercase labels ("LAST DICTATION", "HOTKEY"). Font and colour
/// come from the inherited <see cref="TextElement"/> properties, like a TextBlock. Uppercases nothing itself.
/// </summary>
public sealed class SpacedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SpacedText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(SpacedText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(SpacedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(SpacedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(SpacedText), new FrameworkPropertyMetadata(FontWeights.Normal,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(SpacedText), new FrameworkPropertyMetadata(Brushes.White,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<(FormattedText Glyph, double X)> _glyphs = [];
    private double _height;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Extra space after each character, DIP (CSS <c>letter-spacing</c>; .05em at 11 px is 0.55).</summary>
    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _glyphs.Clear();
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var x = 0.0;
        _height = new FormattedText(" ", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize,
            Foreground, pixelsPerDip).Height;

        var text = Text ?? string.Empty;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var glyph = new FormattedText(enumerator.GetTextElement(), CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, FontSize, Foreground, pixelsPerDip);
            _glyphs.Add((glyph, x));
            x += glyph.WidthIncludingTrailingWhitespace + Tracking;
        }

        // CSS puts the spacing after the last letter too; keep it so layouts match the mockups.
        return new Size(x, _height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var foreground = Foreground;
        foreach (var (glyph, x) in _glyphs)
        {
            glyph.SetForegroundBrush(foreground);
            drawingContext.DrawText(glyph, new Point(x, 0));
        }
    }
}
