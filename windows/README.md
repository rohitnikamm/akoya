# Akoya for Windows

Hold a key, speak, release, and your words appear in any app. The same behaviour as the Mac app, with a slim pill above the taskbar instead of the menu bar icon. Design: [`docs/superpowers/specs/2026-10-20-akoya-windows-design.md`](../docs/superpowers/specs/2026-10-20-akoya-windows-design.md).

## Layout

| Folder | What |
| --- | --- |
| `src/Akoya.Core` | net10.0, no Windows APIs: the dictation state machine, hotkey decoder, level and waveform maths, microphone naming, settings, model manifest |
| `src/Akoya.App` | net10.0-windows, WPF and Win32 adapters: keyboard hook, WASAPI recorder, paste, pill, tray, setup, Velopack updater |
| `tests/Akoya.Core.Tests` | xUnit, runs on any OS |
| `tests/Akoya.App.Tests` | xUnit, needs Windows: renders every view to PNG, engine smoke test, clipboard |
| `scripts/ci.sh` | Push the branch, wait for the Windows CI run, print its summary (bash with `gh` logged in; Linux, macOS or Git Bash) |
| `scripts/package.ps1` | Build the self-contained package and `Setup.exe` with Velopack |

## Build and test

Needs the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10` on Windows).

```bash
dotnet build windows/Akoya.Windows.slnx -c Release
dotnet test windows/tests/Akoya.Core.Tests -c Release     # any OS, seconds
dotnet test windows/tests/Akoya.App.Tests -c Release      # Windows only
dotnet run --project windows/src/Akoya.App -c Release     # Windows only
```

The README's Windows images (`docs/assets/windows-*.png`) are drawn by the app's own views. After a UI change, redraw them on Windows from the repo root (PowerShell):

```powershell
$env:AKOYA_README_DIR = "$PWD\docs\assets"; dotnet test windows/tests/Akoya.App.Tests -c Release --filter ReadmeImages
```

On Linux or macOS add `-p:EnableWindowsTargeting=true` to `dotnet build` to compile the WPF project. Everything that needs Windows is verified by the `windows` GitHub workflow (`windows-latest`), and `scripts/ci.sh` runs it from a terminal.

## Installing and updating

Releases use [Velopack](https://velopack.io). `Setup.exe` installs per user (no admin prompt) into `%LocalAppData%\Akoya\`, adds a Start menu entry and an uninstaller. The package is self-contained win-x64, so the .NET runtime is not needed. The app checks the GitHub releases at launch and every 6 hours, downloads a newer Windows release in the background, and the tray panel then offers "Update ready · Restart". A copy that was not installed by `Setup.exe` (for example under `dotnet run`) never updates itself.

## Releasing

1. Make sure `windows` CI is green on the commit to release.
2. Tag it and push the tag: `git tag windows-v1.0.0 && git push origin windows-v1.0.0`.
3. The `release-windows` workflow builds `Setup.exe` and the update packages and attaches them to a **draft** release named "Akoya for Windows 1.0.0".
4. Try the draft's `Setup.exe` on a clean user, then publish the draft. Untick "Set as the latest release" so the Mac app's release badge and Homebrew cask are unaffected. Installed copies only see published releases.

The workflow also does a dry run (packs, uploads nothing, prints the `Setup.exe` size) when `.github/workflows/release-windows.yml` or `scripts/package.ps1` changes on a branch. Velopack's `vpk` version is read from the `Velopack` pin in `Directory.Packages.props`, so the two cannot drift apart.

Checks that need a real PC are in [`docs/MANUAL_CHECKS_WINDOWS.md`](../docs/MANUAL_CHECKS_WINDOWS.md).
