import AppKit
import AVFoundation
import SwiftUI

/// Shows the three-step onboarding in a borderless, transparent window: a 420×520
/// continuous-corner card, centred, movable by its background, with a × to close it.
///
/// However the window goes away (Done, Skip, ×, app quit) dictation is switched back to
/// pasting. Done and Skip also mark onboarding complete; Done taps the finished haptic. The ×
/// counts as Skip on the practice step or once every permission is on (`closeExit`).
@MainActor
public final class OnboardingWindowController {
    static let cardSize = CGSize(width: 420, height: 520)

    private let coordinator: AppCoordinator
    private let model: WaveformModel
    private let settings: AkoyaSettings
    private let hotkeyName: () -> String
    private let finishedHaptic: () -> Void
    private var window: OnboardingWindow?
    private var viewModel: OnboardingViewModel?
    private var closeObserver: NSObjectProtocol?

    /// Re-runs the speech model preparation; the footer's Retry calls it. Set once at launch.
    public var retryModel: () -> Void = {}

    /// The speech model's state, for the download line on steps 2–3. Set it as it changes.
    public var modelState: ModelState = .notReady {
        didSet { viewModel?.modelState = modelState }
    }

    public var isShown: Bool { window != nil }

    public convenience init(
        coordinator: AppCoordinator, model: WaveformModel, settings: AkoyaSettings,
        hotkeyName: @escaping () -> String
    ) {
        self.init(
            coordinator: coordinator, model: model, settings: settings, hotkeyName: hotkeyName,
            finishedHaptic: Haptics.finished)
    }

    init(
        coordinator: AppCoordinator, model: WaveformModel, settings: AkoyaSettings,
        hotkeyName: @escaping () -> String, finishedHaptic: @escaping () -> Void
    ) {
        self.coordinator = coordinator
        self.model = model
        self.settings = settings
        self.hotkeyName = hotkeyName
        self.finishedHaptic = finishedHaptic
    }

    /// Shows the window at `startAt`, or brings it forward if it is already open.
    public func show(startAt: OnboardingStep = .welcome) {
        if let window {
            window.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        let viewModel = OnboardingViewModel(
            start: startAt, modelState: modelState,
            tryIt: TryItSession(coordinator: coordinator, model: model),
            retryModel: { [weak self] in self?.retryModel() },
            reachedTryIt: { [weak self] in self?.bringForward() })
        let root = OnboardingRootView(
            viewModel: viewModel, settings: settings, model: model, hotkeyName: hotkeyName,
            finish: { [weak self] exit in self?.finish(exit) })
        let window = OnboardingWindow(content: root, size: Self.cardSize)
        window.center()
        closeObserver = NotificationCenter.default.addObserver(
            forName: NSWindow.willCloseNotification, object: window, queue: .main
        ) { [weak self] _ in
            // Only reached if something other than `finish` closes the window.
            MainActor.assumeIsolated { self?.finish(.closed) }
        }
        self.viewModel = viewModel
        self.window = window
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        // The window shadow follows the drawn alpha (the rounded card), so draw first.
        window.display()
        window.invalidateShadow()
        viewModel.startPolling()
    }

    /// The last permission was granted in System Settings, so the practice step is waiting
    /// behind it. Raise the window without waiting for a click; activate only if Akoya is not
    /// already frontmost, so Done and Return work at once.
    private func bringForward() {
        guard let window else { return }
        window.orderFrontRegardless()
        if !NSApp.isActive {
            window.makeKey()
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    /// Closes the window without marking onboarding complete (app quit).
    public func close() {
        finish(.closed)
    }

    func finish(_ exit: OnboardingExit) {
        // The coordinator reads `destination` when a recording stops, so a practice recording
        // still running (a latched or held recording) would otherwise be pasted into whatever
        // app is frontmost next. Drop it. A final pass already running keeps the practice
        // destination it captured at stop time, so it is never pasted.
        if coordinator.isRecording { coordinator.handle(.cancel) }
        coordinator.destination = .paste
        switch exit {
        case .done:
            settings.onboardingCompleted = true
            finishedHaptic()
        case .skipped:
            settings.onboardingCompleted = true
        case .closed:
            break
        }
        tearDown()
    }

    private func tearDown() {
        viewModel?.stopPolling()
        viewModel = nil
        if let closeObserver {
            NotificationCenter.default.removeObserver(closeObserver)
            self.closeObserver = nil
        }
        guard let window else { return }
        self.window = nil
        window.close()
        WindowPresenter.handBackFocus(closing: window)
    }
}

/// Step, grant and download state behind the onboarding window, re-reading the permissions
/// every 0.5 s while shown (System Settings sends no notification when they change).
@MainActor
final class OnboardingViewModel: ObservableObject {
    private static let pollInterval: Duration = .milliseconds(500)

    @Published private(set) var flow: OnboardingFlow
    @Published private(set) var granted: [Permission: Bool] = [:]
    @Published private(set) var microphoneStatus: AVAuthorizationStatus
    @Published var modelState: ModelState
    let tryIt: TryItSession

    private let checkGrant: (Permission) -> Bool
    private let readMicrophoneStatus: () -> AVAuthorizationStatus
    private let grantedHaptic: () -> Void
    private let reachedTryIt: () -> Void
    private var pollTask: Task<Void, Never>?

    /// Re-runs the speech model preparation (the footer's Retry).
    let retryModel: () -> Void

    init(
        start: OnboardingStep, modelState: ModelState, tryIt: TryItSession,
        isGranted: @escaping (Permission) -> Bool = Permissions.isGranted,
        microphoneStatus: @escaping () -> AVAuthorizationStatus = { AVCaptureDevice.authorizationStatus(for: .audio) },
        grantedHaptic: @escaping () -> Void = Haptics.permissionGranted,
        retryModel: @escaping () -> Void = {},
        reachedTryIt: @escaping () -> Void = {}
    ) {
        flow = OnboardingFlow(start: start)
        self.modelState = modelState
        self.tryIt = tryIt
        checkGrant = isGranted
        readMicrophoneStatus = microphoneStatus
        self.microphoneStatus = microphoneStatus()
        self.grantedHaptic = grantedHaptic
        self.retryModel = retryModel
        self.reachedTryIt = reachedTryIt
        readGrants()
    }

    func isGranted(_ permission: Permission) -> Bool {
        granted[permission] ?? false
    }

    func getStarted() {
        readGrants()
        flow.next(granted: isGranted(_:))
    }

    /// The active permission's one button: one action sequence per click.
    func perform(_ permission: Permission) {
        Permissions.performOnboardingAction(
            for: permission, microphoneStatus: microphoneStatus, granted: isGranted(permission))
    }

    func poll() {
        readGrants()
        if flow.refresh(granted: isGranted(_:)) {
            grantedHaptic()
            if flow.step == .tryIt { reachedTryIt() }
        }
    }

    /// What the × means: with every permission on, or on the practice step, setup is as good as
    /// finished, so it counts as Skip instead of showing up again at the next launch.
    var closeExit: OnboardingExit {
        let allGranted = Permission.allCases.allSatisfy { isGranted($0) }
        return flow.step == .tryIt || allGranted ? .skipped : .closed
    }

    func startPolling() {
        guard pollTask == nil else { return }
        pollTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: Self.pollInterval)
                guard !Task.isCancelled else { return }
                self?.poll()
            }
        }
    }

    func stopPolling() {
        pollTask?.cancel()
        pollTask = nil
    }

    private func readGrants() {
        for permission in Permission.allCases {
            let now = checkGrant(permission)
            if granted[permission] != now { granted[permission] = now }
        }
        let status = readMicrophoneStatus()
        if status != microphoneStatus { microphoneStatus = status }
    }
}

/// The onboarding card: the current step (cross-fading over 250 ms) on the V1 card, with the
/// × in the top-right corner.
struct OnboardingRootView: View {
    @ObservedObject var viewModel: OnboardingViewModel
    @ObservedObject var settings: AkoyaSettings
    let model: WaveformModel
    let hotkeyName: () -> String
    let finish: (OnboardingExit) -> Void
    /// False for ImageRenderer snapshots, which cannot draw AppKit views.
    var draggable = true

    var body: some View {
        ZStack(alignment: .topTrailing) {
            ZStack {
                step
                    .id(viewModel.flow.step)
                    .transition(.opacity)
            }
            .animation(.easeInOut(duration: 0.25), value: viewModel.flow.step)
            CloseButton { finish(viewModel.closeExit) }
                .padding(20)
        }
        .frame(width: OnboardingWindowController.cardSize.width, height: OnboardingWindowController.cardSize.height)
        .background {
            if draggable { WindowDragArea() }
        }
        .akoyaV1Card(cornerRadius: AkoyaTheme.window)
        .environment(\.colorScheme, .dark)
        .focusEffectDisabled()
    }

    @ViewBuilder private var step: some View {
        switch viewModel.flow.step {
        case .welcome:
            WelcomeStep(getStarted: viewModel.getStarted)
        case .permission(let permission):
            PermissionsStep(
                active: permission, granted: viewModel.isGranted(_:),
                microphoneStatus: viewModel.microphoneStatus, modelState: viewModel.modelState,
                retryModel: viewModel.retryModel, perform: viewModel.perform(_:))
        case .tryIt:
            TryItStep(
                session: viewModel.tryIt, model: model, hotkeyLabel: hotkeyName(),
                modelState: viewModel.modelState, retryModel: viewModel.retryModel, done: { finish(.done) }, skip: { finish(.skipped) })
        }
    }
}

// MARK: - Shared step parts

/// Full-width pill button: light ("Get started", primary Done), accent (permission actions),
/// or quiet (Done before the first transcript).
struct OnboardingButton: View {
    enum Style { case light, accent, quiet }

    let title: String
    let style: Style
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(AkoyaFonts.outfit(15, .semibold))
                .foregroundStyle(foreground)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 13)
                .background { background }
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }

    private var foreground: Color {
        switch style {
        case .light: AkoyaTheme.surface
        case .accent: AkoyaTheme.textPrimary
        case .quiet: AkoyaTheme.buttonQuietText
        }
    }

    @ViewBuilder private var background: some View {
        switch style {
        case .light:
            Capsule().fill(AkoyaTheme.buttonLight)
        case .accent:
            Capsule().fill(
                LinearGradient(
                    colors: [AkoyaTheme.accentTop, AkoyaTheme.accentBottom], startPoint: .top, endPoint: .bottom))
        case .quiet:
            Capsule().fill(AkoyaTheme.buttonQuiet)
        }
    }
}

/// Three page dots; the current one is an 18 pt pill.
struct PageDots: View {
    let current: Int

    var body: some View {
        HStack(spacing: 6) {
            ForEach(0..<3, id: \.self) { index in
                Capsule()
                    .fill(index == current ? AkoyaTheme.textPrimary : AkoyaTheme.pageDotInactive)
                    .frame(width: index == current ? 18 : 6, height: 6)
            }
        }
        .accessibilityElement()
        .accessibilityLabel("Step \(current + 1) of 3")
    }
}

/// The small footer line while the speech model is not usable yet (steps 2–3), with a small
/// Retry when it failed to load.
struct ModelStatusLine: View {
    let text: String
    var showsRetry = false
    var retry: () -> Void = {}

    var body: some View {
        HStack(spacing: 8) {
            Text(text)
                .font(AkoyaFonts.outfit(12))
                .foregroundStyle(AkoyaTheme.textEyebrow)
                .monospacedDigit()
                .multilineTextAlignment(.center)
            if showsRetry {
                Button("Retry", action: retry)
                    .buttonStyle(.plain)
                    .font(AkoyaFonts.outfit(12, .semibold))
                    .foregroundStyle(AkoyaTheme.accentLightSoft)
            }
        }
    }
}

private struct CloseButton: View {
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Path { path in
                path.move(to: CGPoint(x: 0, y: 0))
                path.addLine(to: CGPoint(x: 9, y: 9))
                path.move(to: CGPoint(x: 9, y: 0))
                path.addLine(to: CGPoint(x: 0, y: 9))
            }
            .stroke(AkoyaTheme.textEyebrow, style: StrokeStyle(lineWidth: 1.6, lineCap: .round))
            .frame(width: 9, height: 9)
            .frame(width: 28, height: 28)
            .background(AkoyaTheme.chipPendingFill, in: Circle())
            .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Close")
    }
}

// MARK: - Window

/// Borderless and transparent so only the rounded card shows; it can still become key so the
/// buttons and Return work.
private final class OnboardingWindow: NSWindow {
    init(content: some View, size: CGSize) {
        super.init(
            contentRect: CGRect(origin: .zero, size: size), styleMask: [.borderless], backing: .buffered,
            defer: false)
        let hosting = NSHostingView(rootView: content)
        hosting.frame = CGRect(origin: .zero, size: size)
        contentView = hosting
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        isMovableByWindowBackground = true
        isReleasedWhenClosed = false
        collectionBehavior = [.fullScreenAuxiliary]
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// Drags the window from any part of the card not covered by a control.
private struct WindowDragArea: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { DragView() }
    func updateNSView(_ nsView: NSView, context: Context) {}

    private final class DragView: NSView {
        override var mouseDownCanMoveWindow: Bool { true }
        override func mouseDown(with event: NSEvent) {
            window?.performDrag(with: event)
        }
    }
}
