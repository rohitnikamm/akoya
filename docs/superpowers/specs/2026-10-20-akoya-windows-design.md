# Akoya for Windows: Design

Status: **final, approved by Rohit to go to planning and building** · 2026-10-20.
Visual targets (all in `docs/design/`):
- Pill: `windows-pill-mockup.html` (loops through the states) and `docs/assets/windows-dictation.png` (the app's own renders, animated).
- Setup: `windows-onboarding-mockup.html` (Welcome, Microphone in every state, Try it, the red
  "Microphone not detected" pill and the yellow tray row).
- Tray panel: the Mac all-in-one dropdown, `all-in-one-dropdown-target.html` / `.png`, with its
  "Menu bar icon" row as the "Pill" row of `windows-pill-height-and-options-mockup.html` (see
  `windows-tray-panel-placeholder.png` for the Mac render).
- Pill height, pop-in, Solid and red waveforms: `windows-pill-height-and-options-mockup.html`.

## Intent

Rohit uses Akoya on his Mac every day, mainly for development (dictating into terminals, Claude
Code, editors). He wants the same tool on his Windows 11 PC, installable from the Akoya GitHub
releases. It should work exactly like the Mac version. Only the way it shows itself changes,
because Windows has no menu bar. Guiding rule for every screen: simple and seamless, nothing to
think hard about.

## Decisions (all confirmed by Rohit, 2026-10-20)

- **Same behaviour as the Mac.** Hold the hotkey to talk, release to transcribe and paste into the
  focused app. Quick tap = hands-free (tap again to stop). Esc cancels. On-device transcription
  (Parakeet TDT 0.6B v2, English), nothing leaves the PC.
- **No waveform in the taskbar.** A **floating pill** at the bottom centre of the screen, just
  above the taskbar, shown only while dictating, on **the monitor that holds the window you're
  typing into** (falls back to the primary monitor).
- **Compact pill:** dot waveform **4 dots tall** (outer rows light only on louder sound) plus a
  timer. States: listening (live waveform + timer) → transcribing (a blue scan sweeps across the
  centre two dot rows, timer frozen) → "Pasted" with a blue check → fades out. Exact look: the pill mockup.
- **Look:** Akoya's dark glass with the bottom-weighted electric-blue rim glow (`#2F6BFF` →
  `#1D4ED8`), Outfit font, as on the Mac.
- **Settings in the tray.** The dot logo sits in the tray overflow (behind ⌃); the user can drag
  it onto the taskbar. Clicking it opens the Mac's all-in-one panel, its Menu bar icon row
  becoming a Pill row (the pill is Windows' waveform).
- **Default hotkey: Right Alt**, swallowed so apps never see a lone Alt tap. Alternatives: Right
  Ctrl and the Copilot key. Left Alt is untouched.
- **Install and updates: Velopack.** Unsigned per-user `Setup.exe` on the GitHub release (one
  SmartScreen "More info → Run anyway"), Start menu entry, uninstaller, one-click updates from the
  same GitHub releases.
- **Setup: Welcome → Microphone → Try it** (three Windows-user reviewers all voted for it).
  Details under Setup below.
- **Mic not detected:** nothing is recorded (no silent fallback); the pill turns red with
  "Microphone not detected", and the tray Microphone row is highlighted yellow until fixed.
- **Stack: C# on .NET 10 (LTS) with WPF.** (.NET 8 and 9 reach end of support on 10 Nov 2026.)
- **Transcription: CPU always works; the NVIDIA GPU is an optional speed-up**, never requiring
  users to install CUDA or anything else.
- **Code in this repo** under `windows/`, sharing the README and releases.
- **Built in Claude's cloud workspace, verified on Windows by GitHub Actions** (`windows-latest`
  runners build, test, and render every screen to PNG on each push), then a hands-on test on
  Rohit's PC through screen control. Rohit's PC has no Visual Studio; the `dotnet` CLI is enough.

## Architecture

```
windows/
  Akoya.Windows.slnx
  Directory.Build.props        shared settings (net10.0, nullable, warnings as errors)
  src/Akoya.Core/              net10.0, no Windows APIs: logic only
  src/Akoya.App/               net10.0-windows, WPF + Win32 adapters
  tests/Akoya.Core.Tests/      xUnit, runs on any OS
  tests/Akoya.App.Tests/       xUnit on Windows: PNG renders of every view, engine smoke test
```

**Akoya.Core** (pure, fully unit-tested), ports of the Mac's `AkoyaCore` logic:
- `DictationCoordinator`: the state machine (`Idle`, `Recording`, `Transcribing`, `Pasted`,
  `Error`, `MicMissing`). Hold ≥ 0.30 s = push-to-talk; shorter = latched hands-free, next press
  stops. Esc cancels. 300 s max recording auto-stops. Silence gate: if the loudest level step never
  exceeds 2 (≈ −44 to −39 dBFS; same `LevelMeter` steps as the Mac) nothing is transcribed and the
  pill just fades. Practice destination for setup's Try it (text goes to the card, with a live
  preview every 100 ms like the Mac's `LivePreviewScheduler`; never two transcriptions at once).
  Trailing space after pasted text, as on the Mac.
- Ports (interfaces) mirroring the Mac's `Ports.swift`: `IAudioRecorder` (start/stop/snapshot/
  cancel, level events ~25 Hz, interruption event), `ITranscriber`, `ITextInserter`, plus
  `IMicrophoneCatalog`, `IClock`/timers for testability.
- `HotkeyDecoder`: turns raw key events into Pressed/Released/Cancel for the chosen key, including
  the AltGr fake-LCtrl rule and the Copilot key sequence (below).
- `LevelMeter` (RMS → step), `RmsWindower`, `WaveformLayout` (dot grid for pill and meters),
  `GlowEnvelope` (attack/release smoothing), ported with their Mac tests.
- `MicrophoneNaming`: plain names and the main/More split from endpoint facts (below).
- `MicrophoneResolver`: saved device ID (or null = System default) → device or "missing".
- `AkoyaSettings`: JSON model + atomic save/load.
- `ModelManifest`: model files, sizes and SHA-256 hashes, download URL.

**Akoya.App** (WPF), thin adapters plus views:
- `KeyboardHook`: `WH_KEYBOARD_LL` on a dedicated thread with a message loop; forwards events to
  `HotkeyDecoder`; swallows the hotkey (and Esc only while recording).
- `WasapiRecorder`: NAudio WASAPI shared-mode capture, resampled to 16 kHz mono float.
- `MicrophoneCatalog`: enumerates active capture endpoints (NAudio `MMDeviceEnumerator`), reads
  form factor, bus enumerator, friendly names; watches add/remove/default-change notifications.
- `MultiMeter`: opens every active input at once (setup only) for live per-row meters.
- `ClipboardPasteInserter`: snapshot clipboard → set text → `SendInput` Ctrl+V → wait 300 ms →
  restore.
- `SherpaTranscriber`: sherpa-onnx offline transducer (`model_type = nemo_transducer`) with the
  int8 Parakeet TDT 0.6B v2 model, CPU provider; provider is a setting so the GPU can be added
  (see GPU).
- `ModelStore`: download, verify, extract (see Model).
- `PillWindow`, `TrayIcon` + `TrayPanelWindow`, `SetupWindow` (Welcome, Microphone, Try it).
- `SingleInstance`, `StartupRegistration`, `Updater` (Velopack), `App` composition root.

Data on disk:
- Settings: `%AppData%\Akoya\settings.json` (hotkey, microphoneId, launchAtStartup, setupDone,
  provider, look `Pixel`/`Solid`, redTint; a missing or bad field loads as its default).
- Model: `%LocalAppData%\Akoya\models\` (outside Velopack's `current\` folder, so updates don't
  re-download it; uninstall removes it).
- Log: `%LocalAppData%\Akoya\logs\akoya.log` (rolling, 1 MB × 3), never contains transcripts.

## Dictation flow

1. **Hotkey down.** `KeyboardHook` → `HotkeyDecoder` → `Pressed`. If the model isn't ready, the
   pill shows "Getting the speech model ready…" in the same style and nothing records. If the
   chosen mic is missing → `MicMissing` (red pill, see Errors). Otherwise `WasapiRecorder.Start`,
   pill appears in **listening** on the target monitor, timer starts, waveform follows the level.
   A pill appearing from hidden (listening, or a message) pops in: the 0.42 s "jelly" of
   `docs/design/windows-pill-height-and-options-mockup.html` (a 34 DIP circle at 55 % scale and
   transparent grows to full size by 24 %, stretches to 107 % width and 92 % height by 64 %, springs
   to 98.5 % and 103 % by 84 %, settles at 100 %; contents fade in from 45 %). It only changes
   what is drawn: recording, levels and the timer start on the press. With Windows' animation
   effects off it is a plain 0.28 s fade. Fade-out (fade and sink 0.28 s) and width changes between
   states are unchanged.
2. **Hotkey up** after ≥ 0.30 s → stop. Up sooner → keep recording (hands-free); the next down
   stops. **Esc** while recording → cancel, pill fades, nothing pasted.
3. **Stop.** Silence gate; if silent → pill fades, nothing pasted. Else pill → **transcribing**
   (scan sweep, timer frozen) and `SherpaTranscriber` runs on a background thread.
4. **Paste.** Empty text → pill fades. Else `ClipboardPasteInserter.Insert(text + " ")`, pill shows
   **Pasted** ✓ for 0.9 s, then fades. The text becomes the tray panel's Last dictation.
5. **Max 300 s** → stops as if released.

Target monitor: `MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTOPRIMARY)`, pill centred
horizontally on that monitor's work area, bottom edge 52 DIP (DPI-scaled) above the top of that
monitor's taskbar measured as if the taskbar were showing, so it sits at the same height whether
the taskbar is shown or auto-hidden (with the default 48 DIP taskbar: 100 DIP above the screen's
bottom). The taskbar is the `Shell_TrayWnd` or `Shell_SecondaryTrayWnd` window on that monitor
(`MonitorFromWindow`), its thickness the height of its rectangle (the same shown or hidden), and
auto-hide comes from `SHAppBarMessage(ABM_GETSTATE)`. Taskbar at the bottom: shown, the gap is
measured from the work area's bottom; auto-hidden, from the monitor's bottom minus the thickness.
Taskbar on another edge, or none on that monitor: from the work area's bottom. Always clamped
inside the work area. Per-monitor-v2 DPI aware.

Pill window: borderless, transparent, topmost, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW |
WS_EX_TRANSPARENT` (never takes focus, click-through, no taskbar button, not in Alt+Tab).

## Hotkey details

- Right Alt = `VK_RMENU` (with the extended flag). Right Ctrl = `VK_RCONTROL`. The hook swallows
  key-down and key-up of the chosen key so no app sees it.
- **AltGr layouts:** Windows injects a fake `VK_LCONTROL` down (scan code 0x21D) right before
  Right Alt and its up right after. When Right Alt is the hotkey, those fake LCtrl events are
  swallowed too, so apps never see a stray Ctrl.
- **Copilot key:** arrives as LWin + LShift + F23. The decoder treats F23 (with LWin + LShift
  held) as the key, swallows F23 and injects a mask key so Windows doesn't open Start on Win-up;
  LWin and LShift pass through.
- Esc is swallowed only while recording.
- Key repeat (auto-repeat key-downs while held) is ignored.
- If the hook is removed by Windows (hook timeout), it is re-installed; hook callbacks do no work
  beyond queueing an event. Re-installed after unlock and after sleep; a watchdog checks every 30 s
  for a hook Windows dropped silently (input per `GetLastInputInfo` more than 60 s after the hook's
  last key) and re-installs it, never during a recording. The hook thread checks the silence again
  just before, so a key that arrives meanwhile is never lost; the log notes it once per silence.

## Pasting

- Clipboard snapshot is a raw Win32 copy (`EnumClipboardFormats`, each HGLOBAL format byte for
  byte, `CF_ENHMETAFILE` as its bits), so every format survives: registered private formats
  (Photoshop, Figma, CAD), Excel's cell formats, Explorer's `Preferred DropEffect` (a cut stays a
  cut), PNG/DIBV5 images and password managers' `CanIncludeInClipboardHistory` /
  `ExcludeClipboardContentFromMonitorProcessing` markers. Formats Windows synthesises from others
  (`CF_BITMAP`, `CF_PALETTE`, `CF_METAFILEPICT`) or that only work inside their owner
  (`CF_OWNERDISPLAY`, `CF_DSP*`, `CF_PRIVATEFIRST`..`CF_GDIOBJLAST`) are skipped. Of the DIB family
  (`CF_DIB`, `CF_DIBV5`) and the text family (`CF_TEXT`, `CF_OEMTEXT`, `CF_UNICODETEXT`) only the
  first one listed is copied: the rest are Windows' conversions of it, made again after the
  restore, so a 4K screenshot counts once against the cap. Restore after 300 ms, as on the Mac, also
  marked `ExcludeClipboardContentFromMonitorProcessing` so it isn't a new Win+V entry; if another
  app holds the clipboard then, the restore is tried again every 20 ms for about 1 s without
  blocking the UI thread. If any other format can't be copied, or the clipboard holds more than
  64 MB, Akoya doesn't borrow the clipboard: it types the text with `SendInput`
  `KEYEVENTF_UNICODE` (held modifiers released first, as for Ctrl+V; a line break as Shift+Enter,
  which is a new line where plain Enter would send a chat message or run a terminal line; a tab as
  a space, since the Tab key moves focus), and the log names the format that stopped it. If
  writing Akoya's text fails after the clipboard was emptied, the snapshot is put back at once.
- Akoya's temporary clipboard content is marked with `ExcludeClipboardContentFromMonitorProcessing`
  and `CanIncludeInClipboardHistory = 0`, so Windows clipboard history (Win+V) and cloud clipboard
  skip it.
- `SendInput` Ctrl+V. If any modifier is still physically held, it's released in the injected
  sequence first and restored state isn't faked.
- **Admin windows (UIPI):** if the foreground window belongs to an elevated process and Akoya isn't
  elevated, Akoya doesn't attempt the paste. It leaves the text on the clipboard (no restore) and
  the pill says "Copied. Press Ctrl+V" (blue, like Pasted), then fades.

## Tray icon and panel

- Tray icon: the dot logo (columns 2-4-6-4-2) as a multi-size `.ico` (16, 20, 24, 32 px; white
  dots). Tooltip "Akoya". Left- or right-click opens the panel.
- Panel: a borderless rounded window anchored above the tray corner of the work area on the
  taskbar's monitor, closing when it loses focus or on Esc. Esc, a click in its shadow, a second
  click on the tray icon and the dictation hotkey give focus back to the window the user was
  typing in (activated before the panel hides, while Windows still lets Akoya do it); a click on
  another window leaves focus there. Contents, top to bottom, as the Mac
  dropdown:
  1. **Status card:** "Akoya", status dot + text (Ready / Listening / Transcribing / Getting the
     speech model ready… with % / Model download failed + Retry / Microphone not detected), the
     dot logo, "Hold [Right Alt ▾] to talk" where the keycap opens the hotkey menu (Right Alt,
     Right Ctrl, Copilot key, ✓ on current), and "or tap it once to start, again to stop".
  2. **Last dictation** card with Copy (hidden until the first dictation).
  3. **Options:** Microphone (value = current plain name ▾; menu: "System default (<name>)", then
     every input by plain name, ✓ on selected; highlighted yellow with "Not detected" when the
     saved one is missing), **Pill** (the Mac's Menu bar icon disclosure row: waveform glyph,
     value "Pixel"/"Solid" and a › that turns when open, collapsed whenever the panel opens; inside,
     "Waveform" Pixel | Solid as a neutral segmented control and "Red while recording" as a neutral
     switch, off by default; no note line, since Hide when idle is left out), Launch at startup
     (switch). When an update is ready: an "Update ready · Restart" row with a blue button.
     Pill options apply at once, also to a pill on screen. Solid: per column one 3 DIP bar with
     round ends, centred on the dot rows, one dot tall at the quietest step rising linearly to the
     four rows' 18 DIP at the loudest (the transcribing sweep: bars the centre two rows tall, 8 DIP). Red while
     recording: the listening waveform is `#FF6B6B` and the transcribing sweep's lit columns
     (head and tail) are amber `#FBBF24`; the rim glow stays blue. Setup's Try it is unchanged.
  4. **Quit Akoya** (quiet text row).
- Neutral control styles as on the Mac (no solid-blue selected states).

## Setup (first run)

Window: the Mac's onboarding window (blue rim glow, 44 px corner radius, ✕, page dots, Skip),
centred on the primary monitor. Exact look: `windows-onboarding-mockup.html`.

1. **Welcome:** dot logo, "Akoya", "Talk instead of type, in any app. Everything stays on your
   PC.", Get started. The model download starts here in the background.
2. **Microphone:** always shown, one layout for every case. "Which mic do you talk into?" /
   "Say something. Pick the one that moves." List of inputs by plain name, each with a live
   10-dot meter; virtual and loopback inputs folded into a final "More (n)" row (click expands in
   place). The Windows default is highlighted; **Akoya never changes the highlight itself**; the
   user clicks a row. Status chip: green "Hearing you" when the highlighted mic hears speech,
   amber "<name> isn't hearing you" when it hears nothing for 3 s while another mic does, nothing
   before the user speaks. One mic → same screen, one row, title "Is this the mic you want?".
   Mic privacy switch off → "Turn on your mic" / "Windows is blocking it. Open Settings and
   switch on microphone access for desktop apps." with Open Settings
   (`ms-settings:privacy-microphone`) and "Waiting for the switch…"; moves on by itself once it's
   on. Continue saves the highlighted device's ID.
3. **Try it:** "Hold [Right Alt] and say anything", "or tap it once to start, again to stop", live
   waveform, the text card filling in live (practice destination, nothing is pasted), Done. While the
   model downloads, the waveform is one line of dots that fills left to right with the progress, and
   a line under it says "Getting the speech model ready 62%"; if the download fails it says "Model
   download failed · Retry" there, the dots stopped where it stopped. The text card keeps its
   placeholder (it is where the user's words go).

Skip or ✕ on any step ends setup (marked done); everything stays reachable from the tray panel.
Setup runs on first launch and when the model is missing.

### Microphone naming rules (pure function, unit-tested)

Input facts per endpoint: Windows friendly name, device description, form factor
(`PKEY_AudioEndpoint_FormFactor`), bus enumerator (from the device instance ID: `USB`, `BTHENUM`
/`BTHHFENUM`, `HDAUDIO`, `INTELAUDIO`, `SWD`, `ROOT`…), is-default.

- Built-in array or internal mic on `HDAUDIO`/`INTELAUDIO` (form factor Microphone, not Headset)
  → **"This PC's microphone"**.
- Bluetooth (`BTH*`) → the device description ("Galaxy Buds2 Pro"), stripping "Headset",
  "Hands-Free", "AG Audio" suffixes.
- Webcam (USB device whose description or friendly name contains camera/webcam/cam/BRIO, or is a
  USB video class composite) → **"Webcam mic"** (if several, "Webcam mic (<model>)").
- Other USB → the device description ("HyperX Cloud III", "Shure MV7").
- **More** group: loopback (form factor `LineLevel`/`DigitalAudioDisplayDevice`, or names like
  "Stereo Mix", "What U Hear", "Wave Out Mix") and virtual (`SWD`/`ROOT` enumerator without a
  physical parent, or known virtual vendors: Voicemeeter, VB-Audio, NVIDIA Broadcast, Steam,
  Discord, OBS, Krisp). Virtual inputs still show a live meter.
- Duplicate plain names get " 2", " 3".
- Fallback: Windows' friendly name with the "Microphone (…)" wrapper removed.

Saved setting is the endpoint ID; "System default" (tray panel only) saves null and follows
Windows.

## Model

- sherpa-onnx's pre-converted `sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8`: one tarball,
  `https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2`,
  containing `encoder.int8.onnx` (≈ 622 MB), `decoder.int8.onnx` (≈ 6.9 MB), `joiner.int8.onnx`
  (≈ 1.7 MB), `tokens.txt` and `test_wavs/0.wav` (expected text: "Well, I don't wish to see it
  any more, observed Phebe, turning away her eyes. It is certainly very like the old portrait.").
  The tarball's byte size and SHA-256 are pinned in `ModelManifest` (a CI job computes them once
  from the real file).
- Download to a `.part` file with progress, verify size and hash, extract (SharpCompress for
  bzip2 + tar) into a temp folder, then rename it into place (atomic). Interrupted downloads restart cleanly. Failure → status "Model download failed"
  with Retry (setup and tray).
- A model that is on disk but fails to load → "Speech model failed" with Retry, which loads the
  same files again (the failure may be out of memory or an antivirus scan). When the same files
  fail a second time in a row they are deleted, so the next Retry downloads them again.
- Loaded once at launch on a background thread; "Ready" only after a warm-up transcription of
  0.5 s of silence.

## GPU (optional speed-up)

- v1 ships CPU-only, which must be fast enough on its own (target: a 10 s dictation transcribes in
  under 1.5 s on a recent 6-core CPU; measured on CI and on Rohit's PC).
- A provider setting (`Auto` / `Cpu`) exists from day one. `Auto` uses the GPU when a working GPU
  provider is available and silently falls back to CPU on any load error.
- Which GPU route ships (sherpa-onnx with ONNX Runtime DirectML, Windows ML, or sherpa-onnx's CUDA
  build if it can be made install-free) is decided by a spike on Rohit's PC during the hands-on
  phase, then built as its own plan task. Users never install CUDA.

## Other behaviour

- **Quit:** a recording is dropped, never pasted. During a transcription and its paste Quit waits
  for them (and the clipboard restore) to finish, for at most 10 s.
- **Single instance:** named mutex `Local\Akoya-<user SID>`. A second launch signals the first
  (named event) to open the tray panel, then exits.
- **Launch at startup:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Akoya` = the Velopack
  stub (`%LocalAppData%\Akoya\Akoya.exe`), so it survives updates. Default off, as on the Mac.
- **Updates:** Velopack `UpdateManager` with `GithubSource` for this repo. Check at launch and
  every 6 h; download in the background; the panel shows "Update ready · Restart". Windows
  releases are tagged `windows-vX.Y.Z` and carry the Velopack assets (`Setup.exe`, `*.nupkg`,
  `releases.win.json`); the plan verifies the update check still finds them when a newer Mac-only
  release exists.
- **Releases:** a `release-windows` GitHub workflow builds `Setup.exe` with `vpk` on tag push
  `windows-v*` and attaches it to a draft release. Publishing stays Rohit's manual step.

## Errors

| Situation | Pill | Tray panel |
| --- | --- | --- |
| Saved mic missing / no mic at all | Red, alert icon, "Microphone not detected", fades after 1.6 s; nothing recorded | Microphone row yellow, "Not detected" |
| Mic privacy switch off | Red, "Turn on your mic in Settings" | Status "Microphone blocked" + Open Settings |
| Mic unplugged mid-recording | Stops; transcribes what was captured | Row yellow until it's back |
| Model not ready yet | "Getting the speech model ready…" | Status with % |
| Model download/load failed | Red, "Speech model failed" | Status + Retry |
| Transcription error / empty text | Fades (Mac shows its error cue the same way) | — |
| Foreground app is elevated | Blue, "Copied. Press Ctrl+V" | — |
| Clipboard busy (another app holds it) | Red, "Couldn't paste" (text kept as Last dictation) | Last dictation with Copy |
| Keyboard hook can't install | — | Status "Hotkey unavailable" + Retry |

## Testing

- **Akoya.Core.Tests** (any OS, CI on `windows-latest`): state machine (hold, tap/latch, Esc,
  max 300 s, silence gate, empty text, practice live preview, never two transcriptions at once,
  mic missing), hotkey decoder (Right Alt, Right Ctrl, AltGr fake LCtrl, Copilot sequence, key
  repeat, Esc only while recording), level meter/RMS/waveform/glow (ported Mac test cases),
  microphone naming (fixtures for laptop, headset, Bluetooth, webcam, Broadcast, Voicemeeter,
  Stereo Mix, duplicates), resolver, settings round-trip and corrupt-file recovery, model
  manifest.
- **Akoya.App.Tests** (Windows): render every view state to PNG with `RenderTargetBitmap` (pill:
  listening, transcribing, pasted, red alert, copied; tray panel: ready, listening, last
  dictation, mic missing, update ready, hotkey menu; setup: welcome, mic list, mic one row, mic
  More expanded, mic amber, mic blocked, Try it) — CI commits the PNGs to the `ci-output` branch
  for visual review against the mockups. Engine smoke test: transcribe the model's bundled test
  WAV on CI (model cached between runs) and assert the expected words and timing. Clipboard
  snapshot/restore round-trip. Naming against the runner's real endpoints (smoke only).
- **Manual checklist** `docs/MANUAL_CHECKS_WINDOWS.md` on Rohit's PC: hold/tap/Esc in Windows
  Terminal, VS Code, Claude Code, Notepad, a browser; AltGr layout; Copilot key if present; pill
  on each monitor; admin window; unplugging the headset; clipboard restored; Win+V history clean;
  setup with the real device list; launch at startup; install/update/uninstall.

## Windows pitfalls covered

AltGr (hotkey details), UIPI (pasting), mic privacy switch (setup + errors), clipboard history
(pasting), two copies running (single instance), DPI and multiple monitors (pill placement),
exclusive-fullscreen games hide the pill (accepted; dictation still works).

## Out of scope for v1

Code signing, winget, non-English models, live words in the pill, a settings window.

## Mac reference

The Mac app's behaviour and copy are the reference: `Sources/AkoyaCore` (AppCoordinator,
HotkeyDecoder, LevelMeter, RMSWindower, WaveformGeometry/Layout, GlowEnvelope, MicrophoneResolver,
PasteTextInserter, LivePreviewScheduler), `Tests/AkoyaCoreTests`, the specs in
`docs/superpowers/specs/`, and `docs/MANUAL_CHECKS.md`.
