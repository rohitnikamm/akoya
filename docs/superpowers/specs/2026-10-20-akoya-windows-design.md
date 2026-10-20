# Akoya for Windows: Design (draft)

Status: **draft, brainstorming in progress, not approved yet** · started 2026-10-20 on the Mac,
to be continued on Rohit's Windows 11 PC.
Visual target: `docs/design/windows-pill-mockup.html` (open it in a browser; it loops through the
pill states) and `docs/assets/windows-preview.gif`.

## Picking this up in a new session

Read this file, `CLAUDE.md` and `README.md` first. Then continue the brainstorm (superpowers
brainstorming skill) from **Open questions** below, one question at a time. When everything is
settled: write the final design here, run the builder check, get Rohit's approval, then write the
plan (superpowers writing-plans). Nothing gets built before approval.

## Intent

Rohit uses Akoya on his Mac every day, mainly for development (dictating into terminals, Claude
Code, editors). He wants the same tool on his Windows 11 PC, installable from the Akoya GitHub
releases. It should work exactly like the Mac version. Only the way it shows itself changes,
because Windows has no menu bar.

## Decided by Rohit

- **Same behaviour as the Mac.** Hold the hotkey to talk, release to transcribe and paste into the
  focused app. Quick tap = hands-free (tap again to stop). Esc cancels. On-device transcription
  (Parakeet), nothing leaves the PC.
- **No waveform in the taskbar** (too much clutter). Instead, a **floating pill** at the bottom
  centre of the screen, just above the taskbar, shown only while dictating. This is the idea from
  the original Omakoe-style mockups, restyled in Akoya's electric blue.
- **Compact pill, waveform only:** dot waveform **4 dots tall** (the outer two rows light up only
  on louder sound), plus a timer. A wider variant with live words was considered and dropped.
- **Pill states:** listening (live waveform + timer) → transcribing (a blue scan sweeps across the
  dots, timer frozen) → "Pasted" with a blue check → fades out. Exact look: the mockup.
- **Look:** Akoya's dark glass with the bottom-weighted electric-blue rim glow (`#2F6BFF` →
  `#1D4ED8`), Outfit font, as on the Mac.
- **Built on the Windows PC itself**, with Claude Code running there so it can run and test the app
  directly.
- The Windows section of the README shows the pill animation (done; "in the works" for now).
- **Settings in the tray** (confirmed 2026-10-20, as in the mockup). Akoya's dot logo sits in the
  hidden tray overflow (behind the ⌃ arrow); the user can drag it onto the taskbar if they want.
  Clicking it opens the same all-in-one panel as the Mac dropdown minus the Menu bar icon row:
  status, hotkey keycap menu, last dictation with Copy, microphone picker, launch at startup, quit.
- **Default hotkey: Right Alt** (confirmed 2026-10-20). The hook swallows it so apps never see a
  lone Alt tap (no menu bar / Office key tips). Alternatives in the hotkey menu: Right Ctrl and the
  Copilot key (many new keyboards have Copilot where Right Ctrl was). Left Alt stays untouched.

## Claude's guesses, not yet confirmed by Rohit

- **Install:** an unsigned `.exe` installer attached to the GitHub release (the user clicks
  "More info" → "Run anyway" on the SmartScreen warning once). `winget` later, maybe.
- **Setup:** shorter than the Mac's: welcome → microphone access → model download → try it.
  Windows has no Accessibility permission to grant.
- **Code lives in this repo** (e.g. a `windows/` folder), sharing the README and releases.

## Open questions (continue here)

1. Confirm or change the guesses above, one at a time.
2. How it gets made: Rohit to say whether he has views or hands it to Claude. Claude's leaning:
   C# / .NET 8 (WPF or WinUI 3) for a native tray app and pill window; sherpa-onnx (C# bindings)
   running Parakeet TDT 0.6B v2 int8 on the CPU; WASAPI capture at 16 kHz mono; a low-level
   keyboard hook for the hotkey; clipboard snapshot + `SendInput` Ctrl+V + restore for pasting;
   an Inno Setup or Velopack installer. Ask whether the PC has an NVIDIA GPU (optional speed-up).
3. Pill position on multi-monitor setups (screen with the focused window, or the primary screen)?
4. Updates: check GitHub releases and offer an update, or just reinstall?

## Windows pitfalls to design for

- Right Alt is **AltGr** on many non-US keyboard layouts (Windows reports it as Ctrl+Alt).
- A normal app **cannot paste into windows running as administrator** (UIPI).
- Windows has a microphone privacy switch for desktop apps; setup must detect it and send the user
  to Settings › Privacy & security › Microphone.
- Clipboard managers and clipboard history can record the pasted text and the restored clipboard.
- Two running copies would both paste (the Mac app solves this with OtherInstances); use a
  single-instance mutex.

## Mac reference

The Mac app's behaviour and copy are the reference: `Sources/AkoyaCore` (AppCoordinator state
machine with a 0.30 s hold threshold, silence gate, 300 s max recording, live preview), the
specs in `docs/superpowers/specs/`, and `docs/MANUAL_CHECKS.md`.
