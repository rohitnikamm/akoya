import SwiftUI

extension View {
    /// The V1 card: a `surface` fill with a blue radial glow from the top centre
    /// (`rgba(40,90,200,0.18)` → transparent at 60%), with the content clipped to a continuous
    /// rounded rect. (The indigo version's white top highlight is gone: the blue target has none.)
    public func akoyaV1Card(cornerRadius: CGFloat) -> some View {
        background {
            ZStack {
                AkoyaTheme.surface
                V1TopGradient()
            }
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
        .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
    }

    /// The faint blue grid in the lower part of a card: 1 pt `rgba(80,140,255,0.07)` lines
    /// on a `pitch` grid over the bottom 55%, fully shown in its lowest 30% and fading out
    /// toward its top edge, drawn behind the content. `opacity` (0…1) scales the whole grid
    /// so it can follow the glow (the approved renders use `0.4 + 0.6 ×` the glow's layer
    /// opacity). Apply it before `akoyaRimGlow` so it sits above the glow.
    public func akoyaGrid(pitch: CGFloat = 34, opacity: Double) -> some View {
        background {
            Canvas { context, size in
                guard pitch > 0, opacity > 0 else { return }
                // CSS: a 55%-high layer anchored to the bottom, tiles from its top-left, with
                // `mask-image: linear-gradient(to top, #000 30%, transparent)`.
                let top = size.height * 0.45
                let height = size.height - top
                var lines = Path()
                var y = top
                while y < size.height {
                    lines.addRect(CGRect(x: 0, y: y, width: size.width, height: 1))
                    y += pitch
                }
                var x: CGFloat = 0
                while x < size.width {
                    lines.addRect(CGRect(x: x, y: top, width: 1, height: height))
                    x += pitch
                }
                context.clip(to: Path(CGRect(x: 0, y: top, width: size.width, height: height)))
                context.drawLayer { layer in
                    layer.fill(lines, with: .color(Color(rgba: 80, 140, 255, 0.07)))
                    layer.blendMode = .destinationIn
                    layer.fill(
                        Path(CGRect(x: 0, y: top, width: size.width, height: height)),
                        with: .linearGradient(
                            Gradient(stops: [
                                .init(color: .black, location: 0),
                                .init(color: .black, location: 0.3),
                                .init(color: .clear, location: 1),
                            ]),
                            startPoint: CGPoint(x: 0, y: size.height), endPoint: CGPoint(x: 0, y: top)))
                }
            }
            .opacity(min(max(opacity, 0), 1))
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
    }

    /// A subtle texture of 1 pt dots (white, `opacity`) on a `pitch` grid, drawn behind the content.
    public func akoyaDotGrid(pitch: CGFloat = 12, opacity: Double = 0.06) -> some View {
        background {
            Canvas { context, size in
                guard pitch > 0 else { return }
                let radius: CGFloat = 1.15
                var dots = Path()
                var y = pitch / 2
                while y < size.height {
                    var x = pitch / 2
                    while x < size.width {
                        dots.addEllipse(in: CGRect(x: x - radius, y: y - radius, width: radius * 2, height: radius * 2))
                        x += pitch
                    }
                    y += pitch
                }
                context.fill(dots, with: .color(.white.opacity(opacity)))
            }
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
    }
}

/// CSS `radial-gradient(ellipse at 50% 0%, rgba(40,90,200,0.18) 0, transparent 60%)` as the
/// browser renders it: a `farthest-corner` ellipse with radii √2 × the distances to that
/// corner (calibrated against the approved targets in Task 2).
private struct V1TopGradient: View {
    private static let gradient = Gradient(stops: [
        .init(color: Color(rgba: 40, 90, 200, 0.18), location: 0),
        .init(color: Color(rgba: 40, 90, 200, 0), location: 0.6),
    ])

    var body: some View {
        Canvas { context, size in
            guard size.width > 0, size.height > 0 else { return }
            let center = CGPoint(x: size.width / 2, y: 0)
            let farthestX = max(center.x, size.width - center.x)
            let farthestY = max(abs(center.y), abs(size.height - center.y))
            let aspect = farthestX / farthestY  // rx / ry
            let radiusY = 2.squareRoot() * farthestY
            context.translateBy(x: center.x, y: center.y)
            context.scaleBy(x: aspect, y: 1)
            let extent = CGRect(x: -size.width / aspect, y: -center.y, width: 2 * size.width / aspect, height: size.height)
            context.fill(
                Path(extent),
                with: .radialGradient(Self.gradient, center: .zero, startRadius: 0, endRadius: radiusY)
            )
        }
    }
}
