#!/bin/bash
# Usage: scripts/release.sh [--dry-run] VERSION
#
# Builds Silk.app, zips it, publishes a GitHub release, and updates the Homebrew tap.
# With --dry-run everything happens locally (build, zip, sha256, cask render to
# build/silk.rb) and the gh/git commands that would publish are printed, not run.
# TAP_DIR overrides where the homebrew-tap clone lives (default: ../homebrew-tap).
set -euo pipefail
cd "$(dirname "$0")/.."

TAP_REPO="https://github.com/rohitnikamm/homebrew-tap"
DRY_RUN=0
VERSION=""

for arg in "$@"; do
    case "$arg" in
        --dry-run) DRY_RUN=1 ;;
        -*) echo "error: unknown option '$arg'" >&2; exit 2 ;;
        *)
            [ -z "$VERSION" ] || { echo "error: multiple versions given" >&2; exit 2; }
            VERSION="$arg"
            ;;
    esac
done

if [ -z "$VERSION" ]; then
    echo "Usage: scripts/release.sh [--dry-run] VERSION" >&2
    exit 2
fi
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "error: VERSION must be semver X.Y.Z (got '$VERSION')" >&2
    exit 2
fi
if [ -n "$(git status --porcelain)" ]; then
    echo "error: working tree is dirty; commit or stash changes first" >&2
    exit 1
fi

# Print the command in dry-run mode, run it otherwise.
run() {
    if [ "$DRY_RUN" -eq 1 ]; then
        echo "[dry-run] $*"
    else
        "$@"
    fi
}

TAG="v$VERSION"
ZIP="build/Silk-$TAG.zip"
CASK_OUT="build/silk.rb"
TAP_DIR="${TAP_DIR:-$(cd .. && pwd)/homebrew-tap}"

scripts/build-app.sh release "$VERSION"

rm -f "$ZIP"
ditto -c -k --keepParent build/Silk.app "$ZIP"
SHA256="$(shasum -a 256 "$ZIP" | awk '{print $1}')"
echo "$ZIP sha256: $SHA256"

sed -e "s/__VERSION__/$VERSION/g" -e "s/__SHA256__/$SHA256/g" packaging/silk.rb > "$CASK_OUT"
echo "Rendered cask to $CASK_OUT"

run gh release create "$TAG" "$ZIP" --title "Silk $VERSION" --generate-notes

if [ "$DRY_RUN" -eq 1 ]; then
    if [ -d "$TAP_DIR/.git" ]; then
        echo "[dry-run] git -C $TAP_DIR pull --ff-only"
    else
        echo "[dry-run] git clone $TAP_REPO $TAP_DIR"
    fi
    echo "[dry-run] mkdir -p $TAP_DIR/Casks && cp $CASK_OUT $TAP_DIR/Casks/silk.rb"
    echo "[dry-run] git -C $TAP_DIR add Casks/silk.rb"
    echo "[dry-run] git -C $TAP_DIR commit -m \"silk $VERSION\""
    echo "[dry-run] git -C $TAP_DIR push"
    echo "Dry run complete; nothing was published."
    exit 0
fi

if [ -d "$TAP_DIR/.git" ]; then
    git -C "$TAP_DIR" pull --ff-only
else
    git clone "$TAP_REPO" "$TAP_DIR"
fi
mkdir -p "$TAP_DIR/Casks"
cp "$CASK_OUT" "$TAP_DIR/Casks/silk.rb"
git -C "$TAP_DIR" add Casks/silk.rb
git -C "$TAP_DIR" commit -m "silk $VERSION"
git -C "$TAP_DIR" push
echo "Released Silk $VERSION"
