using Akoya.Core.Ui;

namespace Akoya.App.Setup;

/// <summary>
/// The setup window's box-shadows (docs/design/windows-onboarding-mockup.html), as static recipes so
/// <see cref="Controls.RimGlowCache"/> rasterises each once.
/// </summary>
public static class SetupGlows
{
    /// <summary>The card (<c>.card</c>): rim, bottom-weighted blue glow, soft blue wash, drop shadow.</summary>
    public static IReadOnlyList<BoxShadow> Card => RimGlowRecipes.OnboardingCard;

    /// <summary>The card's voice glow on Try it (the Mac's card recipe), drawn at the voice's strength.</summary>
    public static IReadOnlyList<BoxShadow> CardVoice => RimGlowRecipes.SetupCardListening;

    /// <summary>The blue button (<c>.btn.blue</c>): <c>0 6px 22px rgba(47,107,255,.35)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> BlueButton = [new(0, 6, 22, 0, new ShadowColor(47, 107, 255, 0.35))];

    /// <summary>The selected row's radio dot: <c>0 0 8px rgba(47,107,255,.9)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> RadioDot = RimGlowRecipes.Halo(new ShadowColor(47, 107, 255, 0.9), 8);

    /// <summary>The selected row's meter dots: <c>0 0 4px rgba(96,165,250,.7)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> MeterDot = RimGlowRecipes.Halo(new ShadowColor(96, 165, 250, 0.7), 4);

    /// <summary>The Welcome logo's dots: <c>0 0 8px rgba(147,197,253,.7)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> LogoDot = RimGlowRecipes.Halo(new ShadowColor(147, 197, 253, 0.7), 8);

    /// <summary>The Try it waveform's dots: <c>0 0 4px rgba(96,165,250,.6)</c>.</summary>
    public static readonly IReadOnlyList<BoxShadow> WaveDot = RimGlowRecipes.Halo(new ShadowColor(96, 165, 250, 0.6), 4);
}
