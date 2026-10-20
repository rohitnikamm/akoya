# Working on Akoya

- Commits and pull requests must not list Claude as an author: no `Co-Authored-By: Claude` or
  similar trailers, and no "Generated with Claude Code" lines. Check `git log` before pushing.
- Rohit wants to see mockups or renders before UI work is built, and every visual result must be
  checked (screenshot it and look at it) before it is shown to him.
- The repo and the Homebrew tap (`rohitnikamm/homebrew-tap`) are public.
- Mac app: `swift test`, `scripts/build-app.sh debug`. Releases: `scripts/release.sh VERSION`.
- Windows version: in design. Start with `docs/superpowers/specs/2026-10-20-akoya-windows-design.md`.
