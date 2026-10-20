# Akoya for Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows 11 version of Akoya (hold Right Alt, speak, release, text is pasted), with a floating pill, a tray panel and a three-step setup, installable from GitHub releases.

**Architecture:** A pure `Akoya.Core` library (state machine, hotkey decoding, levels, mic naming, settings) ported from the Mac's `Sources/AkoyaCore`, and a WPF `Akoya.App` with thin Win32/NAudio/sherpa-onnx adapters and the views. Development happens in a Linux cloud workspace without .NET; every push is built and tested on a GitHub Actions `windows-latest` runner, which also renders every view to PNG for visual review.

**Tech Stack:** C# / .NET 10 (LTS), WPF, xUnit, NAudio (WASAPI), sherpa-onnx (Parakeet TDT 0.6B v2 int8), SharpCompress, H.NotifyIcon.Wpf, Velopack, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-20-akoya-windows-design.md` (read it first; the visual targets are listed at its top).

## Global Constraints

- Target `net10.0` (Core, Core.Tests) and `net10.0-windows` with `<UseWPF>true</UseWPF>` (App, App.Tests). `Nullable` enable, `TreatWarningsAsErrors` true, `LangVersion` latest, set once in `windows/Directory.Build.props`.
- Package versions pinned once in `windows/Directory.Packages.props` (central package management). Before adding a package, look up its current stable version with WebFetch on `https://www.nuget.org/packages/<id>` and pin it exactly. NuGet itself is unreachable from the workspace; only CI restores.
- **You cannot build or run .NET locally.** The only build/test is CI: run `windows/scripts/ci.sh` (Task 0). "Run the tests" in any step means that, and its printed summary is the evidence.
- Commits: author is already configured (Rohit Nikam). **No `Co-Authored-By: Claude` or "Generated with Claude Code" lines** (repo `CLAUDE.md`). Branch: `windows-design`. Never push to `main`.
- Copy (all user-visible strings) exactly as in the spec and the mockups; no em dashes in copy.
- Colours: accent `#2F6BFF` → `#1D4ED8`, surface `#03060F`, light accent `#93C5FD`, amber `#FBBF24`, red `#EF4444`, green text `#86EFAC` / dot `#4ADE80`. Font: Outfit (`Sources/AkoyaCore/Resources/Fonts/Outfit-Variable.ttf`, OFL; copy it with `OFL.txt`).
- Timings: hold threshold 0.30 s; max recording 300 s; silence gate: loudest step ≤ 2 is silent; paste restore delay 300 ms; Pasted shown 0.9 s; red alerts shown 1.6 s; pill fade/rise 0.28 s, cubic-bezier(.2,.8,.2,1); live preview tick 100 ms; level events ~25 Hz (40 ms RMS window); mic "isn't hearing you" after 3 s.
- Pill: 34 px tall (DIP), radius 17, 23 columns × 4 rows of 3 px dots with 2 px gaps, 18 DIP above the work area bottom, on the monitor of the foreground window.
- Paths: settings `%AppData%\Akoya\settings.json`; model `%LocalAppData%\Akoya\models\sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8\`; logs `%LocalAppData%\Akoya\logs\`.
- Every UI task ends by committing PNG renders through CI and comparing them with the mockup (open the mockup with Playwright via `node`, screenshot, and look at both). Rohit wants every visual result checked before it's shown.

## Review Focus

1. **Hotkey key-up never arrives** (Win+L, UAC prompt or Ctrl+Alt+Del while holding Right Alt): recording must not stay on forever. Session lock/switch cancels recording and resets the decoder. Test in Task 3 (`SessionLocked_CancelsRecording`) and Task 2 (`Reset_ClearsHeldState`).
2. **Clipboard held open by another app** (`OpenClipboard` fails): retry 10 × 20 ms, then show the red "Couldn't paste" alert and keep the text as Last dictation, never crash or lose the user's clipboard. Test in Task 9 (`Insert_ClipboardBusy_RetriesThenReportsFailure`).
3. **Long dictations near 300 s:** transcription must complete and not run out of memory. Test in Task 7 (`Transcribe_120Seconds_Completes`, 0.wav tiled to 120 s, < 60 s on the runner).
4. **Corrupt or old `settings.json`** (truncated JSON, unknown hotkey name, missing fields): load defaults for the bad fields, keep the good ones, never crash at launch. Test in Task 5.
5. **Mixed-DPI and odd taskbar layouts** (150 % secondary monitor, taskbar on top or left, negative monitor coordinates): the pill stays fully on the target monitor's work area, bottom-centred. Test in Task 6 (`PillPlacement` cases).

---

## File map

```
.github/workflows/windows.yml          CI: build, test, render, publish results to ci-output
.github/workflows/release-windows.yml  tag windows-v* → draft release with Setup.exe (Task 13)
windows/
  Directory.Build.props  Directory.Packages.props  Akoya.Windows.slnx  README.md
  scripts/ci.sh                         push + wait + fetch results (workspace side)
  scripts/ci-summary.ps1                runner side: trx/build log → summary.md + annotations
  src/Akoya.Core/
    Audio/LevelMeter.cs RmsWindower.cs Resampler.cs
    Dictation/Ports.cs DictationCoordinator.cs LivePreviewScheduler.cs IScheduler.cs
    Hotkey/HotkeyDecoder.cs
    Mics/EndpointFacts.cs MicrophoneNaming.cs MicrophoneResolver.cs MeterLevel.cs
    Settings/AkoyaSettings.cs SettingsStore.cs
    Ui/PillDots.cs PillPlacement.cs GlowEnvelope.cs
    Model/ModelManifest.cs
  src/Akoya.App/
    App.xaml(.cs) AppHost.cs Program.cs
    Theme/Theme.xaml Fonts/Outfit-Variable.ttf Fonts/OFL.txt Assets/akoya.ico
    Controls/DotGrid.cs RimGlowBorder.cs NeutralSwitch.xaml
    Pill/PillWindow.xaml(.cs) PillViewModel.cs PillPlacer.cs
    Tray/TrayIconHost.cs TrayPanelWindow.xaml(.cs) TrayPanelViewModel.cs
    Setup/SetupWindow.xaml(.cs) SetupViewModel.cs WelcomeView.xaml MicrophoneView.xaml TryItView.xaml
    Platform/KeyboardHook.cs ClipboardPasteInserter.cs ElevationCheck.cs WasapiRecorder.cs
             MicrophoneCatalog.cs MultiMeter.cs MicPrivacy.cs SingleInstance.cs StartupRegistration.cs
             Updater.cs DispatcherScheduler.cs
    Speech/SherpaTranscriber.cs ModelStore.cs
  tests/Akoya.Core.Tests/   one test file per Core file
  tests/Akoya.App.Tests/    Snapshots/*.cs (renders), SherpaTranscriberTests.cs, ClipboardTests.cs
docs/MANUAL_CHECKS_WINDOWS.md
```

---

### Task 0: Solution skeleton and the CI loop

**Files:**
- Create: `windows/Directory.Build.props`, `windows/Directory.Packages.props`, `windows/Akoya.Windows.slnx`, `windows/src/Akoya.Core/Akoya.Core.csproj`, `windows/src/Akoya.App/Akoya.App.csproj` + `App.xaml(.cs)` (empty window that never shows), `windows/tests/Akoya.Core.Tests/*.csproj` + `SmokeTests.cs`, `windows/tests/Akoya.App.Tests/*.csproj` + `Snapshots/SnapshotHarness.cs` + `Snapshots/HarnessTests.cs`
- Replace: `.github/workflows/windows.yml` (currently a probe)
- Create: `windows/scripts/ci.sh`, `windows/scripts/ci-summary.ps1`

**Interfaces:**
- Produces: `windows/scripts/ci.sh` — usage `ci.sh` (push current branch, wait, print summary, exit 0/1) and `ci.sh --wait-only` (resume waiting for HEAD). Prints the path of the local results folder `$SCRATCH/ci/<sha>/` containing `summary.md`, `build.log`, `tests/*.trx`, `snapshots/*.png`.
- Produces: `SnapshotHarness.Render(FrameworkElement element, double width, double height, string name)` — renders on an STA thread with `RenderTargetBitmap` at 2× scale and writes `<AKOYA_SNAPSHOT_DIR>/<name>.png` (no-op when the env var is unset). All later UI tasks use it.

- [ ] **Step 1: Write the projects and one passing test per project**

`SmokeTests.Core_IsReachable()` asserts `typeof(Akoya.Core.Marker).Assembly` is not null. `HarnessTests.Render_WritesPng()` renders a 100×40 `Border` (background `#2F6BFF`) as `harness-check` and asserts the PNG exists and is larger than 100 bytes when `AKOYA_SNAPSHOT_DIR` is set.

- [ ] **Step 2: Write the workflow**

`windows.yml`: triggers `push` (paths `windows/**`, `.github/workflows/windows.yml`) and `workflow_dispatch`; `permissions: contents: write`; `concurrency: {group: windows-${{ github.ref }}, cancel-in-progress: true}`; one job `build-test` on `windows-latest`: checkout, `actions/setup-dotnet` with `dotnet-version: 10.0.x`, build (`dotnet build windows/Akoya.Windows.slnx -c Release` teed to `out/build.log`, `continue-on-error`), test (`dotnet test` with `--logger trx --results-directory out/tests`, env `AKOYA_SNAPSHOT_DIR=out/snapshots`, `if: always()`), `pwsh windows/scripts/ci-summary.ps1 out` (`if: always()`), publish `out/` as `runs/<sha>/` in a single-commit orphan branch `ci-output` force-pushed with the workflow token (`if: always()`), final step fails the job if build or test failed.

`ci-summary.ps1 <outDir>`: writes `summary.md` with build result, first 40 `error` lines from `build.log`, test totals per trx, and each failed test's name + message + first 15 stack lines; emits `::error title=<test>::<message>` per failure.

- [ ] **Step 3: Write `ci.sh`**

Bash. Push the current branch; poll `gh api repos/rohitnikamm/akoya/commits/<sha>/check-runs` every 10 s until the run named `build-test` is `completed` (give up after 9 min with exit code 2 and the message `still running, re-run: ci.sh --wait-only`); then `git fetch origin ci-output` and extract `runs/<sha>/` with `git archive` into `$SCRATCH/ci/<sha>/` (`SCRATCH` defaults to `/tmp/claude-0/-home-claude/9febb593-37b7-55a6-bd1f-0fcd51f28b1c/scratchpad`); print `summary.md`; exit 0 on `success`, 1 otherwise. Note: the Actions API is blocked from the workspace; only the Checks API and git work.

- [ ] **Step 4: Run it**

Run: `windows/scripts/ci.sh` (Bash timeout 600000).
Expected: exit 0; summary shows build succeeded, 2 tests passed; `snapshots/harness-check.png` exists locally and is a blue rectangle (look at it with Read).

- [ ] **Step 5: Commit** (`ci: Windows solution skeleton and CI loop`). Delete the probe workflow content (replaced).

---

### Task 1: Levels, waveform dots and glow (Core)

**Files:**
- Create: `windows/src/Akoya.Core/Audio/LevelMeter.cs`, `Audio/RmsWindower.cs`, `Ui/PillDots.cs`, `Ui/GlowEnvelope.cs`, `Mics/MeterLevel.cs`
- Test: `windows/tests/Akoya.Core.Tests/LevelMeterTests.cs`, `RmsWindowerTests.cs`, `PillDotsTests.cs`, `GlowEnvelopeTests.cs`, `MeterLevelTests.cs`

**Interfaces:**
- Produces: `LevelMeter(int columnCount = 23)` with `IReadOnlyList<int> Columns`, `void Push(float rms)`, `void Reset()`, `static int Step(float rms)` (1…8). Port of `Sources/AkoyaCore/Audio/LevelMeter.swift` (same dBFS mapping −50 → 1, −10 → 8, release at most 2 steps per push).
- Produces: `RmsWindower(double sampleRate = 16000, double windowSeconds = 0.04)` with `IReadOnlyList<float> Append(ReadOnlySpan<float> samples)` (port of `RMSWindower.swift`).
- Produces: `PillDots.Listening(IReadOnlyList<int> steps) -> double[]` (length columns × 4, column-major, row 0 top) and `PillDots.Scanner(double elapsedSeconds, int columns = 23) -> double[]`, `PillDots.Rows = 4`.
- Produces: `GlowEnvelope(double attack = 0.10, double release = 0.45)` with `double Step(double target, double dt)` and `static double Opacity(double level, bool recording, bool reduceMotion)` (port of `GlowEnvelope.swift`).
- Produces: `MeterLevel.LitDots(int step, int dots = 10) -> int` for the setup row meters.

- [ ] **Step 1: Port the Mac tests** — translate every case in `Tests/AkoyaCoreTests/LevelMeterTests.swift`, `RMSWindowerTests.swift`, `GlowEnvelopeTests.swift` to xUnit with identical values (LevelMeter tests use `columnCount: 22` where the Mac assumed 22). Add:
  - `PillDots.Listening`: step 1 → middle rows (1, 2) opacity 0.35, outer rows (0, 3) 0.12; step 2 → middle 0.95, outer 0.12; step 8 → all four 0.95; step 6 → outer `0.12 + 0.83 × (6 − 4) / 3`.
  - `PillDots.Scanner`: same head/tail opacities as `WaveformGeometry.scannerOpacities` (period 1.4 s, 1 / 0.7 / 0.48 / 0.32 / 0.18) applied to all 4 rows; `elapsed = 0` → column 0 is 1.0, column 22 is 0.18.
  - `MeterLevel.LitDots`: step 1 → 0, step 8 → 10, step 4 → `round((4 − 1) / 7 × 10)` = 4.
- [ ] **Step 2: Run** `windows/scripts/ci.sh` — expected FAIL (types missing).
- [ ] **Step 3: Implement** the five files.
- [ ] **Step 4: Run** `windows/scripts/ci.sh` — expected PASS.
- [ ] **Step 5: Commit** (`feat(windows): levels, pill dots and glow envelope`).

---

### Task 2: Hotkey decoder (Core)

**Files:**
- Create: `windows/src/Akoya.Core/Hotkey/HotkeyDecoder.cs`
- Test: `windows/tests/Akoya.Core.Tests/HotkeyDecoderTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum HotkeyKey { RightAlt, RightCtrl, Copilot }
public enum HotkeyEvent { Pressed, Released, Cancel }
public readonly record struct RawKey(int VkCode, int ScanCode, bool Extended, bool Injected, bool IsDown);
public readonly record struct DecodeResult(bool Swallow, HotkeyEvent? Event, bool InjectMaskKey = false);
public sealed class HotkeyDecoder {
    public HotkeyDecoder(HotkeyKey key);
    public HotkeyKey Key { get; set; }          // setting it calls Reset()
    public bool RecordingActive { get; set; }   // Esc is only intercepted while true
    public DecodeResult Decode(RawKey key);
    public void Reset();
}
```
Virtual keys: `VK_RMENU 0xA5`, `VK_RCONTROL 0xA3`, `VK_LCONTROL 0xA2`, `VK_LWIN 0x5B`, `VK_LSHIFT 0xA0`, `VK_F23 0x86`, `VK_ESCAPE 0x1B`.

- [ ] **Step 1: Write the failing tests**
  - `RightAlt_DownUp_PressedThenReleased_BothSwallowed`
  - `RightAlt_Repeat_IgnoredButSwallowed` (second down while held → `Swallow: true, Event: null`)
  - `AltGr_FakeLeftCtrl_Swallowed` (`VK_LCONTROL`, scan `0x21D`, down and up, key = RightAlt → swallowed, no event); `RealLeftCtrl_PassesThrough` (scan `0x1D`)
  - `RightCtrl_Hotkey_RightAltPassesThrough`
  - `Copilot_F23WithWinShift_PressedReleased` (LWin down, LShift down → pass through; F23 down → `Swallow, Pressed, InjectMaskKey: true`; F23 up → `Swallow, Released`; LShift/LWin up → pass through); `F23_WithoutWinShift_PassesThrough`
  - `Escape_WhileRecording_CancelSwallowed`; `Escape_WhenIdle_PassesThrough`; Esc up swallowed only if its down was
  - `InjectedEvents_PassThrough` (`Injected: true` never decoded, so Akoya's own `SendInput` isn't caught)
  - `Reset_ClearsHeldState` (down, Reset, down → `Pressed` again, not a repeat)
- [ ] **Step 2: Run** `ci.sh` — FAIL. **Step 3: Implement.** **Step 4: Run** `ci.sh` — PASS.
- [ ] **Step 5: Commit** (`feat(windows): hotkey decoder`). Also fix the spec's Copilot line to: "swallows F23 and injects a mask key so Windows doesn't open Start; LWin/LShift pass through".

---

### Task 3: Dictation state machine (Core)

**Files:**
- Create: `windows/src/Akoya.Core/Dictation/Ports.cs`, `IScheduler.cs`, `DictationCoordinator.cs`, `LivePreviewScheduler.cs`
- Test: `windows/tests/Akoya.Core.Tests/DictationCoordinatorTests.cs`, `LivePreviewSchedulerTests.cs`, `Fakes.cs` (`FakeRecorder`, `FakeTranscriber`, `FakeInserter`, `ManualScheduler`)

**Interfaces:**
- Consumes: `HotkeyEvent` (Task 2), `LevelMeter.Step` (Task 1).
- Produces:
```csharp
public interface IAudioRecorder {
    event Action<float>? Level;          // RMS 0..1, ~25 Hz
    event Action? Interrupted;           // device lost mid-recording
    void Start(string? deviceId);        // throws MicrophoneMissingException or MicrophoneBlockedException
    float[] Stop();                      // 16 kHz mono
    float[] Snapshot();
    void Cancel();
}
public interface ITranscriber { Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct = default); }
public enum InsertResult { Pasted, CopiedOnly, Failed }
public interface ITextInserter { Task<InsertResult> InsertAsync(string text); }
public interface IScheduler { DateTimeOffset Now { get; } IDisposable After(TimeSpan delay, Action action); }
public enum ModelState { Loading, Ready, Failed }
public enum DictationPhase { Idle, Recording, Transcribing, Pasted, Copied, MicMissing, MicBlocked, ModelNotReady, ModelFailed, PasteFailed }
public abstract record Destination { public sealed record Paste : Destination; public sealed record Practice(Action<string, bool> OnText) : Destination; }
public sealed class DictationCoordinator {
    public DictationCoordinator(IAudioRecorder audio, ITranscriber transcriber, ITextInserter inserter,
        IScheduler scheduler, Func<ModelState> modelState, Func<string?> deviceId);
    public DictationPhase Phase { get; }
    public event Action<DictationPhase>? PhaseChanged;
    public event Action<float>? Level;            // forwarded RMS while recording
    public DateTimeOffset? RecordingStartedAt { get; }
    public string? LastDictation { get; }
    public Destination Destination { get; set; }  // default Paste
    public void Handle(HotkeyEvent e);
    public void SessionLocked();                   // cancels like Esc
}
```
Constants (public static): `HoldThreshold = 0.30 s`, `MaxRecording = 300 s`, `SilenceMaxStep = 2`, `PastedDisplay = 0.9 s`, `AlertDisplay = 1.6 s`, `PreviewTick = 100 ms`.

Behaviour to pin (from the spec's Dictation flow and Errors table): Pressed while Idle → model `Loading` → `ModelNotReady`, `Failed` → `ModelFailed`; `Start` throws `MicrophoneMissingException` → `MicMissing`, `MicrophoneBlockedException` → `MicBlocked`; each shown for `AlertDisplay` then Idle, nothing recorded; else `Recording`. Released ≥ HoldThreshold after start → stop; sooner → latched; next Pressed stops. Cancel/SessionLocked while Recording → `audio.Cancel()`, Idle. Stop: silent (loudest step ≤ 2) → Idle; else `Transcribing` → text trimmed; empty → Idle; Paste destination → `InsertAsync(text + " ")` → `Pasted`/`Copied`/`PasteFailed` for their display time (0.9 s, 0.9 s, 1.6 s) → Idle, `LastDictation = text` in all three; Practice → `OnText(text, true)` → Idle. Interrupted while Recording → stop and transcribe what was captured. Max 300 s → stop. Practice destination: every `PreviewTick` while recording, transcribe `Snapshot()` and call `OnText(partial, false)`; never two transcriptions at once (port `LivePreviewScheduler.swift`).

- [ ] **Step 1: Port `Tests/AkoyaCoreTests/AppCoordinatorTests.swift` and `LivePreviewSchedulerTests.swift`** to xUnit with the fakes, keeping every case, then add: `ModelNotReady_ShowsAlertThenIdle`, `ModelFailed_ShowsAlertThenIdle`, `MicMissing_NothingRecorded_AlertThenIdle`, `MicBlocked_NothingRecorded_AlertThenIdle`, `Pasted_ShownForPointNineSeconds`, `CopiedOnly_GoesToCopied_AndSetsLastDictation`, `InsertFailed_GoesToPasteFailed_AndKeepsLastDictation`, `Interrupted_TranscribesCaptured`, `SessionLocked_CancelsRecording`, `MaxRecording_StopsAt300s`, `Silent_GoesIdle_NoTranscription`, `TrailingSpaceAppended`.
- [ ] **Step 2: Run** `ci.sh` — FAIL. **Step 3: Implement** (no threads inside Core: async continuations resume via the caller's `SynchronizationContext`; tests use a context-free run with `ManualScheduler`). **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** (`feat(windows): dictation state machine`).

---

### Task 4: Microphone naming and resolution (Core)

**Files:**
- Create: `windows/src/Akoya.Core/Mics/EndpointFacts.cs`, `MicrophoneNaming.cs`, `MicrophoneResolver.cs`
- Test: `windows/tests/Akoya.Core.Tests/MicrophoneNamingTests.cs`, `MicrophoneResolverTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum EndpointFormFactor { Unknown, Microphone, Headset, Headphones, LineLevel, DigitalAudioDisplayDevice, Other }
public sealed record EndpointFacts(string Id, string FriendlyName, string DeviceDescription,
    EndpointFormFactor FormFactor, string Enumerator, bool HasPhysicalParent, bool IsDefault);
public sealed record NamedMic(string Id, string Name, bool InMore, bool IsDefault);
public static class MicrophoneNaming { public static IReadOnlyList<NamedMic> Name(IReadOnlyList<EndpointFacts> endpoints); }
public sealed record MicChoice(NamedMic? Mic, bool Missing);
public static class MicrophoneResolver { public static MicChoice Resolve(string? savedId, IReadOnlyList<NamedMic> mics); }
```
Rules: exactly the spec's "Microphone naming rules". Order: main group = built-in first, then the rest alphabetically with webcams last; then the More group alphabetically. Resolver: `savedId` null → the `IsDefault` mic; saved id present → it; saved id absent → `Missing: true, Mic: null` (no fallback).

- [ ] **Step 1: Write the failing tests** with fixtures:
  - Laptop: `Microphone Array (Realtek(R) Audio)` / HDAUDIO / Microphone → "This PC's microphone".
  - Bluetooth: `Headset (Galaxy Buds2 Pro Hands-Free AG Audio)` / BTHHFENUM → "Galaxy Buds2 Pro".
  - Webcam: `Microphone (Logitech BRIO)` / USB → "Webcam mic"; two webcams → "Webcam mic (Logitech BRIO)", "Webcam mic (Integrated Camera)".
  - USB headset: `Headset Microphone (HyperX Cloud III)` / USB / Headset → "HyperX Cloud III".
  - `Microphone (NVIDIA Broadcast)` / ROOT / no physical parent → InMore, name "NVIDIA Broadcast".
  - `VoiceMeeter Output (VB-Audio VoiceMeeter VAIO)` / ROOT → InMore, "Voicemeeter".
  - `Stereo Mix (Realtek(R) Audio)` / HDAUDIO / LineLevel → InMore, "Stereo Mix".
  - Duplicates → " 2". Unknown → friendly name without the `Microphone (…)` wrapper.
  - Order test for the mockup's desk list: HyperX Cloud III, Webcam mic, then More: NVIDIA Broadcast, Stereo Mix.
  - Resolver: null → default; present → it; missing → `Missing`.
- [ ] **Step 2: Run** `ci.sh` — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** (`feat(windows): plain microphone names`).

---

### Task 5: Settings (Core)

**Files:**
- Create: `windows/src/Akoya.Core/Settings/AkoyaSettings.cs`, `SettingsStore.cs`
- Test: `windows/tests/Akoya.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum InferenceProvider { Auto, Cpu }
public sealed record AkoyaSettings(HotkeyKey Hotkey = HotkeyKey.RightAlt, string? MicrophoneId = null,
    bool LaunchAtStartup = false, bool SetupDone = false, InferenceProvider Provider = InferenceProvider.Auto);
public static class SettingsStore {
    public static AkoyaSettings Load(string path);   // missing/corrupt → defaults; per-field fallback
    public static void Save(string path, AkoyaSettings settings); // write .tmp then File.Move(overwrite)
}
```
JSON with camelCase names, enums as strings.

- [ ] **Step 1: Tests:** round trip; missing file → defaults; truncated JSON → defaults; `"hotkey":"LeftWin"` → RightAlt but other fields kept; extra unknown fields ignored; Save creates the directory; Save is atomic (a pre-existing file is replaced, no `.tmp` left).
- [ ] **Steps 2–4:** `ci.sh` FAIL → implement → `ci.sh` PASS.
- [ ] **Step 5: Commit** (`feat(windows): settings`).

---

### Task 6: Theme, pill window and placement (App)

**Files:**
- Create: `windows/src/Akoya.Core/Ui/PillPlacement.cs`; `windows/src/Akoya.App/Theme/Theme.xaml`, `Fonts/*`, `Controls/DotGrid.cs`, `Controls/RimGlowBorder.cs`, `Pill/PillWindow.xaml(.cs)`, `Pill/PillViewModel.cs`, `Pill/PillPlacer.cs`
- Test: `windows/tests/Akoya.Core.Tests/PillPlacementTests.cs`, `windows/tests/Akoya.App.Tests/Snapshots/PillSnapshots.cs`

**Interfaces:**
- Consumes: `PillDots` (Task 1), `DictationPhase` (Task 3).
- Produces: `PillPlacement.Compute(RectPx workArea, double pillWidthDip, double pillHeightDip, double dpiScale) -> PointPx` (top-left in physical px; bottom margin 18 DIP; clamped inside the work area). `record struct RectPx(int X, int Y, int Width, int Height)`, `record struct PointPx(int X, int Y)`.
- Produces: `DotGrid` (FrameworkElement; `Columns`, `Rows`, `DotSize`, `Gap`, `Opacities` (double[]), `DotBrush`) drawing in `OnRender`. `RimGlowBorder` (Decorator; `GlowColor`, `GlowStrength` 0..1, `CornerRadius`) reproducing the mockup's inset shadows with layered gradient borders.
- Produces: `PillViewModel` (`Mode`: Listening/Transcribing/Pasted/Copied/Alert; `Opacities`, `TimerText` "0:04", `AlertText`, `IsRed`; `Apply(DictationPhase phase)` maps: MicMissing → red "Microphone not detected", MicBlocked → red "Turn on your mic in Settings", ModelFailed → red "Speech model failed", PasteFailed → red "Couldn't paste", ModelNotReady → blue "Getting the speech model ready…", Copied → blue "Copied. Press Ctrl+V"), `PillWindow.ShowFor(PillViewModel vm)` / `Hide()` with the 0.28 s fade/rise, extended styles `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT`, `ShowActivated=false`, `Topmost`. `PillPlacer.PlaceOnForegroundMonitor(Window pill)`.
- Theme brushes/keys used by later tasks: `Akoya.Surface`, `Akoya.Accent`, `Akoya.AccentDark`, `Akoya.AccentLight`, `Akoya.Amber`, `Akoya.Red`, `Akoya.TextPrimary/Body/Muted/Eyebrow`, `Akoya.FontFamily` (Outfit embedded as a resource).

- [ ] **Step 1: Placement tests:** 1920×1040 work area at (0,0), scale 1 → pill bottom at y = 1040 − 18; 150 % monitor at (1920, 0) 2560×1400 → centred on that monitor, margin 27 px; taskbar on top (work area y = 48) → still bottom of work area; monitor at negative x (−1920); pill wider than area → clamped.
- [ ] **Step 2: Snapshot tests** render `pill-listening` (steps `[1,2,3,5,7,5,3,2,3,5,7,8,7,5,3,2,3,5,7,5,3,2,1]`, timer 0:03), `pill-transcribing` (elapsed 0.3 s, timer 0:04), `pill-pasted`, `pill-copied` ("Copied. Press Ctrl+V"), `pill-alert-mic` ("Microphone not detected", red), each on a 420×120 dark background matching the mockup desk gradient.
- [ ] **Step 3: Run** `ci.sh` — FAIL. **Step 4: Implement.** **Step 5: Run** — PASS.
- [ ] **Step 6: Visual check:** screenshot `docs/design/windows-pill-mockup.html#listening`, `#transcribing`, `#pasted` and the red pill in `windows-onboarding-mockup.html` with Playwright; compare side by side with the PNGs from `ci.sh`; fix differences in size, glow, dot spacing, font until they match.
- [ ] **Step 7: Commit** (`feat(windows): theme and pill`).

---

### Task 7: Speech model and transcriber (App)

**Files:**
- Create: `windows/src/Akoya.Core/Model/ModelManifest.cs`; `windows/src/Akoya.App/Speech/ModelStore.cs`, `Speech/SherpaTranscriber.cs`
- Modify: `.github/workflows/windows.yml` (model cache + download step, `AKOYA_MODEL_DIR`)
- Test: `windows/tests/Akoya.App.Tests/SherpaTranscriberTests.cs`, `ModelStoreTests.cs`

**Interfaces:**
- Produces: `ModelManifest.Url`, `.TarballBytes`, `.TarballSha256`, `.FolderName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8"`, `.Files` (encoder/decoder/joiner/tokens names).
- Produces: `ModelStore(string modelsRoot, HttpClient http)` with `bool IsInstalled`, `string ModelDirectory`, `Task EnsureAsync(IProgress<double> progress, CancellationToken ct)` (download `.part` → verify size + SHA-256 → extract with SharpCompress to `<root>/.extract-<guid>` → `Directory.Move` into place; on any failure delete partials and throw `ModelDownloadException`).
- Produces: `SherpaTranscriber : ITranscriber, IDisposable` — `SherpaTranscriber(string modelDir, InferenceProvider provider, int threads)`; uses sherpa-onnx `OfflineRecognizer` with `ModelConfig.Transducer.{Encoder,Decoder,Joiner}`, `Tokens`, `ModelType = "nemo_transducer"`, `Provider = "cpu"`, `NumThreads = threads`; `WarmUp()` transcribes 0.5 s of silence. Decoding runs on a background thread; one call at a time (SemaphoreSlim).

- [ ] **Step 1: Pin the manifest.** Add a CI step (cache miss only) that downloads the tarball with `curl -L`, prints `::notice::size=<bytes> sha256=<hash>`, and extracts it with `tar -xjf` into `$RUNNER_TEMP/model` (cached with `actions/cache`, key `parakeet-tdt-0.6b-v2-int8`); export `AKOYA_MODEL_DIR`. Run `ci.sh`, read the notice with `gh api .../check-runs/<id>/annotations`, and write the two values into `ModelManifest`.
- [ ] **Step 2: Tests:** `Transcribe_TestWav_ContainsExpectedWords` (load `test_wavs/0.wav`, expect the transcript, case-insensitive, to contain "don't wish to see it" and "old portrait"; log elapsed seconds); `Transcribe_120Seconds_Completes` (0.wav tiled to 120 s, completes in < 60 s); `Transcribe_Silence_ReturnsEmptyOrShort`; `ModelStore_HashMismatch_ThrowsAndCleansUp` (local `HttpMessageHandler` serving 1 KB of zeros); `ModelStore_Success_MovesFolderAtomically` (tiny tar.bz2 fixture made in the test with SharpCompress). Tests needing the model are skipped when `AKOYA_MODEL_DIR` is unset.
- [ ] **Steps 3–5:** `ci.sh` FAIL → implement → `ci.sh` PASS. Record the 0.wav timing in the commit message.
- [ ] **Step 6: Commit** (`feat(windows): model download and sherpa-onnx transcriber`).

---

### Task 8: Audio capture and the microphone catalog (App)

**Files:**
- Create: `windows/src/Akoya.Core/Audio/Resampler.cs`; `windows/src/Akoya.App/Platform/WasapiRecorder.cs`, `MicrophoneCatalog.cs`, `MultiMeter.cs`, `MicPrivacy.cs`
- Test: `windows/tests/Akoya.Core.Tests/ResamplerTests.cs`, `windows/tests/Akoya.App.Tests/MicrophoneCatalogTests.cs`

**Interfaces:**
- Consumes: `IAudioRecorder` (Task 3), `RmsWindower` (Task 1), `EndpointFacts`/`MicrophoneNaming` (Task 4).
- Produces: `Resampler.ToMono16k(ReadOnlySpan<float> interleaved, int channels, int sampleRate) -> float[]` (average channels, linear interpolation; windowed-sinc not required).
- Produces: `WasapiRecorder : IAudioRecorder` (NAudio `WasapiCapture` shared mode on the given endpoint id, or the default when null; throws `MicrophoneMissingException` when the id isn't active; raises `Interrupted` on `RecordingStopped` with an exception or on device removal).
- Produces: `MicrophoneCatalog` with `IReadOnlyList<EndpointFacts> Snapshot()` (active capture endpoints: friendly name, `PKEY_Device_DeviceDesc`, `PKEY_AudioEndpoint_FormFactor`, enumerator from the device instance id, physical-parent check via `PKEY_Device_InstanceId` prefix, default via `GetDefaultAudioEndpoint(Capture, Communications|Console)` → Console) and `event Action? Changed` (IMMNotificationClient).
- Produces: `MultiMeter : IDisposable` — `Start(IEnumerable<string> ids)`, `event Action<string, int>? Step` (endpoint id, level step at ~25 Hz), `Stop()`.
- Produces: `MicPrivacy.IsDesktopAccessAllowed() -> bool` (registry `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone` `Value` and its `NonPackaged` subkey ≠ "Deny"), `MicPrivacy.OpenSettings()` (`ms-settings:privacy-microphone`).

- [ ] **Step 1: Tests:** resampler 48 kHz stereo 1 kHz sine → 16 kHz length ±1 and RMS within 2 %; 44.1 kHz mono → correct length; `Catalog_Snapshot_DoesNotThrow` on the runner (may be empty).
- [ ] **Steps 2–4:** `ci.sh` FAIL → implement → `ci.sh` PASS.
- [ ] **Step 5: Commit** (`feat(windows): WASAPI capture and microphone catalog`).

---

### Task 9: Hotkey hook and pasting (App)

**Files:**
- Create: `windows/src/Akoya.App/Platform/KeyboardHook.cs`, `ClipboardPasteInserter.cs`, `ElevationCheck.cs`
- Test: `windows/tests/Akoya.App.Tests/ClipboardTests.cs`

**Interfaces:**
- Consumes: `HotkeyDecoder` (Task 2), `ITextInserter`/`InsertResult` (Task 3).
- Produces: `KeyboardHook : IDisposable` — `KeyboardHook(HotkeyDecoder decoder, Action<HotkeyEvent> onEvent)` installs `WH_KEYBOARD_LL` on its own thread with a message loop; returns 1 for swallowed keys; on `InjectMaskKey` sends `VK 0xE8` down/up via `SendInput`; events are posted to the UI thread via the captured `SynchronizationContext`; the callback only decodes and posts (Windows drops hooks that exceed `LowLevelHooksTimeout`); `Install()` throws `HotkeyUnavailableException` if `SetWindowsHookEx` fails; `Reinstall()` is called by `AppHost` on session unlock.
- Produces: `ClipboardPasteInserter(IClipboard clipboard, Func<bool> foregroundIsElevated, Action<ushort[]> sendCtrlV, IScheduler scheduler) : ITextInserter` with `IClipboard { IDataObject? Snapshot(); bool TrySetText(string text, bool excludeFromHistory); void Restore(IDataObject? data); }`. Elevated foreground → set text, no paste, no restore → `CopiedOnly`. `TrySetText` false after 10 × 20 ms retries → `Failed`, clipboard untouched. Else Ctrl+V, restore after 300 ms → `Pasted`.
- Produces: `ElevationCheck.ForegroundIsElevated() -> bool` (foreground window → process → `OpenProcessToken`/`TokenElevation`; access denied counts as elevated).
- Exclusion formats set alongside the text: `ExcludeClipboardContentFromMonitorProcessing` (empty), `CanIncludeInClipboardHistory` (DWORD 0), `CanUploadToCloudClipboard` (DWORD 0).

- [ ] **Step 1: Tests** (fake `IClipboard`): `Insert_PastesAndRestores`; `Insert_Elevated_CopiesOnly_NoRestore`; `Insert_ClipboardBusy_RetriesThenReportsFailure` (10 attempts counted, result `Failed`); real-clipboard STA test: `RealClipboard_SnapshotRestore_RoundTripsTextAndHtml`.
- [ ] **Steps 2–4:** `ci.sh` FAIL → implement → `ci.sh` PASS.
- [ ] **Step 5: Commit** (`feat(windows): keyboard hook and clipboard paste`).

---

### Task 10: Tray icon and panel (App)

**Files:**
- Create: `windows/src/Akoya.App/Assets/akoya.ico` (generated by a small script: dot logo 2-4-6-4-2, white, 16/20/24/32 px), `Tray/TrayIconHost.cs`, `Tray/TrayPanelWindow.xaml(.cs)`, `Tray/TrayPanelViewModel.cs`, `Controls/NeutralSwitch.xaml`
- Test: `windows/tests/Akoya.App.Tests/Snapshots/TrayPanelSnapshots.cs`

**Interfaces:**
- Consumes: Theme + `DotGrid` + `RimGlowBorder` (Task 6), `NamedMic`/`MicChoice` (Task 4), `AkoyaSettings` (Task 5), `DictationPhase` (Task 3).
- Produces: `TrayPanelViewModel` (`StatusText`, `StatusKind` Ready/Busy/Warning/Error, `ProgressPercent?`, `HotkeyLabel` "Right Alt", `HotkeyOptions`, `LastDictation?`, `MicLabel`, `MicMissing`, `MicOptions` (first "System default (<name>)"), `LaunchAtStartup`, `UpdateReady`, commands `Copy`, `SelectHotkey`, `SelectMic`, `Retry`, `OpenMicSettings`, `RestartToUpdate`, `Quit`).
- Produces: `TrayIconHost(TrayPanelViewModel vm)` (H.NotifyIcon `TaskbarIcon`, tooltip "Akoya", left/right click → `TrayPanelWindow.ShowNearTray()`), panel 300 DIP wide, closes on `Deactivated` and Esc.

- [ ] **Step 1: Snapshot tests:** `tray-ready`, `tray-listening`, `tray-last-dictation` ("Ship the release notes after lunch, then ping the team."), `tray-mic-missing` (yellow row, "Not detected"), `tray-update-ready`, `tray-hotkey-menu-open`, `tray-downloading` ("Getting the speech model ready… 62%").
- [ ] **Steps 2–4:** `ci.sh` FAIL → implement → `ci.sh` PASS.
- [ ] **Step 5: Visual check** against `docs/design/all-in-one-dropdown-target.png` (minus the Menu bar icon row) and the yellow row in `windows-onboarding-mockup.html`; iterate until they match.
- [ ] **Step 6: Commit** (`feat(windows): tray icon and panel`).

---

### Task 11: Setup window (App)

**Files:**
- Create: `windows/src/Akoya.App/Setup/SetupWindow.xaml(.cs)`, `SetupViewModel.cs`, `WelcomeView.xaml`, `MicrophoneView.xaml`, `TryItView.xaml`
- Test: `windows/tests/Akoya.App.Tests/Snapshots/SetupSnapshots.cs`, `windows/tests/Akoya.Core.Tests/MicStatusTests.cs` (+ `windows/src/Akoya.Core/Mics/MicStatus.cs`)

**Interfaces:**
- Consumes: `NamedMic` list (Task 4), `MultiMeter.Step` (Task 8), `MicPrivacy` (Task 8), `MeterLevel` + `GlowEnvelope` (Task 1), `DictationCoordinator` with `Destination.Practice` (Task 3), `ModelStore` progress (Task 7).
- Produces: `MicStatus.Evaluate(string highlightedId, IReadOnlyDictionary<string, (int step, DateTimeOffset at)> recent, DateTimeOffset now) -> MicStatusKind { None, HearingYou, NotHearing }` — HearingYou when the highlighted mic's step ≥ 3 in the last 0.5 s; NotHearing when the highlighted mic stayed ≤ 2 for 3 s while another mic reached ≥ 3; else None.
- Produces: `SetupViewModel` (`Step` Welcome/Microphone/TryIt, `Mics`, `MoreMics`, `MoreExpanded`, `HighlightedId`, `Status`, `IsBlocked`, `ModelProgress`, `PracticeText`, commands `GetStarted`, `SelectMic(id)`, `ToggleMore`, `OpenSettings`, `Continue`, `Done`, `Skip`) and `event Action<string>? MicChosen` / `Finished`.

- [ ] **Step 1: Tests:** `MicStatus` (none before speech; hearing; not hearing after 3 s; not hearing never fires when no other mic hears speech). Snapshots: `setup-welcome`, `setup-mic-list` (laptop list, laptop highlighted, Hearing you), `setup-mic-amber` (Galaxy Buds highlighted, amber "Galaxy Buds isn't hearing you"), `setup-mic-one` ("Is this the mic you want?"), `setup-mic-more` (HyperX, Webcam mic, "More (2)" last), `setup-mic-blocked`, `setup-tryit` ("Okay, this is actually really smooth."), `setup-tryit-downloading`.
- [ ] **Steps 2–4:** `ci.sh` FAIL → implement → `ci.sh` PASS.
- [ ] **Step 5: Visual check** against `docs/design/windows-onboarding-mockup.html` (render it with `node` + Playwright as in the brainstorm) — every card; selected-row highlight must reach the list's rounded edges.
- [ ] **Step 6: Commit** (`feat(windows): setup window`).

---

### Task 12: Composition, single instance and startup (App)

**Files:**
- Create: `windows/src/Akoya.App/AppHost.cs`, `Program.cs`, `Platform/SingleInstance.cs`, `Platform/StartupRegistration.cs`, `Platform/DispatcherScheduler.cs`
- Modify: `App.xaml.cs`
- Test: `windows/tests/Akoya.App.Tests/AppHostTests.cs`, `SingleInstanceTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `AppHost` builds the object graph (settings → catalog → resolver → recorder → transcriber (after `ModelStore.EnsureAsync`) → coordinator → hook → pill/tray/setup), maps `DictationPhase` to `PillViewModel` and `TrayPanelViewModel`, places the pill with `PillPlacer` on Recording, listens to `SystemEvents.SessionSwitch` → `SessionLocked()` + decoder `Reset()`, shows setup when `!SetupDone || !ModelStore.IsInstalled`, persists settings on every change, logs to `%LocalAppData%\Akoya\logs` (no transcripts).
- Produces: `SingleInstance.TryAcquire(out SingleInstance? handle)`; second instance calls `SingleInstance.SignalFirst()` (named `EventWaitHandle` `Local\Akoya-<SID>-show`) and exits; first opens the tray panel.
- Produces: `StartupRegistration.Set(bool enabled, string exePath)` / `IsEnabled()` (HKCU Run key `Akoya`; path = Velopack root stub when installed, else current exe).
- Produces: `DispatcherScheduler : IScheduler`.

- [ ] **Step 1: Tests:** `AppHost` with fakes for recorder/transcriber/inserter/hook: Pressed → pill visible in listening; Released after 0.5 s → transcribing → pasted → hidden; mic missing → red alert and tray row yellow; setup shown on first run. `SingleInstance`: second acquire fails and signal reaches the first. `StartupRegistration` round trip under a test registry key.
- [ ] **Steps 2–4:** `ci.sh` FAIL → implement → `ci.sh` PASS.
- [ ] **Step 5: Commit** (`feat(windows): app composition`).

---

### Task 13: Updates, packaging and docs

**Files:**
- Create: `windows/src/Akoya.App/Platform/Updater.cs`, `.github/workflows/release-windows.yml`, `docs/MANUAL_CHECKS_WINDOWS.md`, `windows/README.md`
- Modify: `Program.cs` (`VelopackApp.Build().Run()` first line), `README.md` (Windows section: install steps, SmartScreen note; keep "in the works" until Rohit publishes), `CLAUDE.md` (Windows: CI loop, `ci.sh`)

**Interfaces:**
- Produces: `Updater(string repoUrl)` with `Task<bool> CheckAndDownloadAsync()` (Velopack `UpdateManager(new GithubSource(repoUrl, null, false))`; no-op when not installed), `bool UpdateReady`, `void ApplyAndRestart()`. Checked at launch and every 6 h by `AppHost`.
- Produces: `release-windows.yml` on tag `windows-v*`: publish `Akoya.App` self-contained win-x64, `vpk pack --packId Akoya --packVersion <tag without prefix> --mainExe Akoya.exe --icon akoya.ico`, `vpk upload github --repoUrl … --tag <tag> --releaseName "Akoya for Windows <ver>" --publish false` (draft). The `vpk` tool version must equal the Velopack package version.

- [ ] **Step 1: Test:** `Updater_NotInstalled_IsNoOp`. Add a `workflow_dispatch` dry-run job in `release-windows.yml` that packs without uploading and writes `Setup.exe` size to `ci-output`.
- [ ] **Step 2:** The dry-run job also runs on pushes that touch `release-windows.yml` (never push a `windows-v*` tag; publishing is Rohit's step). Run `ci.sh`; expected: dry run produced `Setup.exe`, size noted in `ci-output`.
- [ ] **Step 3:** Write `MANUAL_CHECKS_WINDOWS.md` from the spec's manual checklist, one checkbox per check with the expected result, plus: after the first Windows release, the update check still finds it when a newer Mac-only release exists.
- [ ] **Step 4: Commit** (`feat(windows): updates, packaging and docs`).

---

### After the tasks (controller, not subagents)

1. Whole-branch code review (fresh reviewer, superpowers:requesting-code-review), fixes through `ci.sh`.
2. Visual pass: every PNG in the last run's `snapshots/` next to its mockup; any mismatch becomes a fix task.
3. Hands-on on Rohit's PC with screen control (his approval first): install the .NET 10 SDK with `winget install Microsoft.DotNet.SDK.10`, clone the branch into `Documents\Akoya`, `dotnet run --project windows/src/Akoya.App`, run `docs/MANUAL_CHECKS_WINDOWS.md`, then the GPU spike (DirectML/Windows ML/CUDA, CPU vs GPU timing), whose result becomes its own small plan.
