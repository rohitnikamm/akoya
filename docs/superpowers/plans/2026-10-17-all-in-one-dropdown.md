# All-in-one Dropdown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Put all of Akoya's controls in the dropdown (no Settings window), replace the Hold/Tap switch with hold-or-tap hotkey behaviour, add a clickable keycap hotkey menu, a microphone picker, a collapsible Menu bar icon section, and neutral control styling.

**Architecture:** Pure, tested types hold the decisions (hold-vs-tap transitions in `AppCoordinator`, `MicrophoneResolver`); `AudioCapture` gains device selection via CoreAudio; `DropdownView` is rebuilt to the target; `SettingsView`/`WindowPresenter.showSettings` are deleted.

**Tech Stack:** Swift 6.4 (CLT only), Swift Testing, SwiftUI/AppKit, CoreAudio/AVFoundation.

**Spec:** `docs/superpowers/specs/2026-10-17-all-in-one-dropdown-design.md` · target `docs/design/all-in-one-dropdown-target.png`.

## Global Constraints

- The dropdown never takes focus from the frontmost app, including while its menus are open.
- No solid-blue selected states; blue only for live states, Copy, and Retry/Finish setup.
- Hold threshold 0.30 s. `DictationMode` and `akoya.mode` removed.
- Microphone setting key `akoya.inputDeviceUID` (String?, nil = System default).
- No ⌘Q label on the Quit row.
- Never publish; never launch/kill the owner's running app.

## Review Focus

1. A device chosen in the picker is unplugged mid-recording → recording falls back cleanly (no crash, interruption path), row shows it as unavailable. Test in Task 2.
2. Quick accidental tap with no speech → stays recording until the next press, then silence gate → error cue; no paste. Test in Task 1.
3. Hotkey changed from the keycap menu while recording → current recording ends normally on the old key's release; new key takes effect next time. Test in Task 1 or 3.
4. Reopening the app with the icon hidden → dropdown appears anchored at the item's position. Manual (Task 3).
5. Panel open, then a menu opened, then the user dictates → paste goes to the frontmost app. Manual (Task 3).

---

### Task 1: Hold-or-tap hotkey; remove mode

**Files:** Modify `Sources/AkoyaCore/Dictation/AppCoordinator.swift`, `Sources/AkoyaCore/Dictation/Ports.swift` (remove `DictationMode`), `Sources/AkoyaCore/Settings/AkoyaSettings.swift` (remove `mode`), `Sources/Akoya/AppDelegate.swift`, every other `mode`/`DictationMode` use (DropdownView segmented control removal can be a stub here; Task 3 rebuilds the view), tests.

**Interfaces:** `AppCoordinator.init(..., holdThreshold: TimeInterval = 0.30, ...)` replaces `mode:` and `minHold:`. Behaviour per spec "Hold or tap".

- [ ] **Step 1: Failing tests** (AppCoordinatorTests): `holdThenReleaseStops` (press, advance 0.5 s, release → transcribes); `quickTapKeepsRecording` (press, advance 0.1 s, release → still `.recording`, nothing transcribed); `tapThenPressStops` (tap, then press → transcribes on that press); `escapeCancelsAfterTap`; `releaseAfterTapStopPressIsIgnored` (the release following the stopping press does nothing); `silentTapStillErrorCue`; update/remove old `shortTapCancels`, toggle-mode tests; settings tests drop `mode`.
- [ ] **Step 2:** Run `swift test --filter "AppCoordinator|AkoyaSettings"` → FAIL.
- [ ] **Step 3:** Implement. Track `isLatched` (true after a quick release). Press while recording and latched → stop. Release while recording: if latched → ignore; if held ≥ threshold → stop; else latch.
- [ ] **Step 4:** `swift test` green; commit `feat: hold or tap the hotkey; remove dictation mode`.

### Task 2: Microphone picker backend

**Files:** Create `Sources/AkoyaCore/Audio/AudioDevices.swift` (CoreAudio enumeration + change listener), `Sources/AkoyaCore/Audio/MicrophoneResolver.swift` (pure); modify `AudioCapture.swift` (apply device), `AkoyaSettings.swift` (`inputDeviceUID`), `AppDelegate.swift` (wiring); tests.

**Interfaces:**
```swift
public struct AudioInputDevice: Equatable, Sendable, Identifiable { public let id: UInt32; public let uid: String; public let name: String }
public enum AudioDevices { public static func inputs() -> [AudioInputDevice]; public static func defaultInputID() -> UInt32?; public static func observeChanges(_ onChange: @escaping @MainActor () -> Void) -> AnyObject /* token */ }
public struct MicrophoneChoice: Equatable { public let device: AudioInputDevice?; public let isFallback: Bool }  // device nil → engine default
public enum MicrophoneResolver { public static func resolve(selectedUID: String?, inputs: [AudioInputDevice], defaultID: UInt32?) -> MicrophoneChoice }
// AkoyaSettings: @Published public var inputDeviceUID: String?   (key akoya.inputDeviceUID)
// AudioCapture: public var preferredDeviceUID: () -> String? = { nil }  — resolved at each start()
```
- [ ] **Step 1: Failing tests:** resolver (selected present → it, not fallback; selected missing → default device, fallback true; nil → default, not fallback; no default + nil → device nil); settings persistence of `inputDeviceUID` (incl. nil round-trip); AudioCapture applies resolution through an injectable `applyDevice` seam (no real hardware in tests).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement (set `kAudioOutputUnitProperty_CurrentDevice` on `engine.inputNode.audioUnit` before start; on failure fall back to default and log). **Step 4:** green; commit `feat: microphone selection`.

### Task 3: Rebuild the dropdown; remove the Settings window

**Files:** Modify `Sources/AkoyaCore/UI/Dropdown/DropdownView.swift` (+ split into small files if it grows: e.g. `DropdownOptions.swift`, `NeutralControls.swift`), `StatusCardState.swift` if needed, `AppDelegate.swift`; delete `Sources/AkoyaCore/UI/SettingsView.swift` and `WindowPresenter.showSettings` (keep focus hand-back for onboarding); tests.

**Interfaces:** `DropdownActions` drops `openSettings`, gains `selectHotkey(HotkeyKey)`, `selectMicrophone(String?)`; `DropdownView.init` gains `inputs: [AudioInputDevice]`, `microphone: MicrophoneChoice`. `applicationShouldHandleReopen` → open the panel anchored to the status item.

- [ ] **Step 1:** Build the layout per spec/target: status card hint with keycap button + second line; last dictation; options group (Microphone menu, Menu bar icon disclosure with Pixel|Solid neutral segmented + two neutral switches + note, Launch at login neutral switch); quiet Quit row. Neutral control styles per spec.
- [ ] **Step 2:** Menus (hotkey, microphone) must not activate the app or make the panel key; verify by reasoning + a headless probe where possible.
- [ ] **Step 3: Visual check (mandatory):** ImageRenderer renders of normal and options-expanded states → compare to `docs/design/all-in-one-dropdown-target.png`; iterate; report paths.
- [ ] **Step 4:** Tests for any new pure logic (e.g. microphone row label, disclosure state); `swift test` green; build; commit `feat: all-in-one dropdown; remove Settings window`.

### Task 4: Copy and docs

**Files:** Onboarding Try-it hint ("Hold [key] and say anything · or tap once to start"), README, `docs/MANUAL_CHECKS.md` (new dropdown, hold-or-tap, microphone, no Settings window).

- [ ] Update copy and docs; `swift test`; commit `docs: all-in-one dropdown and hold-or-tap`.
