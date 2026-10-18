# Akoya

macOS menu bar dictation: hold a key, speak, release. Transcribes on-device (Parakeet via FluidAudio) and pastes into the focused app. Apple Silicon, macOS 14+.

## Install

    brew install --cask rohitnikamm/tap/akoya

Akoya is not notarized. The cask removes the quarantine flag after install (as AeroSpace does), so it opens without a Gatekeeper prompt.

## First run

Akoya opens a short three-step setup the first time it launches:

1. **Welcome**: press "Get started".
2. **Permissions**, one at a time, in this order. Each step advances by itself once the switch is on:
   - **Microphone**: to record your voice.
   - **Accessibility**: to detect the hotkey (Right Option by default) and paste the transcription into the focused app.
3. **Try it**: hold the hotkey and say anything; your words appear live in the card. Press Done (or Skip).

The speech model (about 450 MB) downloads in the background during setup. If it fails to load, a Retry button appears in setup and in the menu bar dropdown.

If you close setup before it is done, the menu bar dropdown shows "Setup needed" with a "Finish setup" button that reopens setup at the first permission still missing. Relaunching Akoya does the same once at least one permission is already on (otherwise setup starts again from the welcome step). Closing setup with the x on the Try it step, or once both permissions are on, counts as Skip.

For a stable code signature, so these permissions survive rebuilds and updates, run `scripts/make-signing-cert.sh` once and follow its trust instructions. Do this before you grant any permissions, or skip it if the "Silk Self-Signed" identity is already in your keychain: that identity keeps permissions across rebuilds too. Switching signing identities after granting means granting the permissions again. Without any identity, builds sign ad-hoc and macOS may ask again.

## Usage

Hold Right Option, speak, release. Akoya transcribes and pastes. Or tap the key once to start and again to stop. Press Esc while recording to cancel.

Click the Akoya item in the menu bar to open the dropdown. There is no separate Settings window: everything is there. That is the current status, your last dictation (with a Copy button), the hotkey (click the keycap to choose Right Option, Command or Control), the microphone, the menu bar icon options (waveform look, red while recording, hide when idle; they fold away until you open the Menu bar icon row), Launch at login and Quit.

The Microphone row uses the system default input unless you pick one. If the microphone you picked is unplugged or disconnected (AirPods, say), the row shows it as "(unavailable)" and Akoya records from the system default until it is back. If you hide the idle icon, or just reopen Akoya from Spotlight, the dropdown opens under the menu bar item.

## Upgrading from Silk

Akoya was called Silk before 0.2.0. Before you open Akoya for the first time:

1. Quit Silk.
2. Turn off Silk's Launch at Login (Silk Settings, or System Settings > General > Login Items).
3. Delete the old Silk.app.

Akoya also asks a running Silk to quit when it starts, since both listening for the hotkey would paste every dictation twice, but removing Silk for good is the reliable fix.

Akoya uses a new bundle identifier, so macOS asks for Microphone and Accessibility again once (setup walks you through it). Speech models already downloaded under Silk are moved to Akoya's folder automatically, with no re-download. Old Silk preferences are left alone.

## Development

    swift test                          # run the tests
    scripts/build-app.sh debug          # -> build/products.noindex/Akoya.app

Releases: `scripts/release.sh VERSION` builds, publishes a GitHub release, and updates the Homebrew tap. Use `--dry-run` to try it locally.
