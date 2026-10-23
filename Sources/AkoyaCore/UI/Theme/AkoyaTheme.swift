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
    /// Primary button gradient (Retry, Finish setup), electric blue `#2F6BFF` → `#1D4ED8`.
    public static let accentTop = Color(hex: 0x2F6BFF)
    public static let accentBottom = Color(hex: 0x1D4ED8)
    /// Small accents (chip dot, progress, Copy link), light blue `#93C5FD`; softer `#BFDBFE`.
    public static let accentLight = Color(hex: 0x93C5FD)
    public static let accentLightSoft = Color(hex: 0xBFDBFE)
    public static let textPrimary = Color.white
    public static let textSecondary = Color.white.opacity(0.7)

    // Dropdown panel (docs/design/all-in-one-dropdown-target.html, blue values from
    // blue-glow-target.html).
    /// Near-black blue base the status card cross-fades to while its rim glow shows, `#020611`.
    public static let glowBase = Color(hex: 0x020611)
    /// Last-dictation card and options card fill, `rgba(30,36,58,0.55)`.
    public static let innerFlat = Color(rgba: 30, 36, 58, 0.55)
    /// Glow around the status chip's dot, `#60A5FA`.
    public static let chipGlow = Color(hex: 0x60A5FA)
    /// Onboarding keycap fill (white 0.10) and border `rgba(140,180,255,0.35)`.
    public static let keycapFill = Color.white.opacity(0.10)
    public static let keycapBorder = Color(rgba: 140, 180, 255, 0.35)
    /// The dropdown's neutral keycap button: fill white 0.08 and border white 0.16, brighter
    /// (0.16 / 0.30) while its menu is open.
    public static let keycapButtonFill = Color.white.opacity(0.08)
    public static let keycapButtonBorder = Color.white.opacity(0.16)
    public static let keycapButtonOpenFill = Color.white.opacity(0.16)
    public static let keycapButtonOpenBorder = Color.white.opacity(0.30)
    /// Top highlight on the panel (white 0.07).
    public static let topHighlight = Color.white.opacity(0.07)
    /// Dropdown text levels: last-dictation body 0.9, chip and hint 0.78, last-dictation
    /// label 0.62 (≥ 4.5:1 at 11 pt), the hint's second line 0.55; onboarding muted text 0.6.
    public static let textBody = Color.white.opacity(0.9)
    public static let textHint = Color.white.opacity(0.78)
    public static let textLabel = Color.white.opacity(0.62)
    public static let textHintSecondary = Color.white.opacity(0.55)
    public static let textMuted = Color.white.opacity(0.6)
    /// Download progress track, white 0.10.
    public static let progressTrack = Color.white.opacity(0.10)

    // Onboarding (docs/design/onboarding-screens-1-2-mockup.html, indigo translated to the
    // blue family; try-it values from blue-glow-target.html).
    /// Light pill button ("Get started", primary "Done"), `#F1F1F6` with surface-coloured text.
    public static let buttonLight = Color(hex: 0xF1F1F6)
    /// Quiet pill button ("Done" before the first transcript), white 0.12 with white 0.7 text.
    public static let buttonQuiet = Color.white.opacity(0.12)
    public static let buttonQuietText = Color.white.opacity(0.7)
    /// Permission chips: granted (green), active (blue), pending (grey).
    public static let chipGrantedFill = Color(rgba: 52, 199, 89, 0.15)
    public static let chipGrantedText = Color(hex: 0x86EFAC)
    public static let chipGrantedDot = Color(hex: 0x4ADE80)
    public static let chipActiveFill = Color(rgba: 96, 165, 250, 0.2)
    public static let chipPendingFill = Color.white.opacity(0.06)
    public static let chipPendingText = Color.white.opacity(0.45)
    public static let chipPendingDot = Color.white.opacity(0.3)
    /// The amber dot beside "Waiting for the switch…", `#FBBF24`.
    public static let waiting = Color(hex: 0xFBBF24)
    /// The dropdown's "Model failed" status line: dot and text.
    public static let error = Color(hex: 0xF87171)
    public static let errorText = Color(hex: 0xFCA5A5)
    /// Round permission icon well: `rgba(59,130,246,0.18)` with a 1 pt `rgba(147,197,253,0.28)` ring.
    public static let iconWell = Color(rgba: 59, 130, 246, 0.18)
    public static let iconWellRing = Color(rgba: 147, 197, 253, 0.28)
    /// Page dots: the current page white, the others white 0.3.
    public static let pageDotInactive = Color.white.opacity(0.3)
    /// Onboarding body copy levels: welcome line 0.7, permission reason 0.68, status 0.6,
    /// eyebrow and quiet links 0.5, placeholder 0.4.
    public static let textOnboardingBody = Color.white.opacity(0.7)
    public static let textReason = Color.white.opacity(0.68)
    public static let textEyebrow = Color.white.opacity(0.5)
    public static let textPlaceholder = Color.white.opacity(0.4)
    /// Try-it hotkey line, white 0.8.
    public static let textTryItHint = Color.white.opacity(0.8)

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
