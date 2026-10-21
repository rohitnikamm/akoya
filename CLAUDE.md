# Working on Akoya

- Commits and pull requests must not list Claude as an author: no `Co-Authored-By: Claude` or
  similar trailers, and no "Generated with Claude Code" lines. Check `git log` before pushing.
- Rohit wants to see mockups or renders before UI work is built, and every visual result must be
  checked (screenshot it and look at it) before it is shown to him.
- The repo and the Homebrew tap (`rohitnikamm/homebrew-tap`) are public.
- Mac app: `swift test`, `scripts/build-app.sh debug`. Releases: `scripts/release.sh VERSION`.
- Windows version (in `windows/`, spec: `docs/superpowers/specs/2026-10-20-akoya-windows-design.md`):
  - Local, any OS with the .NET 10 SDK: `dotnet build windows/Akoya.Windows.slnx -c Release -p:EnableWindowsTargeting=true`
    and `dotnet test windows/tests/Akoya.Core.Tests -c Release` (seconds). The App tests (WPF rendering,
    clipboard, Win32, the speech model) only run on Windows.
  - Windows CI: `windows/scripts/ci.sh` pushes the current branch, waits for the `windows` workflow and prints its
    summary; results (summary.md, logs, `.trx`, snapshot PNGs) come from the `ci-output` branches. It only runs for
    pushes touching `windows/**` or the workflow. Never push a `windows-v*` tag: that publishes a release build and is
    Rohit's step.
  - Releases: tag `windows-vX.Y.Z` makes the `release-windows` workflow build a Velopack `Setup.exe` into a draft
    release (`windows/scripts/package.ps1`). Manual checks on a real PC: `docs/MANUAL_CHECKS_WINDOWS.md`.
- Features: `docs/FEATURES.md` lists every user-facing feature on the Mac and Windows versions. When you add or change
  one on either platform, update that file in the same change and either build it on the other platform or mark it
  Different on purpose (with the reason) or Missing.
