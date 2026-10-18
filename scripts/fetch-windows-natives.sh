#!/usr/bin/env bash
# Fetch the Windows native library the exporter loads, into one directory.
# The release zip ships it next to the executable, so the export never downloads anything.
#
#   oodle-data-shared.dll  Oodle decompression (OodleUE release, same as CUE4Parse and the Linux build)
#
# Detex and SkiaSharp need nothing here: CUE4Parse embeds Detex.dll, and the single-file publish
# bundles SkiaSharp's Windows native.
#
# Usage: scripts/fetch-windows-natives.sh <output-dir>
# Needs curl, unzip, sha256sum (Git Bash on the Windows runner has them). Everything is pinned below.
set -euo pipefail

OUT_DIR="${1:?usage: $0 <output-dir>}"

OODLE_RELEASE="2026-06-04-1357"
OODLE_ZIP_SHA256="37f816a3e8f5a65a11f6defc8c28c0508287602767741ba4803c6a21a62a877b"

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# --- Oodle ---
curl -sfL -o "$WORK/oodle.zip" \
    "https://github.com/WorkingRobot/OodleUE/releases/download/${OODLE_RELEASE}/clang-cl-x64-release.zip"
echo "${OODLE_ZIP_SHA256}  $WORK/oodle.zip" | sha256sum -c --quiet -
unzip -q -o -j "$WORK/oodle.zip" "bin/oodle-data-shared.dll" -d "$OUT_DIR"

# --- Check: the file is a PE image ---
head -c 2 "$OUT_DIR/oodle-data-shared.dll" | grep -q '^MZ' \
    || { echo "not a Windows DLL: oodle-data-shared.dll" >&2; exit 1; }

echo "Windows natives written to $OUT_DIR:"
ls -l "$OUT_DIR/oodle-data-shared.dll"
