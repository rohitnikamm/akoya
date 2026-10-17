import CoreText
import SwiftUI

/// The bundled Outfit typeface (OFL, variable `wght` axis). Every call falls back to the
/// system font when the font could not be registered.
public enum AkoyaFonts {
    /// Registers Outfit for this process. Optional (`outfit(_:_:)` registers on first use),
    /// but calling it at launch keeps font loading off the first frame.
    public static func register() {
        _ = postScriptName
    }

    public static func outfit(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        guard let postScriptName else { return .system(size: size, weight: weight) }
        let attributes: [CFString: Any] = [
            kCTFontNameAttribute: postScriptName,
            kCTFontVariationAttribute: [weightAxisTag: weightAxisValue(weight)],
        ]
        let descriptor = CTFontDescriptorCreateWithAttributes(attributes as CFDictionary)
        return Font(CTFontCreateWithFontDescriptor(descriptor, size, nil))
    }

    /// Whether Outfit is registered (false means `outfit` returns the system font).
    static var isRegistered: Bool { postScriptName != nil }

    /// The `wght` variation axis tag as an integer ('w' 'g' 'h' 't').
    static let weightAxisTag = 0x7767_6874

    static func weightAxisValue(_ weight: Font.Weight) -> CGFloat {
        switch weight {
        case .ultraLight: 100
        case .thin: 200
        case .light: 300
        case .medium: 500
        case .semibold: 600
        case .bold: 700
        case .heavy: 800
        case .black: 900
        default: 400
        }
    }

    /// Registered once, lazily and thread-safely; nil if the font is missing or rejected.
    private static let postScriptName: String? = {
        guard let url = fontURL(),
              let descriptors = CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor],
              let name = descriptors.first.flatMap({ CTFontDescriptorCopyAttribute($0, kCTFontNameAttribute) as? String })
        else { return nil }
        var error: Unmanaged<CFError>?
        if !CTFontManagerRegisterFontsForURL(url as CFURL, .process, &error) {
            let code = error.map { CFErrorGetCode($0.takeRetainedValue()) }
            guard code == CTFontManagerError.alreadyRegistered.rawValue else { return nil }
        }
        return name
    }()

    /// Finds the font in AkoyaCore's resource bundle. Not `Bundle.module`: its generated
    /// accessor traps when the bundle is missing, and a missing font must fall back to the
    /// system font instead.
    private static func fontURL() -> URL? {
        let bundleName = "Akoya_AkoyaCore.bundle"
        let codeBundle = Bundle(for: BundleMarker.self)
        let searchDirectories = [
            Bundle.main.resourceURL,  // Akoya.app/Contents/Resources
            Bundle.main.bundleURL,  // `swift run` (.build/<config>/)
            codeBundle.resourceURL,
            codeBundle.bundleURL.deletingLastPathComponent(),  // `swift test` (next to the .xctest)
        ]
        for directory in searchDirectories.compactMap({ $0 }) {
            guard let bundle = Bundle(url: directory.appendingPathComponent(bundleName)) else { continue }
            if let url = bundle.url(forResource: "Outfit-Variable", withExtension: "ttf", subdirectory: "Fonts") {
                return url
            }
        }
        return nil
    }

    private final class BundleMarker {}
}
