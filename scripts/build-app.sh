#!/bin/bash
# Usage: scripts/build-app.sh [debug|release] [version] [--with-model]
# --with-model copies the pinned speech model (scripts/fetch-model.sh) into the app; releases always do.
set -euo pipefail
cd "$(dirname "$0")/.."

WITH_MODEL=0
ARGS=()
for arg in "$@"; do
    case "$arg" in
        --with-model) WITH_MODEL=1 ;;
        *) ARGS+=("$arg") ;;
    esac
done
CONFIG="${ARGS[0]:-debug}"
VERSION="${ARGS[1]:-0.0.1}"
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
cp Resources/ThirdPartyNotices.txt "$APP/Contents/Resources/ThirdPartyNotices.txt"
if [ "$WITH_MODEL" -eq 1 ]; then
    MODEL_DIR="$(scripts/fetch-model.sh)"
    mkdir -p "$APP/Contents/Resources/Models"
    ditto "$MODEL_DIR" "$APP/Contents/Resources/Models/parakeet-tdt-0.6b-v2"
fi
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
echo "Built $APP ($CONFIG, v$VERSION, model: $WITH_MODEL, signed with '$SIGN')"
