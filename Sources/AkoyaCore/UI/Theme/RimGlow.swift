import SwiftUI

/// Size of the rim glow recipe: `card` is the onboarding card (420×520), `compact` the
/// dropdown status card (~292×170, about half the blur and spread).
public enum RimGlowScale: Sendable {
    case card, compact
}

extension View {
    /// The signature rim glow: colour builds inward from the edge (lavender → indigo →
    /// transparent toward the dark centre), brighter at the bottom and top, with nothing
    /// drawn outside the shape. Drawn behind the content; apply it before the card fill so
    /// it sits on top of the fill, e.g.
    /// `content.akoyaDotGrid().akoyaRimGlow(...).akoyaV1Card(cornerRadius:)`.
    /// - Parameter intensity: overall opacity, 0…1 (see `GlowEnvelope.opacity`).
    public func akoyaRimGlow(intensity: Double, cornerRadius: CGFloat, scale: RimGlowScale) -> some View {
        background {
            InsetShadows(layers: scale.layers, cornerRadius: cornerRadius)
                .compositingGroup()
                .opacity(min(max(intensity * scale.intensityGain, 0), 1))
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }
}

extension RimGlowScale {
    /// Maps `intensity` to layer opacity so peak voice (0.8) matches each approved render:
    /// the card target was drawn at 0.8, the compact target at full strength.
    var intensityGain: Double {
        switch self {
        case .card: 1
        case .compact: 1 / 0.8
        }
    }

    /// The approved CSS recipes (spec "Rim glow"; docs/design/tryit-glow-target.html and
    /// dropdown-mockup.html), first layer on top as in CSS.
    var layers: [InsetShadow] {
        switch self {
        case .card:
            [
                InsetShadow(y: -16, blur: 30, spread: -10, color: Color(rgba: 110, 110, 200, 0.60)),  // brighter bottom
                InsetShadow(y: 18, blur: 34, spread: -12, color: Color(rgba: 120, 112, 176, 0.55)),  // lavender top
                InsetShadow(blur: 8, spread: 1, color: Color(rgba: 120, 114, 172, 0.70)),
                InsetShadow(blur: 26, spread: 6, color: Color(rgba: 78, 84, 156, 0.70)),
                InsetShadow(blur: 52, spread: 14, color: Color(rgba: 40, 52, 128, 0.60)),
                InsetShadow(blur: 80, spread: 18, color: Color(rgba: 18, 32, 100, 0.45)),
            ]
        case .compact:
            [
                InsetShadow(y: -8, blur: 16, spread: -6, color: Color(rgba: 28, 44, 124, 0.75)),  // bottom
                InsetShadow(y: 1, blur: 3, spread: 0, color: Color(rgba: 140, 132, 186, 0.9)),  // top lip
                InsetShadow(y: 18, blur: 30, spread: -8, color: Color(rgba: 120, 112, 172, 0.75)),  // lavender top
                InsetShadow(blur: 6, spread: 1, color: Color(rgba: 108, 104, 158, 0.85)),
                InsetShadow(blur: 20, spread: 6, color: Color(rgba: 72, 78, 146, 0.8)),
                InsetShadow(blur: 40, spread: 12, color: Color(rgba: 38, 50, 126, 0.85)),
                InsetShadow(blur: 64, spread: 16, color: Color(rgba: 16, 30, 98, 0.8)),
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
