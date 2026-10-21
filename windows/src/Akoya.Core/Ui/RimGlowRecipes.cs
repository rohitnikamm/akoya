namespace Akoya.Core.Ui;

/// <summary>
/// The box-shadow recipes of the mockups (docs/design/windows-pill-mockup.html, windows-onboarding-mockup.html),
/// first shadow on top as in CSS.
/// </summary>
public static class RimGlowRecipes
{
    public static readonly ShadowColor Accent = new(0x2F, 0x6B, 0xFF, 1);
    public static readonly ShadowColor Red = new(0xEF, 0x44, 0x44, 1);

    /// <summary>The blue pill: thin rim, bottom-weighted glow, bright bottom edge, blue glow below, drop shadow.</summary>
    public static readonly IReadOnlyList<BoxShadow> PillBlue =
    [
        new(0, 0, 0, 1, new ShadowColor(140, 170, 255, 0.18), Inset: true),
        new(0, -8, 14, -7, new ShadowColor(47, 107, 255, 0.85), Inset: true),
        new(0, -2, 3, -1, new ShadowColor(150, 185, 255, 0.9), Inset: true),
        new(0, 12, 40, 0, new ShadowColor(47, 107, 255, 0.32)),
        new(0, 8, 24, 0, new ShadowColor(0, 0, 0, 0.5)),
    ];

    /// <summary>The red alert pill (<c>.pill.red</c>).</summary>
    public static readonly IReadOnlyList<BoxShadow> PillRed =
    [
        new(0, 0, 0, 1, new ShadowColor(255, 150, 150, 0.25), Inset: true),
        new(0, -8, 14, -7, new ShadowColor(239, 68, 68, 0.95), Inset: true),
        new(0, -2, 3, -1, new ShadowColor(255, 170, 170, 0.9), Inset: true),
        new(0, 12, 40, 0, new ShadowColor(239, 68, 68, 0.38)),
        new(0, 8, 24, 0, new ShadowColor(0, 0, 0, 0.5)),
    ];

    /// <summary>
    /// The pill recipe for a glow colour: the exact mockup values for the accent blue and the alert red, and for any
    /// other colour the blue recipe re-tinted (edge and rim mixed halfway toward white).
    /// </summary>
    public static IReadOnlyList<BoxShadow> Pill(ShadowColor glow)
    {
        if (SameRgb(glow, Accent)) return PillBlue;
        if (SameRgb(glow, Red)) return PillRed;
        var edge = Mix(glow, 0.5);
        var rim = Mix(glow, 0.44);
        return
        [
            new(0, 0, 0, 1, rim with { A = 0.18 }, Inset: true),
            new(0, -8, 14, -7, glow with { A = 0.85 }, Inset: true),
            new(0, -2, 3, -1, edge with { A = 0.9 }, Inset: true),
            new(0, 12, 40, 0, glow with { A = 0.32 }),
            new(0, 8, 24, 0, new ShadowColor(0, 0, 0, 0.5)),
        ];
    }

    /// <summary>A soft glow around a small badge, e.g. the Pasted check (<c>0 0 14px rgba(47,107,255,.8)</c>).</summary>
    public static IReadOnlyList<BoxShadow> Halo(ShadowColor color, double blur) => [new(0, 0, blur, 0, color)];

    private static bool SameRgb(ShadowColor a, ShadowColor b) => a.R == b.R && a.G == b.G && a.B == b.B;

    private static ShadowColor Mix(ShadowColor c, double towardWhite) => new(
        (byte)Math.Round(c.R + (255 - c.R) * towardWhite),
        (byte)Math.Round(c.G + (255 - c.G) * towardWhite),
        (byte)Math.Round(c.B + (255 - c.B) * towardWhite),
        c.A);
}
