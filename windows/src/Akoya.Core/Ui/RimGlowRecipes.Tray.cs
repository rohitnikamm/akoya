namespace Akoya.Core.Ui;

/// <summary>The tray panel's shadows (docs/design/all-in-one-dropdown-target.html; the Mac dropdown's recipes).</summary>
public static partial class RimGlowRecipes
{
    /// <summary>The panel: a white top highlight and a deep drop shadow (<c>.panel</c>).</summary>
    public static readonly IReadOnlyList<BoxShadow> TrayPanel =
    [
        new(0, 1, 0, 0, new ShadowColor(255, 255, 255, 0.06), Inset: true),
        new(0, 30, 70, 0, new ShadowColor(0, 0, 0, 0.6)),
    ];

    /// <summary>
    /// The status card's rim glow while listening: the Mac's compact recipe (docs/design/blue-glow-target.html
    /// <c>COMPACT</c>, sized for a ~292×152 card), lit from below with a thin blue rim on the sides and top.
    /// Inset only, so nothing is drawn outside the card.
    /// </summary>
    public static readonly IReadOnlyList<BoxShadow> TrayStatusCardListening =
    [
        new(0, -4, 7, -2, new ShadowColor(200, 228, 255, 0.95), Inset: true),  // white-hot edge
        new(0, -16, 22, -6, new ShadowColor(90, 160, 255, 0.9), Inset: true),
        new(0, -40, 44, -14, new ShadowColor(30, 100, 255, 0.7), Inset: true),
        new(0, -70, 60, -24, new ShadowColor(20, 70, 220, 0.4), Inset: true),
        new(0, 8, 16, -9, new ShadowColor(90, 150, 255, 0.35), Inset: true),    // soft top
        new(0, 0, 4, 1, new ShadowColor(110, 170, 255, 0.45), Inset: true),     // thin rim
        new(0, 0, 14, 3, new ShadowColor(30, 90, 230, 0.4), Inset: true),
        new(0, 0, 30, 6, new ShadowColor(14, 50, 170, 0.3), Inset: true),
    ];

    /// <summary>The hotkey and microphone menus: a white hairline and a drop shadow.</summary>
    public static readonly IReadOnlyList<BoxShadow> TrayMenu =
    [
        new(0, 0, 0, 1, new ShadowColor(255, 255, 255, 0.09), Inset: true),
        new(0, 14, 34, 0, new ShadowColor(0, 0, 0, 0.6)),
    ];

    /// <summary>The status chip's dot halo for Ready and Busy (<c>.chip i</c>: <c>0 0 6px #60A5FA</c>).</summary>
    public static readonly IReadOnlyList<BoxShadow> StatusDotBlue =
    [
        new(0, 0, 6, 0, new ShadowColor(0x60, 0xA5, 0xFA, 1)),
    ];

    /// <summary>The status dot's halo for warnings: the blue recipe in amber (#F59E0B under the #FBBF24 dot).</summary>
    public static readonly IReadOnlyList<BoxShadow> StatusDotAmber =
    [
        new(0, 0, 6, 0, new ShadowColor(0xF5, 0x9E, 0x0B, 1)),
    ];

    /// <summary>The status dot's halo for errors: the blue recipe in red (#DC2626 under the #EF4444 dot).</summary>
    public static readonly IReadOnlyList<BoxShadow> StatusDotRed =
    [
        new(0, 0, 6, 0, new ShadowColor(0xDC, 0x26, 0x26, 1)),
    ];

    /// <summary>Each dot of the dot logo (<c>0 0 4px rgba(147,197,253,.6)</c>).</summary>
    public static readonly IReadOnlyList<BoxShadow> LogoDot =
    [
        new(0, 0, 4, 0, new ShadowColor(147, 197, 253, 0.6)),
    ];

    /// <summary>The neutral switch's thumb while on (<c>0 1px 3px rgba(0,0,0,.4)</c>).</summary>
    public static readonly IReadOnlyList<BoxShadow> SwitchThumb =
    [
        new(0, 1, 3, 0, new ShadowColor(0, 0, 0, 0.4)),
    ];
}
