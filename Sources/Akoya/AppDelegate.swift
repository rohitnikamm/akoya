import AppKit
import AkoyaCore
import os

/// Builds Akoya's components, connects them, and owns the menu bar menu.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    /// How often to retry the hotkey tap while Accessibility has not been granted yet.
    private static let hotkeyRetryInterval: TimeInterval = 2

    private static let log = Logger(subsystem: "com.rohitnikamm.akoya", category: "app")

    private let settings: AkoyaSettings
    private let model: WaveformModel
    private let transcriber: ParakeetTranscriber
    private let windows = WindowPresenter()
    private let coordinator: AppCoordinator
    private let hotkeyMonitor: HotkeyMonitor
    private var statusController: StatusItemController?

    private var modelState: ModelState = .notReady {
        didSet { refreshMenu() }
    }

    private var hotkeyRetryTimer: Timer?
    private var becameActiveObserver: NSObjectProtocol?

    // Menu items whose title or checkmark follows app state; refreshed in `refreshMenu`.
    private let statusLineItem = NSMenuItem(title: "", action: #selector(statusLineClicked), keyEquivalent: "")
    private let hotkeyItem = NSMenuItem(title: "", action: #selector(openSettings), keyEquivalent: "")
    private var modeItems: [(mode: DictationMode, item: NSMenuItem)] = []
    private let launchAtLoginItem = NSMenuItem(
        title: "Launch at Login", action: #selector(toggleLaunchAtLogin), keyEquivalent: "")

    override init() {
        let settings = AkoyaSettings()
        let model = WaveformModel()
        let transcriber = ParakeetTranscriber()
        let coordinator = AppCoordinator(
            audio: AudioCapture(), transcriber: transcriber, inserter: PasteTextInserter(),
            model: model, mode: { settings.mode })
        self.settings = settings
        self.model = model
        self.transcriber = transcriber
        self.coordinator = coordinator
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
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Bind before the status item reads the model, so the first width honours the settings.
        settings.$look.assign(to: &model.$look)
        settings.$redTint.assign(to: &model.$redTint)
        settings.$hideIdleIcon.assign(to: &model.$hideIdleIcon)
        settings.$expandAnimation.assign(to: &model.$expandAnimation)

        statusController = StatusItemController(model: model, menu: buildMenu())
        refreshMenu()

        if !Permissions.allGranted { windows.showOnboarding() }
        migrateModelsFolder()
        prepareModel()
        startHotkeyMonitor()
    }

    /// Reopening the app (e.g. from Finder) is the way back in when the idle icon is hidden.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        windows.showSettings(settings)
        return false
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

    // MARK: - Menu

    private func buildMenu() -> NSMenu {
        let menu = NSMenu()
        menu.delegate = self
        // Enabled state is managed by hand: the status line is only clickable when it offers
        // an action.
        menu.autoenablesItems = false

        statusLineItem.target = self
        menu.addItem(statusLineItem)
        menu.addItem(.separator())

        hotkeyItem.target = self
        menu.addItem(hotkeyItem)

        let modeMenu = NSMenu()
        modeMenu.autoenablesItems = false
        modeItems = DictationMode.allCases.map { mode in
            let item = NSMenuItem(title: mode.menuTitle, action: #selector(selectMode(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = mode.rawValue
            modeMenu.addItem(item)
            return (mode, item)
        }
        let modeItem = NSMenuItem(title: "Mode", action: nil, keyEquivalent: "")
        modeItem.submenu = modeMenu
        menu.addItem(modeItem)

        let settingsItem = NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ",")
        settingsItem.target = self
        menu.addItem(settingsItem)

        launchAtLoginItem.target = self
        menu.addItem(launchAtLoginItem)

        let permissionsItem = NSMenuItem(
            title: "Check Permissions…", action: #selector(checkPermissions), keyEquivalent: "")
        permissionsItem.target = self
        menu.addItem(permissionsItem)

        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit Akoya", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        return menu
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        refreshMenu()
    }

    /// Brings every state-dependent title and checkmark up to date. Also runs on each model
    /// state change, so download progress moves while the menu is open.
    private func refreshMenu() {
        let status = statusLine()
        statusLineItem.title = status.title
        statusLineItem.isEnabled = status.isClickable
        hotkeyItem.title = "Hotkey: \(settings.hotkey.displayName)"
        for (mode, item) in modeItems {
            item.state = settings.mode == mode ? .on : .off
        }
        launchAtLoginItem.state = settings.launchAtLogin ? .on : .off
    }

    /// Model problems and progress come first; once the model is ready, missing permissions.
    private func statusLine() -> (title: String, isClickable: Bool) {
        switch modelState {
        case .failed:
            ("Model failed — click to retry", true)
        case .downloading(let fraction?):
            ("Downloading model… \(Int(fraction * 100))%", false)
        case .downloading(nil), .notReady:
            ("Preparing model…", false)
        case .ready:
            Permissions.allGranted ? ("Model ready", false) : ("Grant permissions…", true)
        }
    }

    @objc private func statusLineClicked() {
        if case .failed = modelState {
            prepareModel()
        } else if !Permissions.allGranted {
            windows.showOnboarding()
        }
    }

    @objc private func openSettings() {
        windows.showSettings(settings)
    }

    @objc private func selectMode(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String, let mode = DictationMode(rawValue: raw) else { return }
        settings.mode = mode
    }

    @objc private func toggleLaunchAtLogin() {
        settings.launchAtLogin.toggle()
    }

    @objc private func checkPermissions() {
        windows.showOnboarding()
    }
}

private extension DictationMode {
    var menuTitle: String {
        switch self {
        case .hold: "Hold"
        case .toggle: "Toggle"
        }
    }
}
