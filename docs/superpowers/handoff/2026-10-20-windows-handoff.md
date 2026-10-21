# Handoff: Akoya for Windows (2026-10-20, 9:20 PM CT)

Written at the end of a long cloud session so a new Claude Code session on Rohit's PC can pick up without the chat.
Read this, then CLAUDE.md, the spec (`docs/superpowers/specs/2026-10-20-akoya-windows-design.md`), the plan
(`docs/superpowers/plans/2026-10-20-akoya-windows.md`) and `docs/FEATURES.md`.

## Where things stand
- Branch `windows-design` (pushed), head `b3e1bd7`. All 14 plan tasks are built, plus one fix round after the final
  whole-branch review and an approved pill-options round. Windows CI on b3e1bd7: build 0 warnings, Core 405/405,
  App 388/388, smoke run passed.
- Nothing is merged to `main` and no release is tagged. Both are Rohit's call. Never push a `windows-v*` tag.
- You are now ON the Windows PC, so you can do what the cloud session couldn't: run the App tests
  (`dotnet test windows\tests\Akoya.App.Tests -c Release`), launch the app (`dotnet run --project
  windows\src\Akoya.App -c Release`) and look at it. `windows/scripts/ci.sh` is a bash script for the cloud setup; on
  this PC just run the tests locally.

## Rohit's decisions (all binding)
- Pill: compact dot waveform; jelly pop-in (circle that springs out, 0.42 s; plain fade when Windows animations are
  off); sits 52 DIP above the taskbar's top edge measured as if shown, so the same height shown or auto-hidden
  ("height B"; if it feels too high, go to "A": 18 DIP above the taskbar's shown top).
- Tray panel matches the Mac's all-in-one dropdown, with a "Pill" row in place of the Mac's "Menu bar icon" row:
  Waveform Pixel | Solid, Red while recording (off by default). With Red while recording on, the processing sweep is
  amber (red, then amber, then the blue Pasted check). With it off, white dots and a blue-white sweep. No "Hide when
  idle" on Windows (the tray icon already sits behind the ^ overflow and the pill only shows while talking).
- Akoya never switches the mic on its own. Missing mic: red pill "Microphone not detected", nothing recorded, tray
  Microphone row yellow. Setup: Welcome, Microphone, Try it; one mic screen for every case; Windows default
  highlighted; "More" at the bottom.
- The Ctrl+V fallback ("Copied. Press Ctrl+V") stays; Rohit called it very important.
- `docs/FEATURES.md` is the parity list; every feature change on either platform updates it (CLAUDE.md rule).
- Copy has no em dashes. Commits carry no Claude co-author or "Generated with" lines.
- Rohit dropped the RunGap question (setup works as is).

## What the last fix round changed (worth checking by hand on this PC)
- Clipboard: raw Win32 byte-for-byte snapshot and restore (private formats, Excel cells, Explorer cut, PNG),
  password-manager exclusion markers kept, restore marked so it doesn't land in Win+V history; if the clipboard can't
  be copied fully, Akoya types the text with SendInput instead of borrowing the clipboard.
- Tap vs hold is measured from the keyboard hook's own timestamps (Bluetooth mics that take seconds to open).
- Model: per-file sizes pinned; a model that fails to load is deleted so Retry downloads it again.
- Tray panel gives focus back to the window you were typing in when it closes; pressing the hotkey while it's open
  closes it first so the paste lands in the field.
- Quit waits for a paste to finish; sleep is treated like a lock; a press during an alert retries; settings load
  retries through a sharing violation; mic-change bursts are debounced.
- Try it: "Didn't catch that. Try again." after a silent take; the card's glow rises with the voice. Tray glow holds
  still when Windows animations are off. A watchdog re-installs the keyboard hook if Windows drops it.

## Open items, in order
1. Rohit's second hands-on test of b3e1bd7 (he may be doing it now): jelly pop and height with his auto-hidden
   taskbar; the Pill row (Solid, Red while recording, amber sweep); tray focus restore; clipboard round-trip with
   Excel cells and an image; quick tap with Bluetooth headphones. Fix what he reports. Logs:
   `%LocalAppData%\Akoya\logs\akoya.log`.
2. A scoped re-review of the fix round was still running in the cloud session when this was written. If Rohit
   brings back findings, fix them. Otherwise consider re-reviewing `a4a4d0d..05ecb1e` yourself, focusing on
   `windows/src/Akoya.App/Platform/SystemClipboard.cs` and `ClipboardPasteInserter.cs` (handle ownership after
   SetClipboardData, OpenClipboard/CloseClipboard on every path, the typing fallback with surrogate pairs and
   newlines).
3. Things the implementers flagged for a real PC: the hook watchdog re-installs about once a minute during
   mouse-only use (GetLastInputInfo counts the mouse; harmless but check); tray focus restore under Windows'
   foreground rules; an auto-hidden taskbar on an edge shared with another monitor; Solid bar scaling and which
   columns turn amber are tunable.
4. Walk `docs/MANUAL_CHECKS_WINDOWS.md` on this PC.
5. Measure a 300 s dictation (time and memory). CI measured 120 s of audio in 13.6 s on CPU.
6. GPU spike: Rohit has an NVIDIA GPU. `InferenceProvider` (Auto / Cpu) is saved in settings.json but the
   sherpa-onnx transcriber always runs on the CPU. Try DirectML / CUDA providers, compare CPU vs GPU timing, write a
   small plan, keep CPU as the always-works path.
7. Questions still open for Rohit: Alt+F4 in setup counts as Skip? (today it doesn't, so setup shows again next
   launch); port the Windows-only behaviours to the Mac (screen lock cancels a recording, a "Copied" message when
   pasting is blocked, the amber sweep with Red while recording)? Right Alt as default on AltGr keyboard layouts
   takes AltGr away (consider Right Ctrl there).
8. When it's all good: superpowers:finishing-a-development-branch. Merging to main and tagging `windows-vX.Y.Z` are
   Rohit's steps.

## Key places
- Core (pure, tested on any OS): `windows/src/Akoya.Core` (Dictation/DictationCoordinator, Hotkey/HotkeyDecoder,
  Mics, Settings, Ui incl. PillPlacement and PillPopIn, Model/ModelManifest).
- App (WPF): `windows/src/Akoya.App` (AppHost, Pill/, Tray/, Setup/, Platform/, Speech/).
- Mockups: `docs/design/windows-pill-mockup.html`, `windows-onboarding-mockup.html`,
  `windows-pill-height-and-options-mockup.html`, `all-in-one-dropdown-target.html`.
