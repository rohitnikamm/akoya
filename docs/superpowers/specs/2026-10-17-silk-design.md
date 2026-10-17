# Silk: Menu Bar Dictation for macOS — Design

Status: draft for review · 2026-10-17
Mockups: https://claude.ai/artifact/9Ep4HKyADg3Bq9CoFRgSzU (approved look)

## Intent

Rohit wants the Omarchy experience of system-wide dictation (Voxtype + the Omakoe
display) on a Mac: hold a key anywhere, speak, release, and the text lands in
whatever app has focus. The status lives in the **menu bar**, not in a floating
card: a small icon on the right side of the menu bar becomes a live waveform while
you speak, then an Omakoe-style scanning bar while it transcribes, then returns to
the icon once the text is pasted.

It should feel like a native Mac utility, run fully on-device, and be installable
by developers with one Homebrew command.

## Goals

- Push-to-talk dictation that works in any app (editors, browsers, Slack, terminals).
- Live waveform in the menu bar while recording; scanning bar while transcribing.
- Local transcription only. No audio leaves the machine.
- Short phrases appear quickly (target: text pasted under ~1 s after release for a
  5-second utterance on an M-series Mac).
- Your clipboard is left as it was after insertion.
- Install with `brew install --cask rohitnikamm/tap/silk`.

## Non-goals (v1)

- Floating on-screen card or notch UI (explicitly rejected).
- LLM cleanup / rewriting modes, cloud engines, streaming partial text.
- Transcript history, meeting transcription, file transcription.
- Intel Macs.

## Anti-goals (failure even if it "works")

- Taking keyboard focus from the app you are typing in.
- Losing or overwriting the user's clipboard.
- Looking non-native in the menu bar (wrong colors in light/dark, jitter, layout jumps
  in neighboring items beyond the item's own width change).
- Sending audio or text off the device.

## Decided by Rohit

- Menu bar only, item on the right side next to the system items.
- States and look exactly as in the mockup: idle glyph → live waveform → scanning
  bar → back to idle. The waveform **appears** (no expand animation) by default.
- Scanning bar modeled on Omakoe's transcribing state (light sweeping back and forth).
- Distributed through Homebrew.

## Constraints

- macOS places third-party status items to the left of Control Center and the clock.
  The app cannot sit to their right. It sets an `autosaveName` so a user's ⌘-drag
  position is remembered.
- Required permissions: Microphone, Input Monitoring (global hotkey), Accessibility
  (simulated ⌘V). The app must guide the user through granting them.
- Homebrew's main cask repo rejects apps that fail Gatekeeper (since 2026-09-01).
  We ship through our own tap.
- Apple Silicon, macOS 14+ (required by the on-device engine choice below).

## Behavior

### State machine

```
idle ──key down──▶ recording ──key up──▶ transcribing ──text──▶ inserting ──▶ idle
  ▲                    │ (held < 250 ms → cancel)   │ (empty / error)
  └────────────────────┴────────────────────────────┴──────────▶ idle (+ brief error cue)
```

- Hold mode (default): key down starts, key up stops.
- Toggle mode (setting): press to start, press again to stop.
- Esc while recording cancels with no transcription.
- A press while transcribing is ignored.

### Menu bar visuals (from the approved mockup)

- Idle: 16×16 five-bar glyph (heights 4/8/12/8/4 pt, 2 pt wide, rounded). Item width 28 pt.
- Recording: 22 columns, 2 pt wide, 1 pt gap (65 pt), 6 pt padding each side →
  item width 77 pt. Newest sample enters on the right and scrolls left. Levels
  sampled ~25 Hz, quantized to 8 steps, mirrored around the vertical center.
  - Pixel look (default): each column is a stack of 2×2 pt cells with 1 pt gaps;
    silence is a single cell so the line never disappears. Tallest column is 7 cells
    (20 pt) so it fits the menu bar (the mockup let its loudest columns overflow).
  - Solid look (setting): plain bars, height = level × 2 pt.
- Transcribing: same 22 columns become a 2-cell-tall bar; a light sweeps end-to-end
  in 700 ms with ease-in-out-sine at the turns; four discrete brightness steps either
  side of the head (1.0 / 0.7 / 0.48 / 0.32), unlit cells at 0.18 (dim, not dark).
  Runs until text arrives; never "finishes" on its own.
- Done: back to idle glyph the moment text is pasted.
- Width change is instant (Appear). An optional Expand setting animates width over
  240 ms; the item grows leftward because the system items to its right are fixed.
- Color: follows the menu bar's foreground (light/dark aware). Optional setting
  tints the waveform red while recording. Error cue: glyph flashes red once.
- Optional setting: hide the icon when idle (item has zero width until you talk).
  Way back in: launching the app again (Spotlight, Finder) while it is running opens
  Settings, and the menu is clickable while the item is visible. *(Claude's call.)*

### Clicking the item

Opens a standard menu: status line (model ready / downloading %), Hotkey: …,
Mode: Hold / Toggle, Settings…, Launch at Login, Check permissions…, Quit.

## Architecture

One Swift app, AppKit shell, SwiftUI for drawing and settings. `LSUIElement`
(no Dock icon).

```
┌────────────── AppCoordinator (state machine, @MainActor) ──────────────┐
│                                                                        │
│  HotkeyMonitor ──press/release──▶                                       │
│  AudioCapture ──levels (25 Hz)──▶ LevelMeter ──▶ StatusItemController   │
│               ──samples (16 kHz mono Float)──▶ Transcriber              │
│  Transcriber ──String──▶ TextInserter ──▶ focused app (⌘V)              │
│  Permissions, Settings, ModelStore                                       │
└────────────────────────────────────────────────────────────────────────┘
```

| Component | Responsibility | Notes |
|---|---|---|
| `AppCoordinator` | Owns the state machine above; wires components; the only place state changes. | Pure enough to unit test with fakes for every dependency. |
| `HotkeyMonitor` | Global key down/up for the chosen key. | Active (not listen-only) `CGEventTap` on `flagsChanged`/`keyDown`, so it can swallow Esc while recording and the hotkey never leaks into the focused app. v1 hotkeys are bare modifiers only: Right Option (default), Right Command, Right Control. Uses the Accessibility permission we already need for pasting. *(Claude's call.)* |
| `AudioCapture` | Records the mic while active. | `AVAudioEngine` input tap, converted to 16 kHz mono Float32 in memory. Emits RMS per 40 ms window. |
| `LevelMeter` | RMS → 1…8 steps with a noise floor and light smoothing; keeps the 22-column history. | Pure function + ring buffer; unit tested. |
| `StatusItemController` | `NSStatusItem` with a custom SwiftUI view (`WaveformView`) for idle / recording / transcribing; builds the click menu. | Custom view, not `MenuBarExtra`, because the label must animate. `autosaveName` set. |
| `WaveformView` | Draws glyph, waveform and scanner exactly per spec. | SwiftUI `Canvas` driven by `TimelineView`; scanner math is a pure function (unit tested). |
| `Transcriber` (protocol) | `transcribe([Float]) async throws -> String` | v1 implementation: Parakeet TDT **v2 (English)** via FluidAudio (Core ML / Neural Engine). No language setting in v1. Protocol allows Parakeet v3 (multilingual), WhisperKit or Apple SpeechAnalyzer later. *(Claude's call.)* |
| `ModelStore` | Downloads the model on first run to `~/Library/Application Support/Silk/Models`, reports progress, verifies it. | Dictation disabled (menu shows progress) until ready. |
| `TextInserter` | Saves every pasteboard item, writes the text, posts ⌘V via `CGEvent`, restores the pasteboard after ~300 ms. | Needs Accessibility. Without it: leave text on clipboard and show a menu notice. |
| `Permissions` | Checks/requests Microphone, Input Monitoring, Accessibility; first-run onboarding window with "Open System Settings" buttons. | Re-checks on app activation. |
| `Settings` | `UserDefaults`-backed: hotkey, mode, look (pixel/solid), red tint, hide idle icon, expand animation, launch at login (`SMAppService`). | Small SwiftUI settings window. |

### Threading

Audio tap on the audio thread → levels hopped to main at 25 Hz; samples appended to a
lock-protected buffer. Transcription on a background task. All state transitions and
UI on the main actor.

## Distribution

- Release build: archived `.app`, zipped, attached to a GitHub Release.
- Homebrew: our own tap repo (`rohitnikamm/homebrew-tap`) with a cask pointing at the release zip.
  Install: `brew install --cask rohitnikamm/tap/silk`.
- Updates: through `brew upgrade` only. No in-app updater (Sparkle) in v1. *(Claude's call.)*
- Signing: **not notarized for now** (Rohit's decision; may add Developer ID later).
  Follows AeroSpace's approach: the cask's `postflight` runs
  `xattr -d com.apple.quarantine` on the installed app, so there is no "Open Anyway"
  step. Release builds are signed with a stable self-signed code-signing certificate
  kept in Rohit's keychain, so macOS keeps the granted permissions across
  `brew upgrade` (a plain ad-hoc signature would reset them every update).
  Releases are built by a local script on Rohit's Mac for this reason. *(Signing
  certificate approach is Claude's call.)* Ad-hoc signed builds install from a tap but need
  "Open Anyway" once, and macOS forgets granted permissions on every update.
  Developer ID signing + notarization fixes both.

## Left to the builder

- Project layout (Xcode project vs. XcodeGen/Tuist), dependency management (SwiftPM).
- Exact noise-floor / smoothing constants for `LevelMeter` (tune by feel to match the mockup).
- Release automation (GitHub Actions or a local script).
- Onboarding window copy and layout (plain, native).

## Testing

- Unit: state machine (with fake hotkey/audio/transcriber/inserter), level
  quantization, scanner brightness math, pasteboard save/restore.
- Manual checklist: permissions flow from a clean user, dictation into TextEdit,
  Safari, Slack, Terminal, VS Code; light and dark menu bar; clipboard preserved;
  first-run model download; Brew install/upgrade/uninstall.
