import SwiftUI

/// Size of the rim glow recipe: `card` is the onboarding card (420×520), `compact` the
/// dropdown status card (~292×152, about 60% of the card's blur and spread).
public enum RimGlowScale: Sendable {
    case card, compact
}

extension View {
    /// The signature rim glow, lit from below: near-white along the bottom edge, rising
    /// through light blue and electric blue into the dark centre about a quarter of the
    /// card's height, with only a thin blue rim on the sides and top. Nothing is drawn
    /// outside the shape. Drawn behind the content; apply it before the card fill so it sits
    /// on top of the fill, e.g.
    /// `content.akoyaGrid(opacity:).akoyaRimGlow(...).akoyaV1Card(cornerRadius:)`.
    /// - Parameter intensity: voice-driven opacity, 0…1 (see `GlowEnvelope.opacity`).
    public func akoyaRimGlow(intensity: Double, cornerRadius: CGFloat, scale: RimGlowScale) -> some View {
        background {
            InsetShadows(layers: scale.layers, cornerRadius: cornerRadius)
                .compositingGroup()
                .opacity(RimGlowScale.layerOpacity(intensity: intensity))
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }
}

extension RimGlowScale {
    /// Peak voice gives `GlowEnvelope` opacity 0.8, and both approved renders
    /// (docs/design/blue-glow-target.png, "peak") were drawn at full strength.
    static let intensityGain = 1 / 0.8

    /// The layer opacity for a voice-driven `intensity`: peak voice (0.8) draws the approved
    /// peak render exactly; anything above is capped at full strength.
    static func layerOpacity(intensity: Double) -> Double {
        min(max(intensity * intensityGain, 0), 1)
    }

    /// The approved CSS recipes (docs/design/blue-glow-target.html, `CARD` and `COMPACT`),
    /// first layer on top as in CSS.
    var layers: [InsetShadow] {
        switch self {
        case .card:
            [
                InsetShadow(y: -6, blur: 10, spread: -2, color: Color(rgba: 200, 228, 255, 0.95)),  // white-hot edge
                InsetShadow(y: -26, blur: 34, spread: -8, color: Color(rgba: 90, 160, 255, 0.9)),
                InsetShadow(y: -70, blur: 70, spread: -20, color: Color(rgba: 30, 100, 255, 0.7)),
                InsetShadow(y: -130, blur: 110, spread: -40, color: Color(rgba: 20, 70, 220, 0.4)),
                InsetShadow(y: 12, blur: 26, spread: -14, color: Color(rgba: 90, 150, 255, 0.35)),  // soft top
                InsetShadow(blur: 5, spread: 1, color: Color(rgba: 110, 170, 255, 0.45)),  // thin rim
                InsetShadow(blur: 22, spread: 4, color: Color(rgba: 30, 90, 230, 0.4)),
                InsetShadow(blur: 50, spread: 10, color: Color(rgba: 14, 50, 170, 0.3)),
            ]
        case .compact:
            [
                InsetShadow(y: -4, blur: 7, spread: -2, color: Color(rgba: 200, 228, 255, 0.95)),  // white-hot edge
                InsetShadow(y: -16, blur: 22, spread: -6, color: Color(rgba: 90, 160, 255, 0.9)),
                InsetShadow(y: -40, blur: 44, spread: -14, color: Color(rgba: 30, 100, 255, 0.7)),
                InsetShadow(y: -70, blur: 60, spread: -24, color: Color(rgba: 20, 70, 220, 0.4)),
                InsetShadow(y: 8, blur: 16, spread: -9, color: Color(rgba: 90, 150, 255, 0.35)),  // soft top
                InsetShadow(blur: 4, spread: 1, color: Color(rgba: 110, 170, 255, 0.45)),  // thin rim
                InsetShadow(blur: 14, spread: 3, color: Color(rgba: 30, 90, 230, 0.4)),
                InsetShadow(blur: 30, spread: 6, color: Color(rgba: 14, 50, 170, 0.3)),
            ]
        }
    }
}

/// One CSS `inset` box-shadow: `inset <x> <y> <blur> <spread> <color>`.
struct InsetShadow {
    var x: CGFloat = 0
    var y: CGFloat = 0
    var blur: CGFloat
    var spread: CGFloat
    var color: Color
}

/// Paints inset shadows the way CSS does: everything outside the shape inset by `spread`
/// and offset by (`x`, `y`) is filled with the colour, Gaussian-blurred (σ = blur / 2), and
/// clipped to the shape.
struct InsetShadows: View {
    let layers: [InsetShadow]
    let cornerRadius: CGFloat

    var body: some View {
        ZStack {
            ForEach(Array(layers.enumerated().reversed()), id: \.offset) { _, layer in
                // Extend the filled area past the frame so the blur has colour to sample at
                // the edge instead of fading against transparency.
                let margin = layer.blur * 1.5 + abs(layer.x) + abs(layer.y) + 2
                InsetShadowFill(
                    cornerRadius: cornerRadius, inset: margin + layer.spread,
                    spread: layer.spread, offset: CGSize(width: layer.x, height: layer.y)
                )
                .fill(layer.color, style: FillStyle(eoFill: true))
                .padding(-margin)
                .blur(radius: layer.blur / 2)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
    }
}

/// The frame rect with a continuous rounded-rect hole punched in it (fill with `eoFill`).
private struct InsetShadowFill: Shape {
    let cornerRadius: CGFloat
    let inset: CGFloat
    let spread: CGFloat
    let offset: CGSize

    func path(in rect: CGRect) -> Path {
        var path = Path(rect)
        let hole = rect.insetBy(dx: inset, dy: inset).offsetBy(dx: offset.width, dy: offset.height)
        if hole.width > 0, hole.height > 0 {
            let radius = min(max(cornerRadius - spread, 0), min(hole.width, hole.height) / 2)
            path.addRoundedRect(in: hole, cornerSize: CGSize(width: radius, height: radius), style: .continuous)
        }
        return path
    }
}
