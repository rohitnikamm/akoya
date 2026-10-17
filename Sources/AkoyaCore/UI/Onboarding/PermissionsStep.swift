import AVFoundation
import SwiftUI

/// Step 2: chips for the three permissions, and one card for the active one with its single
/// action (docs/design/onboarding-screens-1-2-mockup.html, second card).
struct PermissionsStep: View {
    let active: Permission
    let granted: (Permission) -> Bool
    let microphoneStatus: AVAuthorizationStatus
    let modelState: ModelState
    let perform: (Permission) -> Void

    var body: some View {
        let action = Permissions.onboardingAction(
            for: active, microphoneStatus: microphoneStatus, granted: false)
        VStack(spacing: 18) {
            HStack(spacing: 10) {
                ForEach(Permission.allCases, id: \.self) { permission in
                    PermissionChip(title: permission.chipTitle, state: chipState(permission))
                }
            }
            PermissionIcon(permission: active)
                .padding(.top, 10)
            VStack(spacing: 8) {
                Text(active.title)
                    .font(AkoyaFonts.outfit(24, .semibold))
                    .foregroundStyle(AkoyaTheme.textPrimary)
                Text(active.reason)
                    .font(AkoyaFonts.outfit(14))
                    // CSS line-height 1.45 (20.3 pt lines against Outfit's 17.6).
                    .lineSpacing(2.6)
                    .padding(.vertical, 1.3)
                    .multilineTextAlignment(.center)
                    .foregroundStyle(AkoyaTheme.textReason)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: 8) {
                Circle().fill(AkoyaTheme.waiting).frame(width: 6, height: 6)
                Text(action == .request ? "Waiting for your answer…" : "Waiting for the switch…")
                    .font(AkoyaFonts.outfit(12))
                    .foregroundStyle(AkoyaTheme.textMuted)
            }
            Spacer(minLength: 0)
            if let line = modelState.onboardingStatusLine {
                ModelStatusLine(text: line)
            }
            OnboardingButton(
                title: action == .request ? "Allow" : "Open System Settings", style: .accent,
                action: { perform(active) })
                .keyboardShortcut(.defaultAction)
            PageDots(current: OnboardingStep.permission(active).pageIndex)
        }
        .padding(EdgeInsets(top: 40, leading: 32, bottom: 28, trailing: 32))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .akoyaDotGrid(pitch: 12, opacity: 0.07)
    }

    private func chipState(_ permission: Permission) -> PermissionChip.State {
        if granted(permission) { return .granted }
        return permission == active ? .active : .pending
    }
}

private struct PermissionChip: View {
    enum State { case granted, active, pending }

    let title: String
    let state: State

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(dot)
                .frame(width: 6, height: 6)
                .shadow(color: state == .active ? AkoyaTheme.chipGlow : .clear, radius: 3)
            Text(title)
                .font(AkoyaFonts.outfit(12))
                .foregroundStyle(text)
        }
        .padding(.vertical, 5)
        .padding(.horizontal, 10)
        .background(fill, in: Capsule())
        .accessibilityElement(children: .combine)
        .accessibilityValue(state == .granted ? "Granted" : state == .active ? "Current step" : "Pending")
    }

    private var fill: Color {
        switch state {
        case .granted: AkoyaTheme.chipGrantedFill
        case .active: AkoyaTheme.chipActiveFill
        case .pending: AkoyaTheme.chipPendingFill
        }
    }

    private var text: Color {
        switch state {
        case .granted: AkoyaTheme.chipGrantedText
        case .active: AkoyaTheme.accentLightSoft
        case .pending: AkoyaTheme.chipPendingText
        }
    }

    private var dot: Color {
        switch state {
        case .granted: AkoyaTheme.chipGrantedDot
        case .active: AkoyaTheme.accentLight
        case .pending: AkoyaTheme.chipPendingDot
        }
    }
}

/// The 76 pt round well with the permission's line icon.
private struct PermissionIcon: View {
    let permission: Permission

    var body: some View {
        PermissionGlyph(permission: permission)
            .stroke(AkoyaTheme.textPrimary, style: StrokeStyle(lineWidth: 1.8, lineCap: .round, lineJoin: .round))
            .overlay {
                // The accessibility figure's head and the keyboard's keys are filled.
                PermissionGlyphFill(permission: permission).fill(AkoyaTheme.textPrimary)
            }
            .frame(width: 32, height: 32)
            .frame(width: 76, height: 76)
            .background(AkoyaTheme.iconWell, in: Circle())
            .overlay(Circle().strokeBorder(AkoyaTheme.iconWellRing, lineWidth: 1))
            .accessibilityHidden(true)
    }
}

/// Stroked parts of the icons, drawn on a 24-unit grid like the mockup's SVG.
private struct PermissionGlyph: Shape {
    let permission: Permission

    func path(in rect: CGRect) -> Path {
        let unit = min(rect.width, rect.height) / 24
        func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: rect.minX + x * unit, y: rect.minY + y * unit) }
        var path = Path()
        switch permission {
        case .microphone:
            path.addRoundedRect(
                in: CGRect(origin: p(9, 3), size: CGSize(width: 6 * unit, height: 11 * unit)),
                cornerSize: CGSize(width: 3 * unit, height: 3 * unit))
            path.move(to: p(5.5, 11))
            path.addArc(center: p(12, 11), radius: 6.5 * unit, startAngle: .degrees(180), endAngle: .degrees(0), clockwise: true)
            path.move(to: p(12, 17.5))
            path.addLine(to: p(12, 21))
            path.move(to: p(8.5, 21))
            path.addLine(to: p(15.5, 21))
        case .accessibility:
            // The mockup's `M5 8.5h14M12 8.5v5.5M12 14l-3.5 6.5M12 14l3.5 6.5`.
            path.move(to: p(5, 8.5))
            path.addLine(to: p(19, 8.5))
            path.move(to: p(12, 8.5))
            path.addLine(to: p(12, 14))
            path.addLine(to: p(8.5, 20.5))
            path.move(to: p(12, 14))
            path.addLine(to: p(15.5, 20.5))
        case .inputMonitoring:
            path.addRoundedRect(
                in: CGRect(origin: p(2.5, 6), size: CGSize(width: 19 * unit, height: 12 * unit)),
                cornerSize: CGSize(width: 2.5 * unit, height: 2.5 * unit))
            path.move(to: p(8.5, 14.5))
            path.addLine(to: p(15.5, 14.5))
        }
        return path
    }
}

/// Filled parts of the icons: the figure's head, the keyboard's keys.
private struct PermissionGlyphFill: Shape {
    let permission: Permission

    func path(in rect: CGRect) -> Path {
        let unit = min(rect.width, rect.height) / 24
        func dot(_ x: CGFloat, _ y: CGFloat, _ r: CGFloat) -> CGRect {
            CGRect(x: rect.minX + (x - r) * unit, y: rect.minY + (y - r) * unit, width: 2 * r * unit, height: 2 * r * unit)
        }
        var path = Path()
        switch permission {
        case .microphone:
            break
        case .accessibility:
            path.addEllipse(in: dot(12, 4.5, 2))
        case .inputMonitoring:
            for x in [6.5, 10, 13.5, 17.0] {
                path.addEllipse(in: dot(x, 10, 1))
            }
        }
        return path
    }
}

extension Permission {
    /// Short chip label.
    fileprivate var chipTitle: String {
        switch self {
        case .microphone: "Microphone"
        case .accessibility: "Accessibility"
        case .inputMonitoring: "Input"
        }
    }

    fileprivate var title: String {
        switch self {
        case .microphone: "Microphone"
        case .accessibility: "Accessibility"
        case .inputMonitoring: "Input Monitoring"
        }
    }

    fileprivate var reason: String {
        switch self {
        case .microphone: "Lets Akoya hear you while you talk. Your voice never leaves your Mac."
        case .accessibility: "Lets Akoya paste your words and notice your hotkey. Switch on Akoya in the list that opens."
        case .inputMonitoring: "Lets Akoya notice your hotkey in any app. Switch on Akoya in the list that opens."
        }
    }
}
