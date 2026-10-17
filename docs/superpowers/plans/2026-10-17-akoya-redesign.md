# Akoya Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename Silk to Akoya, and replace the plain menu and onboarding with the approved dark "rim glow" dropdown panel and a 3-step onboarding whose practice step shows live words.

**Architecture:** Same SwiftPM layout (`AkoyaCore` library + `Akoya` executable). New pure, unit-tested types hold every decision: the glow envelope, the status-card state, the onboarding step logic, the live-preview scheduling and the models-folder migration. SwiftUI views render them. The `AppCoordinator` gains a voice level, the last dictation, and a "practice" destination that previews live text instead of pasting. The dropdown becomes a non-activating `NSPanel` anchored to the status item.

**Tech Stack:** Swift 6.4 (CLT only), Swift Testing, SwiftUI + AppKit, FluidAudio (unchanged), CoreText font registration, `iconutil`.

**Spec:** `docs/superpowers/specs/2026-10-17-akoya-redesign-design.md` (builds on `docs/superpowers/specs/2026-10-17-silk-design.md`). Visual targets live in `docs/design/`.

## Global Constraints

- Bundle id `com.rohitnikamm.akoya`; app/executable `Akoya`; targets `AkoyaCore` / `Akoya` / `AkoyaCoreTests`; status item autosave name `AkoyaStatusItem`; log subsystem `com.rohitnikamm.akoya`; settings keys `akoya.*`.
- Models live in `~/Library/Application Support/Akoya/Models`; move `…/Silk/Models` there on first launch if needed.
- The dropdown and onboarding are dark-only. Colours come from `AkoyaTheme` tokens (spec table) and never as literals scattered across views.
- Font: Outfit 400/500/600, bundled and registered at launch. The Settings window keeps the system font.
- No glow outline or halo outside cards. The rim glow appears only while recording: opacity `0.2 + 0.6 × level`, attack ≈ 0.10 s, release ≈ 0.45 s; with Reduce Motion it is a fixed 0.5 while recording.
- The dropdown panel is non-activating and never takes focus from the frontmost app.
- Live preview runs every 0.6 s, only in practice mode, never overlaps another transcription, and starts only after the loudest step exceeds `AppCoordinator.silenceMaxStep` with at least 0.5 s of audio.
- Haptics: `.levelChange` when a permission turns green, `.generic` when onboarding finishes. Nothing during dictation.
- Every new view must be rendered to PNG with `ImageRenderer` and compared against its target in `docs/design/` before the task is reported done. Put the PNGs in the task report; never commit them.
- Never publish (no `gh repo create`, `gh release`, `git push`) and never run `make-signing-cert.sh`.

## Review Focus

1. **The dropdown is opened while recording or transcribing.** It shows the live state (glow and "Listening"/"Transcribing") and doesn't interrupt dictation or take focus. Test in Task 4 (state mapping); manual check in Task 7.
2. **A permission is revoked while the app runs, or the model fails mid-onboarding.** The status card shows "Setup needed"/"Model failed" with a working action, and onboarding can be reopened at step 2. Tests in Task 4 and Task 5.
3. **Practice mode is still active when onboarding closes** (Skip/Done/window closed mid-recording). The coordinator returns to paste mode and nothing gets pasted into the onboarding window or lost. Test in Task 3.
4. **Live preview against a slow model, for example the first pass after load.** Ticks are skipped rather than queued, and the final pass always replaces the partial text. Test in Task 3.
5. **Both a Silk models folder and an Akoya models folder exist,** or the move fails. Akoya keeps its own folder, never deletes data, and falls back to a download. Test in Task 1.

---

### Task 1: Rename Silk → Akoya

**Files:**
- Move: `Sources/Silk` → `Sources/Akoya`, `Sources/SilkCore` → `Sources/AkoyaCore`, `Tests/SilkCoreTests` → `Tests/AkoyaCoreTests`; `SilkSettings.swift` → `AkoyaSettings.swift` (type `AkoyaSettings`), and its test file to match.
- Modify: `Package.swift`, `Resources/Info.plist`, `scripts/*.sh`, `packaging/silk.rb` → `packaging/akoya.rb`, `README.md`, `docs/MANUAL_CHECKS.md`, every `import SilkCore`, `StatusItemController.autosaveName`, every logger subsystem, the `ParakeetTranscriber.defaultDirectory` path.
- Create: `Sources/AkoyaCore/Transcription/ModelsFolderMigration.swift`
- Test: `Tests/AkoyaCoreTests/ModelsFolderMigrationTests.swift`; update `AkoyaSettingsTests.swift`.

**Interfaces:**
- Produces: `AkoyaSettings` (same API as `SilkSettings`, keys `akoya.hotkey` / `akoya.mode` / `akoya.look` / `akoya.redTint` / `akoya.hideIdleIcon` / `akoya.expandAnimation`, plus a new `@Published public var onboardingCompleted: Bool` stored at `akoya.onboardingCompleted`, default false).
- Produces: `public enum ModelsFolderMigration { public static func migrate(from old: URL, to new: URL, fileManager: FileManager = .default) -> Result }`, where `Result` is `.moved` / `.skippedTargetExists` / `.skippedNoSource` / `.failed(String)`.

- [ ] **Step 1:** Do the moves with `git mv` and run a text-replace pass: `SilkCore`→`AkoyaCore`, `Silk.app`→`Akoya.app`, `com.rohitnikamm.silk`→`com.rohitnikamm.akoya`, `SilkStatusItem`→`AkoyaStatusItem`, `silk.` defaults keys→`akoya.`, and user-facing "Silk"→"Akoya" in strings, menus, onboarding copy, README and scripts. Then grep for leftover `Silk`/`silk` and resolve each one; the only remaining hits should be the migration source path and the signing fallback.
- [ ] **Step 2:** `build-app.sh` signs with "Akoya Self-Signed", falls back to "Silk Self-Signed", then to ad-hoc, and prints which one it used. `make-signing-cert.sh` creates "Akoya Self-Signed". `release.sh` requires the "Akoya Self-Signed" or "Silk Self-Signed" authority. The cask is `akoya` with a `zap` on the Akoya paths and `uninstall quit: "com.rohitnikamm.akoya"`.
- [ ] **Step 3: Failing tests.**

```swift
@Test func movesWhenOnlyOldExists()      // old has a file, new missing → .moved; file now at new path, old gone
@Test func keepsExistingTarget()          // both exist → .skippedTargetExists; both untouched
@Test func noSourceIsNoop()               // neither → .skippedNoSource
@Test func createsParentOfTarget()        // new's parent dir missing → created, .moved
@Test func settingsDefaultsUnderAkoyaKeys()  // AkoyaSettings on an empty suite: defaults; writes land under "akoya.hotkey" etc.
@Test func onboardingCompletedPersists()
```

- [ ] **Step 4:** Run `swift test --filter "ModelsFolderMigration|AkoyaSettings"`. Expected: FAIL.
- [ ] **Step 5:** Implement the migration with `FileManager.moveItem`, and call it from `AppDelegate` before `prepare`, logging the result. The target is `…/Application Support/Akoya/Models` and the source is `…/Application Support/Silk/Models`.
- [ ] **Step 6:** Run `swift test` (all green, paste the tail), then `scripts/build-app.sh debug 0.2.0` (expect `build/Akoya.app`, signed with one of the identities), then the launch smoke (`open build/Akoya.app`, `pgrep -x Akoya`, `pkill -x Akoya`).
- [ ] **Step 7:** Commit with message `refactor: rename Silk to Akoya`.

### Task 2: Akoya visual kit (theme, font, logo, gradients, rim glow)

**Files:**
- Create: `Sources/AkoyaCore/UI/Theme/AkoyaTheme.swift`, `Sources/AkoyaCore/UI/Theme/AkoyaFonts.swift`, `Sources/AkoyaCore/UI/Theme/DotLogo.swift`, `Sources/AkoyaCore/UI/Theme/CardBackgrounds.swift`, `Sources/AkoyaCore/UI/Theme/RimGlow.swift`, `Sources/AkoyaCore/UI/Theme/GlowEnvelope.swift`, `Sources/AkoyaCore/Resources/Fonts/Outfit-{Regular,Medium,SemiBold}.ttf` (OFL; include `OFL.txt`).
- Modify: `Package.swift` (resources for `AkoyaCore`); `scripts/build-app.sh` only if the resource bundle copy needs it.
- Test: `Tests/AkoyaCoreTests/GlowEnvelopeTests.swift`, `Tests/AkoyaCoreTests/DotLogoTests.swift`

**Interfaces:**
- Produces: `public enum AkoyaTheme` with static `Color`s `surface`, `panelTop`, `panelBottom`, `inner`, `indigoTop`, `indigoBottom`, `lavender`, `lavenderSoft`, `textPrimary`, `textSecondary`, plus radii `panel = 30`, `card = 24`, `window = 52`.
- Produces: `public enum AkoyaFonts { public static func register(); public static func outfit(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font }`. If registration fails it falls back to the system font.
- Produces: `public struct DotLogo: View { public init(dotSize: CGFloat = 6, glow: Bool = true) }`, with columns `[2, 4, 6, 4, 2]` and `public static let columns`.
- Produces: `extension View { func akoyaV1Card(cornerRadius: CGFloat) -> some View; func akoyaDotGrid(pitch: CGFloat = 12, opacity: Double = 0.06) -> some View; func akoyaRimGlow(intensity: Double, cornerRadius: CGFloat, scale: RimGlowScale) -> some View }`, where `public enum RimGlowScale { case card, compact }` (card = the onboarding recipe, compact = about half the blur/spread for the dropdown card).
- Produces: `public struct GlowEnvelope { public init(attack: TimeInterval = 0.10, release: TimeInterval = 0.45); public mutating func step(target: Double, dt: TimeInterval) -> Double; public static func opacity(level: Double, recording: Bool, reduceMotion: Bool) -> Double }`

- [ ] **Step 1: Failing tests.**

```swift
@Test func opacityAtRestIsZero() { #expect(GlowEnvelope.opacity(level: 0.9, recording: false, reduceMotion: false) == 0) }
@Test func opacityMapsLevel() { #expect(GlowEnvelope.opacity(level: 1, recording: true, reduceMotion: false) == 0.8); #expect(GlowEnvelope.opacity(level: 0, recording: true, reduceMotion: false) == 0.2) }
@Test func reduceMotionIsConstant() { #expect(GlowEnvelope.opacity(level: 0.1, recording: true, reduceMotion: true) == 0.5) }
@Test func attackFasterThanRelease()   // from 0, step(target:1, dt:0.1) ≥ 0.6; from 1, step(target:0, dt:0.1) ≥ 0.6 remaining (i.e. release slower)
@Test func envelopeConverges()         // 2 s of steps at dt 1/60 toward 1 → > 0.99
@Test func dotLogoColumns() { #expect(DotLogo.columns == [2, 4, 6, 4, 2]) }
```

- [ ] **Step 2:** Run `swift test --filter "GlowEnvelope|DotLogo"`. Expected: FAIL.
- [ ] **Step 3:** Implement. `GlowEnvelope` is a one-pole smoother with separate attack and release time constants (`α = 1 − exp(−dt/τ)`). `akoyaRimGlow` draws the colour inward from the edge with nothing outside the shape. Use layered blurred `strokeBorder`s of `RoundedRectangle(style: .continuous)`, clipped to the shape, in the spec's colour stops (lavender at the edge → indigo → transparent), brighter at the bottom and top as in the recipe. Scale the opacity by `intensity`. `akoyaV1Card` is a `surface` fill with the spec's top radial gradient.
- [ ] **Step 4: Visual check.** Add a temporary test or executable snippet that uses `ImageRenderer` to render a 420×520 card at intensity 0 / 0.5 / 0.8 with `.card`, and a 292×170 card at 0.8 with `.compact`, to PNGs in `/tmp`. Read them next to `docs/design/tryit-glow-target.png` and `docs/design/dropdown-card-glow-target.png`, and adjust until the rim width, colour progression, dark centre and brighter bottom match. Put the final PNG paths in the report; don't commit the renders.
- [ ] **Step 5:** Run `swift test`. Expected: green.
- [ ] **Step 6:** Commit with message `feat: Akoya visual kit (theme, Outfit, dot logo, rim glow)`.

### Task 3: Coordinator: voice level, last dictation, practice mode with live preview

**Files:**
- Modify: `Sources/AkoyaCore/Dictation/Ports.swift` (add `snapshot()`), `Sources/AkoyaCore/Audio/AudioCapture.swift`, `Sources/AkoyaCore/Dictation/AppCoordinator.swift`, `Sources/AkoyaCore/UI/WaveformView.swift` (`WaveformModel` fields)
- Create: `Sources/AkoyaCore/Dictation/LivePreviewScheduler.swift`
- Test: `Tests/AkoyaCoreTests/LivePreviewSchedulerTests.swift`, additions to `AppCoordinatorTests.swift` and `AudioCaptureTests.swift`

**Interfaces:**
- Consumes: the existing `AppCoordinator`, `AudioRecording`, `Transcribing`, `LevelMeter`.
- Produces: `AudioRecording.snapshot() -> [Float]`, which copies the samples captured so far without clearing them. `AudioCapture` implements it, and so must the test fakes.
- Produces on `WaveformModel`: `@Published public var voiceLevel: Double` (0…1, which is `(step − 1) / 7` from the latest RMS, 0 when not recording), and `@Published public var lastDictation: String?`, set after a successful paste-mode insert and in memory only.
- Produces on `AppCoordinator`:

```swift
public enum Destination { case paste, practice(onText: @MainActor (String, _ isFinal: Bool) -> Void) }
public var destination: Destination = .paste   // set by onboarding; reset to .paste on finish/close
```

- Produces: `public struct LivePreviewScheduler { public init(interval: TimeInterval = 0.6, minAudio: TimeInterval = 0.5); public mutating func shouldRun(now: TimeInterval, audioSeconds: Double, loudestStep: Int, inFlight: Bool) -> Bool }`

- [ ] **Step 1: Failing tests.**
  - Scheduler:
    - `waitsForSpeech`: loudestStep 2 → false.
    - `waitsForMinAudio`: 0.4 s → false.
    - `runsOnInterval`: true at t=0, false at t=0.3, true at t=0.61.
    - `skipsWhileInFlight`.
  - Coordinator, paste mode:
    - `voiceLevelFollowsRMS`: 1.0 gives voiceLevel 1, 0 gives 0, and stop resets it to 0.
    - `lastDictationSetAfterInsert`.
    - `pasteModeNeverPreviews`: the transcriber is called exactly once.
  - Coordinator, practice mode, using a manual clock and a manual tick hook:
    - `practiceEmitsPartials`: after speech and a tick, `onText(partial, false)` is called.
    - `practiceFinalReplacesPartial`: on release, `onText(final, true)`; the inserter is never called; `lastDictation` is unchanged.
    - `practiceNoOverlap`: a slow fake transcriber plus two ticks → one call in flight.
    - `resetToPasteAfterPractice`: when destination is set back to `.paste` mid-recording, release pastes normally.
  - AudioCapture: `snapshotDoesNotClear` (seam: inject samples into the buffer, call `snapshot()` twice, then `stop()` returns everything).
- [ ] **Step 2:** Run `swift test --filter "LivePreview|AppCoordinator|AudioCapture"`. Expected: FAIL.
- [ ] **Step 3:** Implement. In practice mode, a 0.1 s `Task` loop runs during recording and asks the scheduler whether to run. If yes, it takes `audio.snapshot()`, transcribes it and calls `onText(trimmed, false)`, but only if the transcript is non-empty and the coordinator is still recording. Expose an internal `tickLivePreview()` for tests. The silence gate and the error cue are unchanged; in practice mode an empty final calls `onText("", true)` and shows the error cue.
- [ ] **Step 4:** Run `swift test`. Expected: green.
- [ ] **Step 5:** Commit with message `feat: voice level, last dictation, practice mode with live preview`.

### Task 4: Dropdown panel

**Files:**
- Create: `Sources/AkoyaCore/UI/Dropdown/StatusCardState.swift`, `Sources/AkoyaCore/UI/Dropdown/DropdownView.swift`, `Sources/AkoyaCore/UI/Dropdown/StatusPanelController.swift`
- Modify: `Sources/AkoyaCore/UI/StatusItemController.swift` (click toggles the panel instead of `statusItem.menu`), `Sources/Akoya/AppDelegate.swift` (replace the `NSMenu` with the panel and remove the menu code)
- Test: `Tests/AkoyaCoreTests/StatusCardStateTests.swift`

**Interfaces:**
- Consumes: `WaveformModel` (phase, voiceLevel, lastDictation), `ModelState`, `Permissions.allGranted`, `AkoyaSettings` (mode, hotkey), and the Task 2 kit.
- Produces:

```swift
public enum StatusCardState: Equatable {
    case ready, listening, transcribing, downloading(Double?), modelFailed, setupNeeded
    public static func from(phase: DictationPhase, model: ModelState, permissionsGranted: Bool) -> StatusCardState
    public var chipTitle: String   // "Ready" "Listening" "Transcribing" "Downloading 42%" ("Downloading…" if nil) "Model failed" "Setup needed"
}
public struct DropdownActions { public var openSettings, quit, retryModel, finishSetup: () -> Void; public var copyLast: (String) -> Void }
public struct DropdownView: View { public init(model: WaveformModel, settings: AkoyaSettings, modelState: ModelState, permissionsGranted: Bool, actions: DropdownActions) }
@MainActor public final class StatusPanelController { public init(content: some View); public func toggle(relativeTo button: NSStatusBarButton); public func close(); public var isShown: Bool { get } }
```

- [ ] **Step 1: Failing tests.** Precedence: `setupNeeded` beats everything except active `.recording`/`.transcribing` phases; then `modelFailed`, then `downloading`, then the phase.
  - `recordingIsListening`
  - `transcribing`
  - `idleReady`
  - `downloadingShowsPercent` (`.downloading(0.42).chipTitle == "Downloading 42%"`)
  - `downloadingUnknown` (`"Downloading…"`)
  - `failed`
  - `setupNeededWhenIdle`
  - `recordingBeatsSetupNeeded`
  - `errorPhaseShowsReady`
- [ ] **Step 2:** Run `swift test --filter StatusCardState`. Expected: FAIL.
- [ ] **Step 3:** Implement the state and the views per `docs/design/dropdown-mockup.html`:
  - The status card uses the V1 gradient at rest, and `akoyaRimGlow(intensity: GlowEnvelope-smoothed opacity, .compact)` while listening.
  - The status card has the dot logo, the chip and the keycap with the hotkey name.
  - "Retry" and "Finish setup" buttons appear in the card for their states.
  - Below it: the mode segmented pill bound to `settings.mode`; the last-dictation card (hidden when nil) with Copy; a footer with a Settings pill and a round Quit button.
  - The panel is an `NSPanel` with styleMask `[.nonactivatingPanel, .borderless]`, `isFloatingPanel`, `level = .statusBar`, transparent background and `hidesOnDeactivate = false`. It is positioned under the button, right-aligned, with an 8 pt gap, clamped to the screen's visible frame.
  - Close it on a global mouse-down monitor outside the panel, on Esc (local key monitor while key), and on a second item click.
  - Copy uses `NSPasteboard.general`.
- [ ] **Step 4: Visual check.** Use `ImageRenderer` to render `DropdownView` in the ready, listening (voiceLevel 0.8), downloading(0.42) and setupNeeded states to `/tmp` PNGs. Compare them with `docs/design/dropdown-mockup.html` (read its structure) and `docs/design/dropdown-card-glow-target.png`, and fix any differences. Put the paths in the report.
- [ ] **Step 5:** Wire it into AppDelegate and delete the old menu code. Run `swift test`, then `scripts/build-app.sh debug 0.2.0` and the launch smoke.
- [ ] **Step 6:** Commit with message `feat: Akoya dropdown panel`.

### Task 5: Onboarding (3 steps, live practice, haptics)

**Files:**
- Create: `Sources/AkoyaCore/UI/Onboarding/OnboardingFlow.swift`, `Sources/AkoyaCore/UI/Onboarding/OnboardingWindow.swift`, `Sources/AkoyaCore/UI/Onboarding/WelcomeStep.swift`, `Sources/AkoyaCore/UI/Onboarding/PermissionsStep.swift`, `Sources/AkoyaCore/UI/Onboarding/TryItStep.swift`, `Sources/AkoyaCore/UI/Haptics.swift`
- Delete: `Sources/AkoyaCore/UI/OnboardingView.swift` (and update `WindowPresenter` to drop it)
- Modify: `Sources/AkoyaCore/UI/WindowPresenter.swift`, `Sources/Akoya/AppDelegate.swift`
- Test: `Tests/AkoyaCoreTests/OnboardingFlowTests.swift`

**Interfaces:**
- Consumes: the `Permissions` APIs, `AppCoordinator.destination` (Task 3), `WaveformModel`, `AkoyaSettings.onboardingCompleted`, the Task 2 kit, and the Task 4 `StatusCardState` (not needed directly).
- Produces:

```swift
public enum OnboardingStep: Equatable { case welcome, permission(Permission), tryIt }
public struct OnboardingFlow {
    public init(start: OnboardingStep = .welcome)
    public private(set) var step: OnboardingStep
    public mutating func next(granted: (Permission) -> Bool)            // welcome → first ungranted permission (order mic, accessibility, input) or tryIt
    public mutating func refresh(granted: (Permission) -> Bool) -> Bool // on a permission step: if granted → advance, return true (caller fires haptic)
    public static func firstIncomplete(granted: (Permission) -> Bool) -> OnboardingStep
}
public enum Haptics { public static func permissionGranted(); public static func finished() }
@MainActor public final class OnboardingWindowController { public init(coordinator: AppCoordinator, model: WaveformModel, settings: AkoyaSettings, hotkeyName: @escaping () -> String); public func show(startAt: OnboardingStep = .welcome); public func close() }
```

- [ ] **Step 1: Failing tests.**
  - `welcomeGoesToFirstMissing`: mic granted, AX missing → `.permission(.accessibility)`.
  - `allGrantedSkipsToTryIt`.
  - `refreshAdvancesAndReports`.
  - `refreshNoChangeReturnsFalse`.
  - `orderIsMicAxInput`.
  - `firstIncompleteForReopen`: used by "Finish setup".
- [ ] **Step 2:** Run `swift test --filter OnboardingFlow`. Expected: FAIL.
- [ ] **Step 3:** Implement per the spec and the mockups (`docs/design/onboarding-screens-1-2-mockup.html` for steps 1–2 in the V1 look, `docs/design/onboarding-screen-3-tryit-mockup.html` plus `tryit-glow-target.png` for step 3):
  - The window is borderless and transparent with a 420×520 continuous-corner card, centred, movable by its background, and has a close (×) control.
  - Steps cross-fade over 250 ms, with page dots.
  - The permissions step has chips, one card for the active permission and its single action (spec rules), a 0.5 s poll via `refresh`, and a haptic on each advance.
  - Try it: on appear, set `coordinator.destination = .practice { text, isFinal in … }`, and show the dot waveform from `model.columns`. The rim glow follows `GlowEnvelope.opacity(voiceLevel…)`. Live text is shown as it arrives; Done becomes primary after the first non-empty final; a quiet "Skip" link is always available.
  - On Done, Skip or close: reset `destination = .paste`, set `onboardingCompleted = true` (Done/Skip only), fire `Haptics.finished()` on Done, and close.
  - AppDelegate shows onboarding when `!onboardingCompleted || !Permissions.allGranted` (starting at `firstIncomplete` if already completed once). The dropdown's "Finish setup" opens it at `firstIncomplete`.
- [ ] **Step 4: Visual check.** Use `ImageRenderer` on each step: welcome; permission(.accessibility) with mic done; try-it at rest and with voiceLevel 0.8 plus partial text. Compare with the mockups and targets, fix any differences, and put the paths in the report.
- [ ] **Step 5:** Run `swift test`, then build and the launch smoke.
- [ ] **Step 6:** Commit with message `feat: Akoya 3-step onboarding with live practice`.

### Task 6: App icon and Settings polish

**Files:**
- Create: `scripts/make-icon.swift` (renders the icon PNGs with AppKit/CoreGraphics), `Resources/AppIcon.icns` (generated and committed)
- Modify: `scripts/build-app.sh` (copy the icns), `Resources/Info.plist` (`CFBundleIconFile` = `AppIcon`), `Sources/AkoyaCore/UI/SettingsView.swift`
- Test: none (visual); the build output is checked.

- [ ] **Step 1:** `make-icon.swift` draws a 1024 px dark continuous-corner square (`surface` colour with the V1 top gradient) and the 5-column dot logo in lavender-white dots with a soft glow. It writes the standard `AppIcon.iconset` sizes (16–1024, @1x/@2x), and then `iconutil -c icns` builds `Resources/AppIcon.icns`. Run it with `swift scripts/make-icon.swift`, Read the 512 px PNG to check it, and commit the `.icns`.
- [ ] **Step 2:** `build-app.sh` copies `Resources/AppIcon.icns` into `Contents/Resources`; Info.plist gets `CFBundleIconFile`. Build and confirm the file is in the bundle with `plutil -p`.
- [ ] **Step 3:** In `SettingsView`, use the indigo accent (`.tint(AkoyaTheme.indigoTop)`), title the window "Akoya Settings", group the controls into sections, and keep Launch at Login there.
- [ ] **Step 4:** Run `swift test`, then build and the launch smoke. Commit with message `feat: Akoya app icon and settings polish`.

### Task 7: Docs, release scripts, end-to-end smoke

**Files:**
- Modify: `README.md`, `docs/MANUAL_CHECKS.md` (new flows), `scripts/release.sh` (verify the akoya names)

- [ ] **Step 1:** Rewrite the manual checklist for Akoya. It must cover: onboarding end to end (permission order, auto-advance, haptics, live words, Done/Skip); the dropdown (open/close, no focus steal, glow while dictating, Copy, mode switch, Settings, Quit, Finish setup, Retry); the app icon in the System Settings privacy lists; and models moved from Silk with no re-download.
- [ ] **Step 2:** Run `scripts/release.sh --dry-run 0.2.0` (commit first). Expected: builds `Akoya-v0.2.0.zip`, renders the `akoya` cask, publishes nothing.
- [ ] **Step 3:** Run `swift test` (paste the tail). Commit with message `docs: Akoya manual checks and release notes`.

*After merge (controller, with Rohit's OK): move `~/Developer/silk` → `~/Developer/akoya`.*
