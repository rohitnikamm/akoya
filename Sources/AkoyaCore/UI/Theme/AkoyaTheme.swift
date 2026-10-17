import SwiftUI

/// Akoya's colour and radius tokens (spec: "Visual language").
public enum AkoyaTheme {
    /// Card base, `#0B0B18`.
    public static let surface = Color(hex: 0x0B0B18)
    /// Dropdown panel gradient, `#11112A` → `#08080F`.
    public static let panelTop = Color(hex: 0x11112A)
    public static let panelBottom = Color(hex: 0x08080F)
    /// Pills and inner cards, `rgba(40,40,58,0.6)` (the spec allows 0.6–0.85).
    public static let inner = Color(hex: 0x28283A, opacity: 0.6)
    /// Selected segment and primary button gradient, `#4F46E5` → `#3730A3`.
    public static let indigoTop = Color(hex: 0x4F46E5)
    public static let indigoBottom = Color(hex: 0x3730A3)
    /// Dots and small accents, `#A5B4FC`; softer variant `#C7D2FE`.
    public static let lavender = Color(hex: 0xA5B4FC)
    public static let lavenderSoft = Color(hex: 0xC7D2FE)
    public static let textPrimary = Color.white
    public static let textSecondary = Color.white.opacity(0.7)

    /// Corner radii; always use them with `RoundedRectangle(style: .continuous)`.
    public static let panel: CGFloat = 30
    public static let card: CGFloat = 24
    public static let window: CGFloat = 52
}

extension Color {
    /// sRGB colour from a `0xRRGGBB` literal.
    init(hex: UInt32, opacity: Double = 1) {
        self.init(
            .sRGB,
            red: Double((hex >> 16) & 0xFF) / 255,
            green: Double((hex >> 8) & 0xFF) / 255,
            blue: Double(hex & 0xFF) / 255,
            opacity: opacity
        )
    }

    /// sRGB colour from CSS-style `rgba(r, g, b, a)` components.
    init(rgba red: Int, _ green: Int, _ blue: Int, _ opacity: Double) {
        self.init(
            .sRGB,
            red: Double(red) / 255, green: Double(green) / 255, blue: Double(blue) / 255,
            opacity: opacity
        )
    }
}
