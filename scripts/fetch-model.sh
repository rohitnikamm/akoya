#!/bin/bash
# Usage: scripts/fetch-model.sh
#
# Prints the path of a verified copy of the pinned speech model (scripts/model-pins.txt) in
# ~/Library/Caches/Akoya-build/models/. A file is downloaded only if it is missing or its
# sha256 differs; exits non-zero on any mismatch after download.
set -euo pipefail
cd "$(dirname "$0")/.."

PINS="scripts/model-pins.txt"
DEST="${AKOYA_MODEL_CACHE:-$HOME/Library/Caches/Akoya-build/models}/parakeet-tdt-0.6b-v2"

REPO="$(awk '$1 == "repo" {print $2}' "$PINS")"
REVISION="$(awk '$1 == "revision" {print $2}' "$PINS")"
if [ -z "$REPO" ] || [ -z "$REVISION" ]; then
    echo "error: $PINS must start with 'repo' and 'revision' lines" >&2
    exit 1
fi

while read -r sha path; do
    [ -n "$sha" ] || continue
    case "$sha" in repo|revision) continue ;; esac
    file="$DEST/$path"
    if [ -f "$file" ] && [ "$(shasum -a 256 "$file" | awk '{print $1}')" = "$sha" ]; then
        continue
    fi
    echo "Downloading $path" >&2
    mkdir -p "$(dirname "$file")"
    curl -fL --retry 3 -sS -o "$file.part" "https://huggingface.co/$REPO/resolve/$REVISION/$path"
    actual="$(shasum -a 256 "$file.part" | awk '{print $1}')"
    if [ "$actual" != "$sha" ]; then
        rm -f "$file.part"
        echo "error: sha256 mismatch for $path (expected $sha, got $actual)" >&2
        exit 1
    fi
    mv "$file.part" "$file"
done < "$PINS"

echo "$DEST"
