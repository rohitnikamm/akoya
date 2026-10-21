namespace Akoya.Core.Ui;

/// <summary>The setup card's voice glow (the Mac's onboarding Try it step).</summary>
public static partial class RimGlowRecipes
{
    /// <summary>
    /// The setup card's rim glow while the user speaks on Try it: the Mac's card recipe
    /// (docs/design/blue-glow-target.html <c>CARD</c>, docs/design/tryit-glow-target.png), lit from below with a thin
    /// blue rim on the sides and top. Inset only; drawn over the card's fixed glow at
    /// <see cref="GlowEnvelope.LayerOpacity"/> of the voice.
    /// </summary>
    public static readonly IReadOnlyList<BoxShadow> SetupCardListening =
    [
        new(0, -6, 10, -2, new ShadowColor(200, 228, 255, 0.95), Inset: true),   // white-hot edge
        new(0, -26, 34, -8, new ShadowColor(90, 160, 255, 0.9), Inset: true),
        new(0, -70, 70, -20, new ShadowColor(30, 100, 255, 0.7), Inset: true),
        new(0, -130, 110, -40, new ShadowColor(20, 70, 220, 0.4), Inset: true),
        new(0, 12, 26, -14, new ShadowColor(90, 150, 255, 0.35), Inset: true),   // soft top
        new(0, 0, 5, 1, new ShadowColor(110, 170, 255, 0.45), Inset: true),      // thin rim
        new(0, 0, 22, 4, new ShadowColor(30, 90, 230, 0.4), Inset: true),
        new(0, 0, 50, 10, new ShadowColor(14, 50, 170, 0.3), Inset: true),
    ];
}
