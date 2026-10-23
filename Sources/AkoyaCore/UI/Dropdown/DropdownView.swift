import AppKit
import Combine
import SwiftUI

/// What the dropdown's controls do. The app supplies these; the view only calls them.
public struct DropdownActions {
    public var selectHotkey: (HotkeyKey) -> Void
    /// The chosen microphone's UID; nil for System default.
    public var selectMicrophone: (String?) -> Void
    public var quit: () -> Void
    public var retryModel: () -> Void
    public var finishSetup: () -> Void
    public var copyLast: (String) -> Void

    public init(
        selectHotkey: @escaping (HotkeyKey) -> Void, selectMicrophone: @escaping (String?) -> Void,
        quit: @escaping () -> Void, retryModel: @escaping () -> Void, finishSetup: @escaping () -> Void,
        copyLast: @escaping (String) -> Void
    ) {
        self.selectHotkey = selectHotkey
        self.selectMicrophone = selectMicrophone
        self.quit = quit
        self.retryModel = retryModel
        self.finishSetup = finishSetup
        self.copyLast = copyLast
    }
}

/// The all-in-one menu bar dropdown (docs/design/all-in-one-dropdown-target.html): status
/// card with the hotkey keycap button, last dictation, the options card, and a quiet Quit row.
/// Dark only, 320 pt wide, height to content.
public struct DropdownView: View {
    static let width: CGFloat = 320

    @ObservedObject private var model: WaveformModel
    @ObservedObject private var settings: AkoyaSettings
    @StateObject private var glow: VoiceGlowDriver
    @StateObject private var disclosure: DisclosureState
    private let modelState: ModelState
    private let permissionsGranted: Bool
    private let inputs: [AudioInputDevice]
    private let defaultInputID: UInt32?
    private let actions: DropdownActions

    /// - Parameters:
    ///   - inputs: the connected input devices (`AudioDevices.inputs()`).
    ///   - defaultInputID: the system default input (`AudioDevices.defaultInputID()`).
    public init(
        model: WaveformModel, settings: AkoyaSettings, modelState: ModelState, permissionsGranted: Bool,
        inputs: [AudioInputDevice], defaultInputID: UInt32?, actions: DropdownActions
    ) {
        self.init(
            model: model, settings: settings, modelState: modelState, permissionsGranted: permissionsGranted,
            inputs: inputs, defaultInputID: defaultInputID, actions: actions, optionsExpanded: false)
    }

    /// `optionsExpanded` starts with the Menu bar icon options open (for renders and tests).
    init(
        model: WaveformModel, settings: AkoyaSettings, modelState: ModelState, permissionsGranted: Bool,
        inputs: [AudioInputDevice], defaultInputID: UInt32?, actions: DropdownActions, optionsExpanded: Bool
    ) {
        self.model = model
        self.settings = settings
        self.modelState = modelState
        self.permissionsGranted = permissionsGranted
        self.inputs = inputs
        self.defaultInputID = defaultInputID
        self.actions = actions
        _glow = StateObject(wrappedValue: VoiceGlowDriver(model: model))
        _disclosure = StateObject(wrappedValue: DisclosureState(isExpanded: optionsExpanded))
    }

    public var body: some View {
        VStack(spacing: 10) {
            let state = StatusCardState.from(
                phase: model.phase, model: modelState, permissionsGranted: permissionsGranted)
            StatusCard(
                state: state, showsRetry: state.showsRetry(model: modelState), hotkey: settings.hotkey,
                glowOpacity: glow.opacity, selectHotkey: actions.selectHotkey,
                retry: actions.retryModel, finishSetup: actions.finishSetup)
            if let text = model.lastDictation {
                LastDictationCard(text: text, copy: actions.copyLast)
            }
            OptionsGroup(
                settings: settings,
                microphone: MicrophoneMenuModel(
                    selectedUID: settings.inputDeviceUID, selectedName: settings.inputDeviceName,
                    inputs: inputs, defaultID: defaultInputID),
                selectMicrophone: actions.selectMicrophone, disclosure: disclosure)
            QuitRow(quit: actions.quit)
        }
        .padding(14)
        .frame(width: Self.width)
        .background {
            ZStack {
                LinearGradient(
                    colors: [AkoyaTheme.panelTop, AkoyaTheme.panelBottom], startPoint: .top, endPoint: .bottom)
                InsetShadows(
                    layers: [InsetShadow(y: 1, blur: 0, spread: 0, color: AkoyaTheme.topHighlight)],
                    cornerRadius: AkoyaTheme.panel)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: AkoyaTheme.panel, style: .continuous))
        .environment(\.colorScheme, .dark)
        .focusEffectDisabled()
    }
}

// MARK: - Status card

private struct StatusCard: View {
    static let minHeight: CGFloat = 159

    let state: StatusCardState
    /// Retry sits beside "Finish setup" when the model failed behind a missing permission.
    let showsRetry: Bool
    let hotkey: HotkeyKey
    /// Rim glow opacity (0 at rest); see `VoiceGlowDriver`.
    let glowOpacity: Double
    let selectHotkey: (HotkeyKey) -> Void
    let retry: () -> Void
    let finishSetup: () -> Void

    var body: some View {
        VStack(spacing: 8) {
            DotLogo(dotSize: 5)
                .frame(height: 44)
                .padding(.vertical, 2)
            HStack(spacing: 7) {
                Text("Hold")
                KeycapButton(hotkey: hotkey, select: selectHotkey)
                Text("to talk")
            }
            .font(AkoyaFonts.outfit(13))
            .foregroundStyle(AkoyaTheme.textHint)
            if let line = state.statusLine {
                StatusLine(text: line, isError: state == .modelFailed)
            }
            switch state {
            case .downloading(let fraction?):
                DownloadProgress(fraction: fraction)
            case .modelFailed:
                PrimaryButton(title: "Retry", action: retry)
            case .setupNeeded:
                HStack(spacing: 8) {
                    PrimaryButton(title: "Finish setup", action: finishSetup)
                    if showsRetry { PrimaryButton(title: "Retry", action: retry) }
                }
            case .ready, .listening, .transcribing, .downloading(nil):
                EmptyView()
            }
        }
        .padding(EdgeInsets(top: 16, leading: 16, bottom: 14, trailing: 16))
        // Keeps the size the card had with its title, chip and hint; the content is centred in it.
        .frame(maxWidth: .infinity, minHeight: Self.minHeight)
        .akoyaDotGrid(pitch: 10, opacity: 0.07)
        // Layers, bottom to top: V1 card, glow base (cross-fades in with the glow), rim glow,
        // dot grid, content. Every kit modifier draws as a background, so the glow and the
        // glow base are applied before `akoyaV1Card` to sit above its opaque fill. Neither is
        // built at rest.
        .background {
            if glowOpacity > 0 {
                Color.clear.akoyaRimGlow(intensity: glowOpacity, cornerRadius: AkoyaTheme.card, scale: .compact)
            }
        }
        .background {
            if glowOpacity > 0 {
                // Fully on the glow base once the glow reaches its listening floor (0.2).
                AkoyaTheme.glowBase.opacity(min(1, glowOpacity / 0.2))
            }
        }
        .akoyaV1Card(cornerRadius: AkoyaTheme.card)
    }
}

/// "● Model failed" and friends: the one status line under "Hold … to talk".
private struct StatusLine: View {
    let text: String
    let isError: Bool

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(isError ? AkoyaTheme.error : AkoyaTheme.accentLight)
                .frame(width: 6, height: 6)
                .shadow(color: isError ? AkoyaTheme.error.opacity(0.6) : AkoyaTheme.chipGlow, radius: 3)
            Text(text)
                .font(AkoyaFonts.outfit(12))
                .foregroundStyle(isError ? AkoyaTheme.errorText : AkoyaTheme.textHint)
                .monospacedDigit()
        }
        .accessibilityElement(children: .combine)
    }
}

/// The hotkey keycap in "Hold [Right ⌥ ▾] to talk". Clicking it pops up the native hotkey
/// menu below it; the keycap brightens while the menu is open.
private struct KeycapButton: View {
    let hotkey: HotkeyKey
    let select: (HotkeyKey) -> Void
    @StateObject private var anchor = MenuAnchor()
    @Environment(\.popUpMenu) private var popUpMenu

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        Button {
            anchor.present(DropdownMenus.hotkey(current: hotkey, select: select), alignment: .leading, with: popUpMenu)
        } label: {
            HStack(spacing: 6.5) {
                Text(hotkey.keycapLabel)
                    .font(AkoyaFonts.outfit(13, .semibold))
                    .foregroundStyle(AkoyaTheme.textPrimary)
                DropMark()
                    .foregroundStyle(Color.white.opacity(0.6))
            }
            // CSS padding 3px 8px 3px 10px plus the 1px border; the "▾" glyph's side bearings
            // are folded into the spacing and the trailing padding.
            .padding(EdgeInsets(top: 4, leading: 11, bottom: 4, trailing: 10))
            .background(anchor.isOpen ? AkoyaTheme.keycapButtonOpenFill : AkoyaTheme.keycapButtonFill, in: shape)
            .overlay(shape.strokeBorder(
                anchor.isOpen ? AkoyaTheme.keycapButtonOpenBorder : AkoyaTheme.keycapButtonBorder, lineWidth: 1))
            .contentShape(shape)
        }
        .buttonStyle(.plain)
        .trackFrame(in: anchor)
        .accessibilityLabel("Hotkey")
        .accessibilityValue(hotkey.displayName)
        .accessibilityHint("Choose the key to hold or tap")
    }
}

private struct DownloadProgress: View {
    let fraction: Double

    var body: some View {
        Capsule()
            .fill(AkoyaTheme.progressTrack)
            .overlay(alignment: .leading) {
                GeometryReader { proxy in
                    Capsule()
                        .fill(AkoyaTheme.accentLight)
                        .frame(width: proxy.size.width * min(max(fraction, 0), 1))
                }
            }
            .frame(width: 180, height: 3)
            .accessibilityElement()
            .accessibilityLabel("Model download")
            .accessibilityValue("\(Int((min(max(fraction, 0), 1) * 100).rounded())) percent")
    }
}

private struct PrimaryButton: View {
    let title: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(AkoyaFonts.outfit(13, .semibold))
                .foregroundStyle(AkoyaTheme.textPrimary)
                .padding(.vertical, 7)
                .padding(.horizontal, 18)
                .background(AccentGradient(), in: Capsule())
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }
}

private struct AccentGradient: ShapeStyle {
    func resolve(in environment: EnvironmentValues) -> LinearGradient {
        LinearGradient(colors: [AkoyaTheme.accentTop, AkoyaTheme.accentBottom], startPoint: .top, endPoint: .bottom)
    }
}

// MARK: - Last dictation

private struct LastDictationCard: View {
    let text: String
    let copy: (String) -> Void
    @StateObject private var feedback = CopyFeedback()

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("LAST DICTATION")
                    .font(AkoyaFonts.outfit(11, .medium))
                    .kerning(0.55)
                    .foregroundStyle(AkoyaTheme.textLabel)
                Spacer()
                Button(feedback.isShowing ? "Copied" : "Copy") {
                    copy(text)
                    feedback.flash()
                }
                .font(AkoyaFonts.outfit(12))
                .foregroundStyle(AkoyaTheme.accentLight)
                .buttonStyle(.plain)
                .accessibilityLabel("Copy last dictation")
            }
            Text(text)
                .font(AkoyaFonts.outfit(13))
                .foregroundStyle(AkoyaTheme.textBody)
                // CSS line-height 1.4 (18.2 pt lines): the extra leading goes between lines
                // and, halved, above the first and below the last.
                .lineSpacing(2.3)
                .padding(.vertical, 0.7)
                .lineLimit(4)
                .frame(maxWidth: .infinity, alignment: .leading)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.vertical, 12)
        .padding(.horizontal, 14)
        .background(AkoyaTheme.innerFlat, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
    }
}

/// Shows "Copied" for a moment after Copy. A class rather than `@State`: the `@State` macro
/// plugin is missing from the Command Line Tools toolchain.
@MainActor
private final class CopyFeedback: ObservableObject {
    @Published private(set) var isShowing = false
    private var resetTask: Task<Void, Never>?

    func flash() {
        isShowing = true
        resetTask?.cancel()
        resetTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(1.5))
            guard !Task.isCancelled else { return }
            self?.isShowing = false
        }
    }
}
