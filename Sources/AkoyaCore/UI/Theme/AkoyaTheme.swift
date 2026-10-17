import SwiftUI

/// Akoya's colour and radius tokens (spec: "Visual language", as amended by "Palette change:
/// electric blue, bottom-weighted").
public enum AkoyaTheme {
    /// Card base, `#03060F`.
    public static let surface = Color(hex: 0x03060F)
    /// Dropdown panel gradient, `#0E1424` → `#070A12`.
    public static let panelTop = Color(hex: 0x0E1424)
    public static let panelBottom = Color(hex: 0x070A12)
    /// Pills and inner cards, `rgba(30,36,58,0.6)` (the spec allows 0.6–0.8).
    public static let inner = Color(rgba: 30, 36, 58, 0.6)
    /// Selected segment and primary button gradient, electric blue `#2F6BFF` → `#1D4ED8`.
    public static let accentTop = Color(hex: 0x2F6BFF)
    public static let accentBottom = Color(hex: 0x1D4ED8)
    /// Small accents (chip dot, progress, Copy link), light blue `#93C5FD`; softer `#BFDBFE`.
    public static let accentLight = Color(hex: 0x93C5FD)
    public static let accentLightSoft = Color(hex: 0xBFDBFE)
    public static let textPrimary = Color.white
    public static let textSecondary = Color.white.opacity(0.7)

    // Dropdown panel (docs/design/dropdown-mockup.html, blue values from blue-glow-target.html).
    /// Near-black blue base the status card cross-fades to while its rim glow shows, `#020611`.
    public static let glowBase = Color(hex: 0x020611)
    /// Mode switch track and Settings pill, `rgba(30,36,58,0.8)`.
    public static let pill = Color(rgba: 30, 36, 58, 0.8)
    /// Last-dictation card gradient, `#171D33` → `#0B0F1C`.
    public static let innerCardTop = Color(hex: 0x171D33)
    public static let innerCardBottom = Color(hex: 0x0B0F1C)
    /// Round Quit button fill `#E8E8F0` and its glyph `#111111`.
    public static let quitFill = Color(hex: 0xE8E8F0)
    public static let quitGlyph = Color(hex: 0x111111)
    /// Glow around the status chip's dot, `#60A5FA`.
    public static let chipGlow = Color(hex: 0x60A5FA)
    /// Keycap fill (white 0.10) and border `rgba(140,180,255,0.35)`.
    public static let keycapFill = Color.white.opacity(0.10)
    public static let keycapBorder = Color(rgba: 140, 180, 255, 0.35)
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
