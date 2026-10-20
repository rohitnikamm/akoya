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

- **Install and updates: Velopack** (confirmed 2026-10-20). An unsigned `Setup.exe` on the GitHub
  release installs per-user (no admin prompt) with a Start menu entry and an uninstaller. The user
  clicks "More info" → "Run anyway" on the SmartScreen warning once. Updates come from the same
  GitHub releases: the tray panel shows "Update available" and applies it in one click. This also
  settles open question 4. `winget` later, maybe.
- **Setup: Welcome → Microphone → Try it** (confirmed 2026-10-20, mockup:
  `docs/design/windows-onboarding-mockup.html`). Guiding rule from Rohit: setup must be simple and
  seamless, nothing to think hard about. Three Windows-user reviewers (developer, gamer/streamer,
  everyday laptop user) all voted for this flow over a two-screen one.
  - The speech model starts downloading on Welcome.
  - Microphone screen, always shown, same layout in every case: "Which mic do you talk into?" /
    "Say something. Pick the one that moves." A list of inputs, each with a live 10-dot level
    meter, and a status chip ("Hearing you" green, or amber "<mic> isn't hearing you").
  - One mic is always highlighted: the Windows default. **Akoya never switches it on its own**;
    the user clicks a row to change it.
  - Plain names only ("This PC's microphone", "Webcam mic", "Galaxy Buds"); Windows' technical
    names never appear in setup. Virtual and loopback inputs (NVIDIA Broadcast, Stereo Mix) are
    folded into a "More (n)" row at the bottom of the list.
  - Only one mic: the same screen with one row, titled "Is this the mic you want?".
  - Windows mic privacy switch off: "Turn on your mic" with an Open Settings button; it moves on by
    itself once the switch is on.
  - The pick is saved as that exact device, so Discord or Voicemeeter changing the Windows default
    doesn't move it. The tray panel keeps the Mac's "System default (…)" option for anyone who
    wants to follow Windows.
- **Mic not detected** (confirmed 2026-10-20): if the chosen mic can't be found when the hotkey is
  pressed, nothing is recorded (no silent fallback to another mic). The pill still appears, turns
  red with an alert icon and "Microphone not detected", then fades. The tray panel's Microphone row
  is highlighted in yellow ("Not detected") until it's fixed.

- **Stack: C# on .NET 10 (LTS) with WPF** (confirmed 2026-10-20). Not .NET 8: .NET 8 and 9 reach
  end of support on 10 November 2026; .NET 10 is the current LTS. WPF because the tray icon, the
  panel and a transparent always-on-top pill are all straightforward there, and Akoya draws its own
  dark-glass look anyway. Rejected: WinUI 3 (awkward tray/overlay windows, fussier packaging) and
  Tauri (still needs native code for hook/paste/audio, so two languages).
- **Transcription: CPU always works; the GPU is an optional speed-up** (confirmed 2026-10-20).
  Rohit's PC has an NVIDIA GPU, but many PCs don't. Users must never have to install CUDA or
  anything else. Which GPU route (DirectML or Windows ML through ONNX Runtime, or sherpa-onnx's
  CUDA build) gets settled by a short spike on Rohit's PC at the start of the plan, which also
  measures CPU speed.
- **Dev setup is part of the work** (Rohit, 2026-10-20). The PC has no Visual Studio. Claude Code
  on the PC installs what's needed (the .NET 10 SDK via winget, the Velopack `vpk` tool); the
  `dotnet` CLI builds WPF without Visual Studio.
- **Code lives in this repo** in a `windows/` folder, sharing the README and releases (assumed,
  Rohit didn't object).

## Claude's guesses, not yet confirmed by Rohit

- (none left)

## Open questions (continue here)

1. ~~Confirm the guesses~~: all settled (see Decided).
2. ~~How it gets made~~: settled, .NET 10 + WPF, CPU with optional GPU (see Decided).
3. Pill position on multi-monitor setups (screen with the focused window, or the primary screen)?
4. ~~Updates~~: settled, Velopack updates from GitHub releases (see Decided).

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
