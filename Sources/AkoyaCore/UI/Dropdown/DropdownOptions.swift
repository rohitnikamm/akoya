import AppKit
import SwiftUI

/// The options card (docs/design/all-in-one-dropdown-target.html, `.group`): Microphone menu,
/// the Menu bar icon disclosure, and Launch at login, separated by hairlines.
struct OptionsGroup: View {
    @ObservedObject var settings: AkoyaSettings
    let microphone: MicrophoneMenuModel
    let selectMicrophone: (String?) -> Void
    @ObservedObject var disclosure: DisclosureState

    var body: some View {
        VStack(spacing: 0) {
            MicrophoneRow(menu: microphone, select: selectMicrophone)
            RowSeparator()
            MenuBarIconRow(look: settings.look, disclosure: disclosure)
            if disclosure.isExpanded {
                MenuBarIconOptions(settings: settings)
                    .transition(.opacity.combined(with: .offset(y: -6)))
            }
            RowSeparator()
            Toggle(isOn: $settings.launchAtLogin) {
                RowLabel(title: "Launch at login") { PowerGlyph().stroke(style: Self.glyphStroke) }
            }
            .toggleStyle(NeutralSwitchStyle())
            .padding(.vertical, 10)
            .padding(.horizontal, 14)
        }
        .padding(.vertical, 2)
        .background(AkoyaTheme.innerFlat, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
    }

    static let glyphStroke = StrokeStyle(lineWidth: 1.3, lineCap: .round)
}

/// Whether the Menu bar icon options are expanded. Per panel session: the panel hosts fresh
/// content each time it opens, so it starts collapsed. A class rather than `@State` (the
/// `@State` macro plugin is missing from the Command Line Tools toolchain).
@MainActor
final class DisclosureState: ObservableObject {
    @Published var isExpanded: Bool

    init(isExpanded: Bool = false) {
        self.isExpanded = isExpanded
    }
}

/// Text levels and fills for the options card (spec: "Rows: 13 pt text white 88%, values
/// white 62%, icons white 60%").
private enum RowStyle {
    static let title = Color.white.opacity(0.88)
    static let value = Color.white.opacity(0.62)
    static let icon = Color.white.opacity(0.60)
    static let innerTitle = Color.white.opacity(0.80)
    static let note = Color.white.opacity(0.50)
    static let separator = Color.white.opacity(0.07)
    static let innerFill = Color.white.opacity(0.035)
}

private struct RowLabel<Glyph: View>: View {
    let title: String
    @ViewBuilder let glyph: Glyph

    var body: some View {
        HStack(spacing: 9) {
            glyph
                .frame(width: 14, height: 14)
                .foregroundStyle(RowStyle.icon)
            Text(title)
                .font(AkoyaFonts.outfit(13))
                .foregroundStyle(RowStyle.title)
                .lineLimit(1)
        }
    }
}

/// A row value with its small trailing mark: "▾" for a menu, "›" for the disclosure.
private struct RowValue: View {
    enum Mark { case menu, disclosure(isExpanded: Bool) }

    let text: String?
    let mark: Mark

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            if let text {
                Text(text)
                    .font(AkoyaFonts.outfit(12.5))
                    .lineLimit(1)
                    .truncationMode(.tail)
                    .transition(.opacity)
            }
            switch mark {
            case .menu:
                // Sits like the target's baseline-aligned 9 px "▾": centred 2.2 pt above the baseline.
                DropMark()
                    .alignmentGuide(.firstTextBaseline) { $0[.bottom] + 1.1 }
            case .disclosure(let isExpanded):
                Text("›")
                    .font(AkoyaFonts.outfit(9))
                    .rotationEffect(.degrees(isExpanded ? 90 : 0))
            }
        }
        .foregroundStyle(RowStyle.value)
    }
}

private struct RowSeparator: View {
    var body: some View {
        RowStyle.separator
            .frame(height: 1)
            .padding(.horizontal, 14)
    }
}

// MARK: - Microphone

private struct MicrophoneRow: View {
    let menu: MicrophoneMenuModel
    let select: (String?) -> Void
    @StateObject private var anchor = MenuAnchor()
    @Environment(\.popUpMenu) private var popUpMenu

    var body: some View {
        Button {
            anchor.present(DropdownMenus.microphone(menu, select: select), alignment: .trailing, with: popUpMenu)
        } label: {
            HStack(spacing: 12) {
                RowLabel(title: "Microphone") { MicGlyph().stroke(style: OptionsGroup.glyphStroke) }
                    .layoutPriority(1)
                Spacer(minLength: 0)
                RowValue(text: menu.rowValue, mark: .menu)
                    .opacity(anchor.isOpen ? 0.7 : 1)
                    .trackFrame(in: anchor)
            }
            .padding(.vertical, 10)
            .padding(.horizontal, 14)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Microphone")
        .accessibilityValue(menu.rowValue)
    }
}

// MARK: - Menu bar icon

private struct MenuBarIconRow: View {
    let look: WaveLook
    @ObservedObject var disclosure: DisclosureState

    var body: some View {
        Button {
            withAnimation(.spring(response: 0.32, dampingFraction: 0.86)) { disclosure.isExpanded.toggle() }
        } label: {
            HStack(spacing: 12) {
                RowLabel(title: "Menu bar icon") { WaveGlyph() }
                Spacer(minLength: 0)
                RowValue(
                    text: MenuBarIconDisclosure.value(look: look, isExpanded: disclosure.isExpanded),
                    mark: .disclosure(isExpanded: disclosure.isExpanded))
            }
            .padding(.vertical, 10)
            .padding(.horizontal, 14)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Menu bar icon")
        .accessibilityValue(look.displayName)
        .accessibilityHint(disclosure.isExpanded ? "Hides the icon options" : "Shows the icon options")
    }
}

private struct MenuBarIconOptions: View {
    @ObservedObject var settings: AkoyaSettings

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                InnerTitle("Waveform")
                Spacer(minLength: 8)
                NeutralSegmented(
                    options: WaveLook.allCases.map { ($0, $0.displayName) }, selection: $settings.look)
            }
            .padding(.vertical, 8)
            .padding(.horizontal, 12)
            .accessibilityElement(children: .contain)
            .accessibilityLabel("Waveform")
            Toggle(isOn: $settings.redTint) { InnerTitle("Red while recording") }
                .toggleStyle(NeutralSwitchStyle())
                .padding(.vertical, 8)
                .padding(.horizontal, 12)
            Toggle(isOn: $settings.hideIdleIcon) { InnerTitle("Hide when idle") }
                .toggleStyle(NeutralSwitchStyle())
                .padding(.vertical, 8)
                .padding(.horizontal, 12)
            Text("Hidden? Open Akoya from Spotlight to get back here.")
                .font(AkoyaFonts.outfit(11))
                .foregroundStyle(RowStyle.note)
                .fixedSize(horizontal: false, vertical: true)
                .padding(EdgeInsets(top: 2, leading: 12, bottom: 8, trailing: 12))
        }
        .padding(.vertical, 2)
        .background(RowStyle.innerFill, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .padding(EdgeInsets(top: 0, leading: 10, bottom: 6, trailing: 10))
    }
}

private struct InnerTitle: View {
    let title: String
    init(_ title: String) { self.title = title }

    var body: some View {
        Text(title)
            .font(AkoyaFonts.outfit(12.5))
            .foregroundStyle(RowStyle.innerTitle)
    }
}

// MARK: - Quit

/// A quiet text row. No ⌘Q label: the panel never becomes key, so the shortcut can't work here.
struct QuitRow: View {
    let quit: () -> Void

    var body: some View {
        Button(action: quit) {
            Text("Quit Akoya")
                .font(AkoyaFonts.outfit(13))
                .foregroundStyle(RowStyle.value)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(EdgeInsets(top: 6, leading: 14, bottom: 4, trailing: 14))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Menu anchors

/// Where a menu button sits (in the panel's hosting view coordinates) and whether its menu is
/// open. The frame is not published: it only matters when the menu opens.
@MainActor
final class MenuAnchor: ObservableObject {
    @Published private(set) var isOpen = false
    var frame: CGRect = .zero

    /// Shows the open state, then pops the menu up on the next turn of the run loop, so the
    /// button's tap finishes and the open state draws first. `popUp` returns when the menu closes.
    func present(_ menu: NSMenu, alignment: PopUpMenuAction.Alignment, with popUp: PopUpMenuAction) {
        guard !isOpen else { return }
        isOpen = true
        DispatchQueue.main.async {
            MainActor.assumeIsolated {
                popUp(menu, below: self.frame, alignment: alignment)
                self.isOpen = false
            }
        }
    }
}

extension View {
    /// Records this view's frame in the hosting view's coordinates on `anchor`.
    func trackFrame(in anchor: MenuAnchor) -> some View {
        onGeometryChange(for: CGRect.self, of: { $0.frame(in: .global) }) { frame in
            anchor.frame = frame
        }
    }
}

// MARK: - Glyphs (14×14, from the target's SVGs)

private func unitPoint(in rect: CGRect) -> (CGFloat, CGFloat) -> CGPoint {
    let unit = min(rect.width, rect.height) / 14
    return { x, y in CGPoint(x: rect.minX + x * unit, y: rect.minY + y * unit) }
}

/// `rect x4.5 y1 w5 h8 rx2.5`, `M2.5 7a4.5 4.5 0 0 0 9 0`, `M7 11.5V13`.
private struct MicGlyph: Shape {
    func path(in rect: CGRect) -> Path {
        let unit = min(rect.width, rect.height) / 14
        let point = unitPoint(in: rect)
        var path = Path()
        let capsuleOrigin = point(4.5, 1)
        path.addRoundedRect(
            in: CGRect(x: capsuleOrigin.x, y: capsuleOrigin.y, width: 5 * unit, height: 8 * unit),
            cornerSize: CGSize(width: 2.5 * unit, height: 2.5 * unit))
        // The cradle: the lower half of a circle (SwiftUI's `clockwise` is flipped here).
        path.move(to: point(2.5, 7))
        path.addArc(center: point(7, 7), radius: 4.5 * unit, startAngle: .degrees(180), endAngle: .degrees(0),
            clockwise: true)
        path.move(to: point(7, 11.5))
        path.addLine(to: point(7, 13))
        return path
    }
}

/// Four filled bars: `x1 y5 h4`, `x4 y3 h8`, `x7 y1 h12`, `x10 y3 h8`, each 2 wide, rx 1.
private struct WaveGlyph: View {
    var body: some View {
        Canvas { context, size in
            let unit = min(size.width, size.height) / 14
            for (x, y, height) in [(1.0, 5.0, 4.0), (4, 3, 8), (7, 1, 12), (10, 3, 8)] {
                let rect = CGRect(x: x * unit, y: y * unit, width: 2 * unit, height: height * unit)
                context.fill(Path(roundedRect: rect, cornerRadius: unit), with: .foreground)
            }
        }
    }
}

/// The power icon: `M7 1.8v5` and `M4.2 3.6a4.6 4.6 0 1 0 5.6 0`.
private struct PowerGlyph: Shape {
    func path(in rect: CGRect) -> Path {
        let unit = min(rect.width, rect.height) / 14
        let point = unitPoint(in: rect)
        var path = Path()
        path.move(to: point(7, 1.8))
        path.addLine(to: point(7, 6.8))
        // The arc's ends sit at (4.2, 3.6) and (9.8, 3.6); its centre is below the chord.
        let radius: CGFloat = 4.6
        let drop = (radius * radius - 2.8 * 2.8).squareRoot()
        let center = point(7, 3.6 + drop)
        let startAngle = Angle(radians: atan2(-drop, -2.8))
        let endAngle = Angle(radians: atan2(-drop, 2.8))
        // The long way round, through the bottom.
        path.move(to: point(4.2, 3.6))
        path.addArc(center: center, radius: radius * unit, startAngle: startAngle, endAngle: endAngle, clockwise: true)
        return path
    }
}
