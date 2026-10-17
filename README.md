# Silk

macOS menu bar dictation: hold a key, speak, release. Transcribes on-device (Parakeet via FluidAudio) and pastes into the focused app.

## Build

    scripts/build-app.sh [debug|release] [version]   # -> build/Silk.app

For a stable code signature (so macOS permissions survive rebuilds), run `scripts/make-signing-cert.sh` once and follow its trust instructions. Without it the build signs ad-hoc.

## Test

    swift test
