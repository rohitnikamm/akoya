import SwiftUI

extension View {
    /// The V1 card: a `surface` fill with a radial glow from the top centre
    /// (`rgba(99,102,241,0.30)` → `rgba(91,33,182,0.14)` at 38% → transparent at 66%) and a
    /// faint top highlight, with the content clipped to a continuous rounded rect.
    public func akoyaV1Card(cornerRadius: CGFloat) -> some View {
        background {
            ZStack {
                AkoyaTheme.surface
                V1TopGradient()
                InsetShadows(
                    layers: [InsetShadow(y: 1, blur: 0, spread: 0, color: .white.opacity(0.07))],
                    cornerRadius: cornerRadius
                )
            }
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
        .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
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

/// CSS `radial-gradient(ellipse at 50% -5%, ...)` as the browser rendered the approved
/// target: a `farthest-corner` ellipse with radii √2 × the distances to that corner.
private struct V1TopGradient: View {
    private static let gradient = Gradient(stops: [
        .init(color: Color(rgba: 99, 102, 241, 0.30), location: 0),
        .init(color: Color(rgba: 91, 33, 182, 0.14), location: 0.38),
        .init(color: Color(rgba: 91, 33, 182, 0), location: 0.66),
    ])

    var body: some View {
        Canvas { context, size in
            guard size.width > 0, size.height > 0 else { return }
            let center = CGPoint(x: size.width / 2, y: -0.05 * size.height)
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
