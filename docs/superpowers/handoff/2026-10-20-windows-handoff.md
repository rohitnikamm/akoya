# Handoff: Akoya for Windows (updated 2026-10-21)

For a new Claude Code session on Rohit's Windows PC. Read this, then CLAUDE.md, the spec
(`docs/superpowers/specs/2026-10-20-akoya-windows-design.md`) and `docs/FEATURES.md` (the feature list for both
platforms; it is current). The history of how we got here is in git (`git log`) and the merged pull requests.

## Where things stand
- Akoya for Windows is released. `main` is the current version; the latest release is `windows-v1.0.4`. Every
  release is a draft that gets notes and is published "not Latest", so the Mac's `v0.2.0` stays the repo's latest
  release (README badge, Homebrew). Rohit asks for each tag; tag only when he does (CLAUDE.md).
- Akoya is installed on this PC from Setup.exe and updates itself ("Update ready · Restart" in the tray panel).
- CI: `windows/scripts/ci.sh` works from Git Bash here (`gh` is installed and logged in; the remote is SSH). It pushes
  the current branch, waits for the `windows` workflow and prints the summary. A push that changes
  `windows/scripts/package.ps1` also runs the installer dry run (`release-windows`).
- Work goes on a branch, then a pull request that Rohit merges (Claude Code may not merge into `main` itself).
- Local branch `gpu-spike` (not pushed) keeps the GPU benchmark apps (`spike/gpu/`) that the GPU plan refers to.

## Releases so far
- 1.0.0: first release.
- 1.0.1: the dictated text stays on the clipboard; Try it shows the model download as dots filling under the hint.
- 1.0.2: the tray's "Restart" (update ready) is a light-blue text link like Copy.
- 1.0.3: the app icon (Akoya.exe, Start menu, taskbar, Settings › Apps, Setup.exe); at most 8 transcription threads.
- 1.0.4: quiet speech that moves the pill is transcribed (Rohit confirmed it works much better).

## Rohit's decisions (binding)
- Pill: compact 4-row dot waveform (today's Pixel stays; a round-dot version is Proposed, see below); jelly pop-in
  (0.42 s; plain fade with Windows animations off); 52 DIP above the taskbar's top measured as if shown, so the same
  height shown or auto-hidden. The processing sweep lights only the centre two dot rows. Solid stays as it is.
- Tray panel = the Mac's all-in-one dropdown, with a "Pill" row (Waveform Pixel | Solid, Red while recording, off by
  default; red on: red waveform and an amber sweep). No "Hide when idle". "Restart" to update is a light-blue link.
- Akoya never switches the mic on its own; a missing mic is a red pill "Microphone not detected" and a yellow tray row.
  Setup: Welcome, Microphone, Try it. While the model downloads, Try it's dots fill with the progress and the status
  sits under them, never in the text card.
- After a dictation the text stays on the clipboard as an ordinary copy (Win+V history keeps the earlier one); the old
  snapshot/restore is gone. The "Copied. Press Ctrl+V" fallback for admin windows stays (Rohit: very important).
- Silence gate: only a take that never moved the pill is silence; quiet takes are transcribed, minus a lone invented
  filler word. Each take's peak level is logged ("Take: peak -41 dBFS (level 2); transcribing"), never the words.
- No GPU path for now: the CPU is fast enough (a 7 s dictation in about 0.3 s); see
  `docs/superpowers/plans/2026-10-21-akoya-windows-gpu.md`.
- The Mac app is updated separately, later. FEATURES.md lists what the Mac lacks ("Missing on Mac"); don't change
  the Mac app from the Windows work.
- Rohit sees a mockup or render before UI work is built, and every visual is checked before he sees it.
- Copy has no em dashes. Commits and pull requests carry no Claude co-author or "Generated with" lines.

## Proposed, not built
- Pixel waveform as round dots growing from one centre line (5 rows of 3 DIP dots, or 7 rows of 2.5 DIP dots), with a
  small cluster gliding along the line while transcribing. Rohit is keeping today's for now. Mockups:
  https://claude.ai/artifact/WCoDv8qsipNgC7L4xW1veJ (14 designer reviews: none kept today's; the second round split
  6 for 7 rows, 4 for 5 rows).

## Open items
1. Hands-on checks on this PC: a Japanese input method (IME) while dictating; the Try it glow next to
   `docs/design/tryit-glow-target.png`; walk `docs/MANUAL_CHECKS_WINDOWS.md`.
2. Questions for Rohit: should Alt+F4 during setup count as Skip (today setup shows again next launch)? On AltGr
   keyboard layouts, Right Alt as the default hotkey takes AltGr away: default to Right Ctrl there?
3. Mac catch-up (separate work, when Rohit says): the "Missing on Mac" rows in FEATURES.md.

## Key places
- Core (pure, tested on any OS): `windows/src/Akoya.Core` (Dictation/DictationCoordinator, Hotkey/HotkeyDecoder,
  Mics, Settings, Ui incl. PillPlacement, PillPopIn, PillDots, TryItWave; Model/ModelManifest).
- App (WPF): `windows/src/Akoya.App` (AppHost, Pill/, Tray/, Setup/, Platform/, Speech/).
- Scripts: `windows/scripts/ci.sh` (CI), `package.ps1` (installer), `make-app-icon.ps1` (app icon from the Mac's
  `Resources/AppIcon.icns`), `make-tray-icon.py` (tray icons). README images: the `ReadmeImages` test (command in
  `windows/README.md`).
- Logs on this PC: `%LocalAppData%\Akoya\logs\akoya.log`.
- Mockups: `docs/design/` (pill, onboarding, pill height and options, the all-in-one dropdown target).
