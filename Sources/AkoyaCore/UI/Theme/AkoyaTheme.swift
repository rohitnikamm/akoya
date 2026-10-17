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

    // Dropdown panel (docs/design/dropdown-mockup.html).
    /// Navy base the status card cross-fades to while its rim glow shows, `#000218`.
    public static let glowBase = Color(hex: 0x000218)
    /// Mode switch track and Settings pill, `#14142A`.
    public static let pill = Color(hex: 0x14142A)
    /// Last-dictation card gradient, `#151530` → `#0D0D1A`.
    public static let innerCardTop = Color(hex: 0x151530)
    public static let innerCardBottom = Color(hex: 0x0D0D1A)
    /// Round Quit button fill `#E8E8F0` and its glyph `#111111`.
    public static let quitFill = Color(hex: 0xE8E8F0)
    public static let quitGlyph = Color(hex: 0x111111)
    /// Glow around the status chip's dot, `#818CF8`.
    public static let chipGlow = Color(hex: 0x818CF8)
    /// Keycap fill (white 0.10) and border (lavender 0.35).
    public static let keycapFill = Color.white.opacity(0.10)
    public static let keycapBorder = lavender.opacity(0.35)
    /// Hairline border on inner cards (white 0.06) and top highlight on the panel (white 0.07).
    public static let hairline = Color.white.opacity(0.06)
    public static let topHighlight = Color.white.opacity(0.07)
    /// Dropdown text levels: last-dictation body 0.88, pill labels 0.85, chip and hint 0.75,
    /// unselected segment 0.6, section caption 0.45.
    public static let textBody = Color.white.opacity(0.88)
    public static let textControl = Color.white.opacity(0.85)
    public static let textHint = Color.white.opacity(0.75)
    public static let textMuted = Color.white.opacity(0.6)
    public static let textCaption = Color.white.opacity(0.45)
    /// Download progress track, white 0.10.
    public static let progressTrack = Color.white.opacity(0.10)

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
