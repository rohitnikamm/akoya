#!/bin/bash
# Usage: scripts/build-app.sh [debug|release] [version]
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${1:-debug}"
VERSION="${2:-0.0.1}"
IDENTITY="Silk Self-Signed"
APP="build/Silk.app"

swift build -c "$CONFIG" --arch arm64
BIN_DIR="$(swift build -c "$CONFIG" --arch arm64 --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/Silk" "$APP/Contents/MacOS/Silk"
for bundle in "$BIN_DIR"/*.bundle; do
    [ -e "$bundle" ] && cp -R "$bundle" "$APP/Contents/Resources/"
done
sed "s/__VERSION__/$VERSION/g" Resources/Info.plist > "$APP/Contents/Info.plist"

# Valid identities only; capture first so grep -q closing the pipe cannot SIGPIPE under pipefail.
identities="$(security find-identity -v -p codesigning || true)"
if grep -q "$IDENTITY" <<<"$identities"; then
    SIGN="$IDENTITY"
else
    SIGN="-"
    echo "warning: '$IDENTITY' identity not found; signing ad-hoc. Run scripts/make-signing-cert.sh so permissions persist across rebuilds." >&2
fi
codesign --force --sign "$SIGN" "$APP"
echo "Built $APP ($CONFIG, v$VERSION, signed with '$SIGN')"
