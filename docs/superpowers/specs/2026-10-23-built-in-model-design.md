# Built-in speech model: design

Date: 2026-10-23. Both platforms.

## Intent

Today Akoya downloads its speech model on first launch: the Mac from Hugging Face (about 450 MB of Core ML files,
through FluidAudio), Windows from a sherpa-onnx GitHub release (a 482 MB tarball, 660 MB unpacked). Company laptops
often block those downloads, so Akoya never becomes ready there. Rohit decided to ship the model inside the app on both
platforms, so Akoya works on first launch with no network at all.

Akoya has exactly one fixed model, so shipping it is the normal pattern (apps with a model picker download instead).
Rohit is firm that laptops must not be cluttered: there must never be two copies of the model on a machine.

## Goals

- A fresh install on a machine with no network becomes Ready without downloading anything, on the Mac and on Windows.
- Existing users who update stop using their downloaded copy and get it deleted, so only the copy inside the app
  remains.
- Updates replace the model with the app (one copy on disk, never two).
- Release builds are reproducible: the model files are pinned (version and checksums), not "whatever is latest".

## Non-goals

- No model picker or second model.
- No change to how transcription works, its speed or its accuracy (same model, same files as today).
- No separate "offline" download: there is one build per platform, and it includes the model.
- The model files are never committed to git (they are fetched at build time).

## Behaviour

### Where the model lives

- **Mac:** inside the app bundle, `Akoya.app/Contents/Resources/Models/parakeet-tdt-0.6b-v2/` (the same folder layout
  FluidAudio uses today: `Preprocessor`, `Encoder`, `Decoder`, `JointDecision` `.mlmodelc`, `parakeet_vocab.json`,
  `config.json`).
- **Windows:** inside the installed app, next to `Akoya.exe` in Velopack's `current` folder, in
  `models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/` (the same four files the manifest already pins and size-checks).

### At launch

1. If the built-in model is present and valid (the existing checks: every file present, Windows' pinned byte sizes),
   load it from there. No download, nothing written.
2. Once it has loaded successfully, delete the old downloaded copy if there is one:
   - Mac: `~/Library/Application Support/Akoya/Models/parakeet-tdt-0.6b-v2` (and the whole `Models` folder if it is
     then empty), plus any leftover `~/Library/Application Support/Silk/Models` copy.
   - Windows: `%LocalAppData%\Akoya\models\` and any `.part` download leftovers.
   Deletion happens only after the built-in model has loaded, so a broken bundle never leaves the user with nothing.
3. If there is no built-in model (a developer build made without it, or a damaged install), fall back to today's
   behaviour: use a downloaded copy if one exists, otherwise download it. The download path stays as it is today,
   including its progress, Retry and messages.

### Builds and releases

- **Mac:** `scripts/release.sh` puts the pinned model into the app before signing and zipping. The model is fetched
  once into a local build cache (outside the repo, e.g. `~/Library/Caches/Akoya-build/models/`), verified against
  pinned checksums, and copied into the bundle. `scripts/build-app.sh debug` does not include it by default (developer
  builds stay quick and use the downloaded copy); an option includes it so the built-in path can be tested locally.
- **Windows:** `windows/scripts/package.ps1` (run by the `release-windows` workflow) downloads the pinned tarball
  (URL and SHA-256 already in `ModelManifest`), checks it, unpacks the four files into the publish folder's `models/`
  before `vpk pack`. CI may cache the tarball between runs. Velopack's delta updates then ship the model only when it
  changes, so most Windows updates stay small.
- Pin the Mac model: the Hugging Face repository FluidAudio uses (`FluidInference/parakeet-tdt-0.6b-v2-coreml`) at a
  fixed commit, with a checksum per file, recorded in one place in the repo.

### Credit

The model is NVIDIA's Parakeet TDT 0.6B v2, under CC-BY-4.0, which allows redistribution with attribution. Ship a
short notice with each app (Mac: `Contents/Resources/ThirdPartyNotices.txt`; Windows: next to `Akoya.exe`) naming the
model, NVIDIA, the licence and the converted versions used (FluidInference's Core ML conversion; sherpa-onnx's int8
conversion), and add the same credit to `README.md`.

### Sizes (for the record)

- Mac: the release zip grows by roughly the model's compressed size (about 400–450 MB). Homebrew downloads the whole
  zip on each `brew upgrade`; on disk there is still one copy.
- Windows: Setup.exe grows by about 480 MB; delta updates normally don't include the model again. Check how much
  Velopack's local package cache holds after an update and note it in the manual checks.

## Docs

- `docs/FEATURES.md` "Speech model" rows (Download, Interrupted download, Where, and a new "Built in" row): Same on both
  platforms, with the download now a fallback.
- `docs/MANUAL_CHECKS.md` and `docs/MANUAL_CHECKS_WINDOWS.md`: fresh install with networking off becomes Ready; update
  from a version with a downloaded model deletes the old copy; the app size is as expected.

## Constraints

- Mac: macOS 14+, Apple Silicon; the bundle must stay signed after adding the model (sign after copying).
- Windows: GitHub release assets must stay under 2 GB each (they will).
- Keep the existing download code working as the fallback; its tests keep passing.

## Left to the builder

- How the Mac build fetches the pinned files (FluidAudio's own downloader pointed at a revision, `curl` against the
  pinned Hugging Face URLs, or similar), as long as it is pinned and checksum-verified.
- The exact build-script flag name for including the model in a debug build.
- Whether the Windows CI caches the tarball.
