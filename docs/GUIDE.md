# Akoya guide

## First run

Akoya opens a three-step setup the first time it launches:

1. **Welcome**: press "Get started".
2. **Permissions**, one at a time. Each step moves on by itself once the switch is on.
   - **Microphone**: to record your voice.
   - **Accessibility**: to detect the hotkey and paste the transcription into the focused app.
3. **Try it**: hold the hotkey and say anything; your words appear live in the card.

The speech model (about 450 MB) downloads in the background during setup. If it fails, a Retry button appears in setup and in the dropdown.

If you close setup early, the dropdown shows "Setup needed" with a "Finish setup" button that picks up at the first missing permission.

## The dropdown

Click the Akoya icon in the menu bar. Everything lives here; there is no Settings window.

- **Status** and the hotkey. Click the keycap to choose Right Option, Right Command or Right Control.
- **Last dictation**, with a Copy button.
- **Microphone**: the system default unless you pick one. If the one you picked is disconnected (AirPods, say), the row shows "(unavailable)" and Akoya records from the system default until it is back.
- **Dictation indicator**: show a floating pill or the menu bar waveform while you dictate, Pixel or Solid waveform, red while recording, hide the menu bar icon when idle. If the icon is hidden, open Akoya from Spotlight to get the dropdown back.
- **Launch at login** and **Quit**.

## Upgrading from Silk

Akoya was called Silk before 0.2.0. Before you open Akoya for the first time:

1. Quit Silk.
2. Turn off Silk's Launch at Login (System Settings > General > Login Items).
3. Delete Silk.app.

Akoya uses a new bundle identifier, so macOS asks for Microphone and Accessibility once more. Downloaded speech models are moved over automatically.

## Development

```bash
swift test                    # run the tests
scripts/build-app.sh debug    # build build/products.noindex/Akoya.app
scripts/release.sh VERSION    # publish a GitHub release and update the Homebrew tap (--dry-run to rehearse)
```

For a stable code signature, so permissions survive rebuilds, run `scripts/make-signing-cert.sh` once before granting any permissions and follow its trust instructions. Without an identity, builds sign ad-hoc and macOS may ask for permissions again after each build. Manual test steps are in [MANUAL_CHECKS.md](MANUAL_CHECKS.md).
