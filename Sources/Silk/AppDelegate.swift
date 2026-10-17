import AppKit
import SilkCore

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let model = WaveformModel()
    private var statusController: StatusItemController?

    // Temporary: drives the menu bar item with fake levels until the coordinator exists.
    private var meter = LevelMeter()
    private var levelTimer: Timer?
    private let previewPhases: [(title: String, phase: DictationPhase)] = [
        ("Preview: Recording", .recording),
        ("Preview: Transcribing", .transcribing),
        ("Preview: Error", .error),
        ("Preview: Idle", .idle),
    ]

    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu()
        for (index, preview) in previewPhases.enumerated() {
            let item = NSMenuItem(title: preview.title, action: #selector(previewPhase(_:)), keyEquivalent: "")
            item.target = self
            item.tag = index
            menu.addItem(item)
        }
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit Silk", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        statusController = StatusItemController(model: model, menu: menu)
    }

    @objc private func previewPhase(_ sender: NSMenuItem) {
        let phase = previewPhases[sender.tag].phase
        levelTimer?.invalidate()
        levelTimer = nil
        if phase == .recording {
            meter.reset()
            model.columns = meter.columns
            let timer = Timer(timeInterval: 1.0 / 25.0, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated {
                    guard let self else { return }
                    self.meter.push(rms: Float.random(in: 0.003...0.32))
                    self.model.columns = self.meter.columns
                }
            }
            RunLoop.main.add(timer, forMode: .common)
            levelTimer = timer
        }
        model.phase = phase
    }
}
