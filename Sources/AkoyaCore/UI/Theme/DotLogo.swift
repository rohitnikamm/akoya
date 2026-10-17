import SwiftUI

/// The Akoya dot-matrix mark: five columns of 2/4/6/4/2 white dots, vertically centred,
/// with an optional soft lavender glow.
public struct DotLogo: View {
    public nonisolated static let columns = [2, 4, 6, 4, 2]

    private let dotSize: CGFloat
    private let glow: Bool

    public init(dotSize: CGFloat = 6, glow: Bool = true) {
        self.dotSize = dotSize
        self.glow = glow
    }

    public var body: some View {
        // Gaps scale with the dot (mockup: 6 pt dots, 7 pt column gap, 4 pt row gap).
        HStack(alignment: .center, spacing: dotSize * 7 / 6) {
            ForEach(Array(Self.columns.enumerated()), id: \.offset) { _, count in
                VStack(spacing: dotSize * 4 / 6) {
                    ForEach(0..<count, id: \.self) { _ in
                        Circle().fill(Color.white).frame(width: dotSize, height: dotSize)
                    }
                }
            }
        }
        .shadow(color: glow ? AkoyaTheme.lavender.opacity(0.5) : .clear, radius: glow ? dotSize / 3 : 0)
        .accessibilityHidden(true)
    }
}
