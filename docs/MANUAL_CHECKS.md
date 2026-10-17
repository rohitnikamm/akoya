# Manual checks (first real run)

Nothing here could be checked automatically: it needs a screen, a microphone and macOS permissions.

## Setup
1. Optional, but recommended so permissions survive rebuilds: `scripts/make-signing-cert.sh`, then run the trust command it prints.
2. `scripts/build-app.sh debug 0.1.0 && open build/Akoya.app`

## Core flow
- [ ] Onboarding appears; each "Open System Settings" button shows a system prompt or the right pane, and Akoya is listed there.
- [ ] Menu status line goes Preparing → Downloading % → Model ready (first launch downloads ~450 MB).
- [ ] With Accessibility granted while Akoya is in the background, the hotkey starts working within ~2 s.
- [ ] Hold Right Option, speak, release: waveform while speaking, scanning bar, then text appears in TextEdit, Safari, Slack, Terminal and VS Code.
- [ ] The first word isn't clipped (engine starts on press).
- [ ] Holding the key without speaking: brief red cue, nothing pasted. Quiet speech still transcribes.
- [ ] Esc while recording cancels and doesn't close a dialog (a long-held Esc may still auto-repeat into the app).
- [ ] Copy an image first; it's still on the clipboard after dictating.
- [ ] Close Settings, then dictate right away: text lands in the previous app, not in Akoya.
- [ ] Deny the microphone, then press: red cue, no system dialog mid-dictation.

## Edge cases
- [ ] Password field / Terminal with Secure Keyboard Entry: holding the key does nothing and nothing gets stuck.
- [ ] AirPods as input, and connecting or disconnecting them mid-recording.
- [ ] Right Command and Right Control work when chosen in Settings; Toggle mode works.

## Look
- [ ] Light and dark menu bars, including a wallpaper-tinted menu bar; no clipping at the tallest column.
- [ ] Item sits left of Control Center and remembers a ⌘-dragged position after relaunch.
- [ ] Settings changes (look, red tint, hide idle icon, expand animation) apply live.
- [ ] With "Hide icon when idle" on, opening Akoya again from Finder/Spotlight opens Settings.
- [ ] VoiceOver reads the item as "Akoya".
- [ ] Rebuild and relaunch: permissions still granted (requires the self-signed cert).
