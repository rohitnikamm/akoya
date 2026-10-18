import SwiftUI

/// The dropdown's neutral control palette (spec: "Neutral control styles"). Selected states
/// are lighter white, never solid blue; blue is kept for live states and calls to action.
enum NeutralStyle {
    static let segmentTrack = Color.white.opacity(0.06)
    static let segmentTrackHairline = Color.white.opacity(0.08)
    static let segmentSelected = Color.white.opacity(0.14)
    static let segmentSelectedHighlight = Color.white.opacity(0.18)
    static let segmentUnselectedText = Color.white.opacity(0.55)

    static let switchOffTrack = Color.white.opacity(0.10)
    static let switchOffHairline = Color.white.opacity(0.10)
    static let switchOffThumb = Color.white.opacity(0.55)
    static let switchOnTrack = Color.white.opacity(0.28)
    static let switchOnHairline = Color.white.opacity(0.22)

    static let spring = Animation.spring(response: 0.28, dampingFraction: 0.78)
}

/// Pixel | Solid style segmented control: white 6% track with a hairline, the selected
/// segment white 14% with a top highlight and semibold white text.
struct NeutralSegmented<Value: Hashable>: View {
    let options: [(value: Value, title: String)]
    @Binding var selection: Value

    var body: some View {
        HStack(spacing: 0) {
            ForEach(options, id: \.value) { option in
                let isSelected = option.value == selection
                let shape = RoundedRectangle(cornerRadius: 6, style: .continuous)
                Button {
                    withAnimation(NeutralStyle.spring) { selection = option.value }
                } label: {
                    Text(option.title)
                        .font(AkoyaFonts.outfit(12, isSelected ? .semibold : .regular))
                        .foregroundStyle(isSelected ? Color.white : NeutralStyle.segmentUnselectedText)
                        .padding(.vertical, 3)
                        .padding(.horizontal, 10)
                        .background {
                            if isSelected {
                                shape.fill(NeutralStyle.segmentSelected)
                                    .overlay(alignment: .top) {
                                        // CSS `inset 0 1px 0 rgba(255,255,255,.18)`.
                                        shape.strokeBorder(NeutralStyle.segmentSelectedHighlight, lineWidth: 1)
                                            .mask(alignment: .top) { Rectangle().frame(height: 1) }
                                    }
                            }
                        }
                        .contentShape(shape)
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(isSelected ? .isSelected : [])
            }
        }
        .padding(2)
        .background {
            let track = RoundedRectangle(cornerRadius: 8, style: .continuous)
            track.fill(NeutralStyle.segmentTrack)
                .overlay(track.strokeBorder(NeutralStyle.segmentTrackHairline, lineWidth: 1))
        }
    }
}

/// A 34×20 switch: off white 10% track with a white 55% thumb, on white 28% with a white
/// thumb and a small shadow. The thumb slides with a spring.
struct NeutralSwitchStyle: ToggleStyle {
    func makeBody(configuration: Configuration) -> some View {
        Button {
            withAnimation(NeutralStyle.spring) { configuration.isOn.toggle() }
        } label: {
            HStack(spacing: 0) {
                configuration.label
                Spacer(minLength: 8)
                track(isOn: configuration.isOn)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityValue(configuration.isOn ? "On" : "Off")
    }

    private func track(isOn: Bool) -> some View {
        Capsule()
            .fill(isOn ? NeutralStyle.switchOnTrack : NeutralStyle.switchOffTrack)
            .overlay(Capsule().strokeBorder(
                isOn ? NeutralStyle.switchOnHairline : NeutralStyle.switchOffHairline, lineWidth: 1))
            .frame(width: 34, height: 20)
            .overlay(alignment: isOn ? .trailing : .leading) {
                Circle()
                    .fill(isOn ? Color.white : NeutralStyle.switchOffThumb)
                    .frame(width: 16, height: 16)
                    .shadow(color: .black.opacity(isOn ? 0.4 : 0), radius: 1.5, y: 1)
                    .padding(2)
            }
    }
}

/// The target's small "▾" (a 9 px glyph Outfit lacks, so the fallback font would draw it
/// wider): a 2.8 × 2.2 pt downward triangle in the current foreground style.
struct DropMark: View {
    var body: some View {
        DropTriangle()
            .frame(width: 2.8, height: 2.2)
            .accessibilityHidden(true)
    }
}

private struct DropTriangle: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: rect.minX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.maxX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.midX, y: rect.maxY))
        path.closeSubpath()
        return path
    }
}
