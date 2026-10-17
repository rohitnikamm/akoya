# Akoya

macOS menu bar dictation: hold a key, speak, release. Transcribes on-device (Parakeet via FluidAudio) and pastes into the focused app. Apple Silicon, macOS 14+.

## Install

    brew install --cask rohitnikamm/tap/akoya

Akoya is not notarized. The cask removes the quarantine flag after install (as AeroSpace does), so it opens without a Gatekeeper prompt.

## First run

Grant these in System Settings > Privacy & Security:

- **Microphone**: to record your voice.
- **Accessibility**: to paste the transcription into the focused app.
- **Input Monitoring**: to detect the Right Option hotkey.

For a stable code signature, so these permissions survive rebuilds and updates, run `scripts/make-signing-cert.sh` once and follow its trust instructions. Without it, builds sign ad-hoc and macOS may ask again.

## Usage

Hold Right Option, speak, release. Akoya transcribes and pastes. Press Esc while recording to cancel.

## Development

    swift test                          # run the tests
    scripts/build-app.sh debug          # -> build/Akoya.app

Releases: `scripts/release.sh VERSION` builds, publishes a GitHub release, and updates the Homebrew tap. Use `--dry-run` to try it locally.
