#!/bin/bash
# Usage: scripts/build-app.sh [debug|release] [version]
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${1:-debug}"
VERSION="${2:-0.0.1}"
IDENTITIES=("Akoya Self-Signed" "Silk Self-Signed") # Silk is the pre-rename identity, kept as a fallback.
# ".noindex" keeps Spotlight from listing dev builds next to the installed app.
APP="build/products.noindex/Akoya.app"

swift build -c "$CONFIG" --arch arm64
BIN_DIR="$(swift build -c "$CONFIG" --arch arm64 --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/Akoya" "$APP/Contents/MacOS/Akoya"
for bundle in "$BIN_DIR"/*.bundle; do
    [ -e "$bundle" ] && cp -R "$bundle" "$APP/Contents/Resources/"
done
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns" # regenerate with: swift scripts/make-icon.swift
sed "s/__VERSION__/$VERSION/g" Resources/Info.plist > "$APP/Contents/Info.plist"

# Valid identities only; capture first so grep -q closing the pipe cannot SIGPIPE under pipefail.
identities="$(security find-identity -v -p codesigning || true)"
SIGN="-"
for identity in "${IDENTITIES[@]}"; do
    if grep -q "$identity" <<<"$identities"; then
        SIGN="$identity"
        break
    fi
done
if [ "$SIGN" = "-" ]; then
    echo "warning: no 'Akoya Self-Signed' identity found; signing ad-hoc. Run scripts/make-signing-cert.sh so permissions persist across rebuilds." >&2
fi
codesign --force --sign "$SIGN" "$APP"
echo "Built $APP ($CONFIG, v$VERSION, signed with '$SIGN')"
