import AkoyaCore
import AppKit
import os
import SwiftUI

/// Builds Akoya's components, connects them, and owns the menu bar dropdown panel.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    /// How often to retry the hotkey tap while Accessibility has not been granted yet.
    private static let hotkeyRetryInterval: TimeInterval = 2

    private static let log = Logger(subsystem: "com.rohitnikamm.akoya", category: "app")

    private let settings: AkoyaSettings
    private let model: WaveformModel
    private let transcriber: ParakeetTranscriber
    private let coordinator: AppCoordinator
    private let hotkeyMonitor: HotkeyMonitor
    private let panelStatus = PanelStatus()
    private let onboarding: OnboardingWindowController
    private var statusController: StatusItemController?
    private var panel: StatusPanelController?

    private var modelState: ModelState = .notReady {
        didSet {
            panelStatus.modelState = modelState
            onboarding.modelState = modelState
        }
    }

    private var hotkeyRetryTimer: Timer?
    private var becameActiveObserver: NSObjectProtocol?
    /// Screen lock and sleep observers; see `observeScreenLock`.
    private var screenLockObservers: [NSObjectProtocol] = []
    /// Keeps the audio device listeners alive; see `refreshInputs`.
    private var inputDevicesObservation: AnyObject?

    override init() {
        let settings = AkoyaSettings()
        let model = WaveformModel()
        let transcriber = ParakeetTranscriber()
        let audioCapture = AudioCapture()
        audioCapture.preferredDeviceUID = { settings.inputDeviceUID }
        let coordinator = AppCoordinator(
            audio: audioCapture, transcriber: transcriber, inserter: PasteTextInserter(),
            model: model)
        self.settings = settings
        self.model = model
        self.transcriber = transcriber
        self.coordinator = coordinator
        onboarding = OnboardingWindowController(
            coordinator: coordinator, model: model, settings: settings,
            hotkeyName: { settings.hotkey.keycapLabel })
        hotkeyMonitor = HotkeyMonitor(
            key: { settings.hotkey },
            isRecording: { coordinator.isRecording },
            onEvent: { event in
                // Runs synchronously inside the event tap; hand off so the tap never waits on
                // dictation work. The main queue is FIFO, so press/release order is preserved.
                DispatchQueue.main.async {
                    MainActor.assumeIsolated { coordinator.handle(event) }
                }
            })
        super.init()
        coordinator.isReady = { [weak self] in self?.modelState == .ready }
        coordinator.isModelFailed = { [weak self] in
            if case .failed = self?.modelState { true } else { false }
        }
        coordinator.usesPill = { settings.indicator == .pill }
        onboarding.retryModel = { [weak self] in self?.prepareModel() }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        AkoyaFonts.register()
        // Bind before the status item reads the model, so the first width honours the settings.
        settings.$look.assign(to: &model.$look)
        settings.$redTint.assign(to: &model.$redTint)
        settings.$hideIdleIcon.assign(to: &model.$hideIdleIcon)
        settings.$indicator.assign(to: &model.$indicator)

        refreshInputs()
        inputDevicesObservation = AudioDevices.observeChanges { [weak self] in self?.refreshInputs() }

        let panel = StatusPanelController(
            content: DropdownRoot(model: model, settings: settings, status: panelStatus, actions: dropdownActions()))
        // Reopening from Spotlight activates Akoya; hand focus back once the panel closes.
        panel.onVisibilityChange = { isShown in
            if !isShown { WindowPresenter.handBackFocusIfActive() }
        }
        self.panel = panel
        statusController = StatusItemController(model: model) { [weak self] button in
            self?.togglePanel(from: button)
        }

        quitLegacySilk()
        showOnboardingIfNeeded()
        migrateModelsFolder()
        prepareModel()
        startHotkeyMonitor()
        observeScreenLock()
    }

    /// Practice mode must not outlive the onboarding window.
    func applicationWillTerminate(_ notification: Notification) {
        onboarding.close()
    }

    /// Reopening the app (Spotlight, Finder) opens the dropdown under the menu bar item: the
    /// way back in when the idle icon is hidden (the item is then 0 wide but still anchors).
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        guard let panel, let button = statusController?.button else { return false }
        if !panel.isShown { panelStatus.permissionsGranted = Permissions.allGranted }
        panel.open(relativeTo: button)
        // Spotlight/Finder activated Akoya; hand focus back now so a dictation with the panel
        // open pastes into the previous app (the panel survives the hide: canHide is false).
        WindowPresenter.handBackFocusIfActive()
        return false
    }

    // MARK: - Microphones

    /// Reloads the input list and system default for the Microphone menu (at launch and
    /// whenever devices come and go or the default changes).
    private func refreshInputs() {
        panelStatus.inputs = AudioDevices.inputs()
        panelStatus.defaultInputID = AudioDevices.defaultInputID()
    }

    // MARK: - Onboarding

    /// First launch shows the whole flow; relaunching before finishing resumes at the first
    /// missing permission once one is already on; after that only a missing permission
    /// brings it back.
    private func showOnboardingIfNeeded() {
        guard let start = OnboardingFlow.launchStart(
            completed: settings.onboardingCompleted, granted: Permissions.isGranted) else { return }
        onboarding.show(startAt: start)
    }

    // MARK: - Silk

    /// A still-running Silk listens for the same hotkey, so every dictation would paste twice.
    private func quitLegacySilk() {
        let count = LegacySilk.quitRunning()
        if count > 0 {
            Self.log.info("Asked \(count, privacy: .public) running Silk instance(s) to quit")
        }
        // A second copy of Akoya (e.g. a dev build next to the Homebrew install) would double
        // every dictation; the newest launch wins.
        let others = OtherInstances.quitRunning()
        if others > 0 {
            Self.log.info("Asked \(others, privacy: .public) other Akoya instance(s) to quit")
        }
    }

    // MARK: - Model

    /// Moves models downloaded under the old Silk name before `prepare` looks for them.
    private func migrateModelsFolder() {
        let result = ModelsFolderMigration.migrate(
            from: ParakeetTranscriber.legacyDirectory, to: ParakeetTranscriber.defaultDirectory)
        Self.log.info("Models folder migration: \(String(describing: result), privacy: .public)")
    }

    private func prepareModel() {
        modelState = .notReady
        let transcriber = transcriber
        // States arrive on arbitrary threads; the main queue keeps them in order.
        let report: @Sendable (ModelState) -> Void = { [weak self] state in
            DispatchQueue.main.async {
                MainActor.assumeIsolated { self?.modelState = state }
            }
        }
        Task { await transcriber.prepare(onState: report) }
    }

    // MARK: - Screen lock

    /// A lock or sleep cancels a recording, and a dictation still transcribing is kept as the
    /// last dictation instead of being pasted behind the lock screen.
    private func observeScreenLock() {
        let sources: [(NotificationCenter, Notification.Name)] = [
            (DistributedNotificationCenter.default(), Notification.Name("com.apple.screenIsLocked")),
            (NSWorkspace.shared.notificationCenter, NSWorkspace.willSleepNotification),
        ]
        screenLockObservers = sources.map { center, name in
            center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.coordinator.screenLocked() }
            }
        }
    }

    // MARK: - Hotkey

    /// Starts the event tap, or keeps retrying until it starts: the user grants Accessibility in
    /// System Settings while Akoya stays in the background, so retry on a timer as well as on
    /// activation.
    private func startHotkeyMonitor() {
        if hotkeyMonitor.start() {
            stopHotkeyRetry()
            return
        }
        guard hotkeyRetryTimer == nil else { return }
        becameActiveObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.startHotkeyMonitor() }
        }
        let timer = Timer(timeInterval: Self.hotkeyRetryInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.startHotkeyMonitor() }
        }
        RunLoop.main.add(timer, forMode: .common)
        hotkeyRetryTimer = timer
    }

    private func stopHotkeyRetry() {
        hotkeyRetryTimer?.invalidate()
        hotkeyRetryTimer = nil
        if let becameActiveObserver {
            NotificationCenter.default.removeObserver(becameActiveObserver)
            self.becameActiveObserver = nil
        }
    }

    // MARK: - Dropdown panel

    private func togglePanel(from button: NSStatusBarButton) {
        guard let panel else { return }
        // Permissions change in System Settings while Akoya is in the background; re-check on open.
        if !panel.isShown { panelStatus.permissionsGranted = Permissions.allGranted }
        panel.toggle(relativeTo: button)
    }

    private func dropdownActions() -> DropdownActions {
        DropdownActions(
            selectHotkey: { [weak self] key in self?.settings.hotkey = key },
            selectMicrophone: { [weak self] uid in
                guard let self else { return }
                // The name is kept so the menu can still name the device while it is unplugged.
                settings.inputDeviceName = uid.flatMap { uid in panelStatus.inputs.first { $0.uid == uid }?.name }
                settings.inputDeviceUID = uid
            },
            quit: { NSApp.terminate(nil) },
            // The panel stays open so the card shows the download starting again.
            retryModel: { [weak self] in self?.prepareModel() },
            finishSetup: { [weak self] in
                // Onboarding first: closing the panel hands focus back unless a window is showing.
                self?.onboarding.show(startAt: OnboardingFlow.firstIncomplete(granted: Permissions.isGranted))
                self?.panel?.close()
            },
            copyLast: { text in
                let pasteboard = NSPasteboard.general
                pasteboard.clearContents()
                pasteboard.setString(text, forType: .string)
            })
    }
}

/// App state the dropdown shows that does not live in `WaveformModel` or `AkoyaSettings`.
@MainActor
private final class PanelStatus: ObservableObject {
    @Published var modelState: ModelState = .notReady
    @Published var permissionsGranted = Permissions.allGranted
    @Published var inputs: [AudioInputDevice] = []
    @Published var defaultInputID: UInt32?
}

/// Feeds `PanelStatus` into `DropdownView`, which takes those values as plain parameters.
private struct DropdownRoot: View {
    let model: WaveformModel
    let settings: AkoyaSettings
    @ObservedObject var status: PanelStatus
    let actions: DropdownActions

    var body: some View {
        DropdownView(
            model: model, settings: settings, modelState: status.modelState,
            permissionsGranted: status.permissionsGranted, inputs: status.inputs,
            defaultInputID: status.defaultInputID, actions: actions)
    }
}
