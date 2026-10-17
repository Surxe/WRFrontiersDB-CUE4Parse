#!/usr/bin/env bash
# Fetch or build the Linux native libraries the exporter loads, into one directory.
# The release zip ships them next to the executable, so the export needs no setup.
#
#   liboodle-data-shared.so  Oodle decompression (OodleUE release, same as CUE4Parse)
#   libSkiaSharp.so          PNG encoding (SkiaSharp.NativeAssets.Linux, same version as the project)
#   libdetex.so              BC texture decoding (built from hglm/detex; CUE4Parse ships only a Windows build)
#
# Usage: scripts/fetch-linux-natives.sh <output-dir>
# Needs curl, unzip, gcc, git. Everything is pinned below.
set -euo pipefail

OUT_DIR="${1:?usage: $0 <output-dir>}"

OODLE_RELEASE="2026-06-04-1357"
SKIASHARP_VERSION="3.119.1"
DETEX_REPO="https://github.com/hglm/detex.git"
DETEX_COMMIT="cab11584d9be140602a66fd9a88ef0b99f08a97a"

# The detex modules that make up the library (the Makefile's list minus its CLI programs).
DETEX_MODULES=(
    bptc-tables bits clamp convert dds
    decompress-bc decompress-bptc decompress-bptc-float
    decompress-etc decompress-eac decompress-rgtc
    division-tables file-info half-float hdr ktx misc raw texture
)

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# --- Oodle ---
curl -sfL -o "$WORK/oodle.zip" \
    "https://github.com/WorkingRobot/OodleUE/releases/download/${OODLE_RELEASE}/gcc-x64-release.zip"
unzip -q -o -j "$WORK/oodle.zip" "lib/liboodle-data-shared.so" -d "$OUT_DIR"

# --- libSkiaSharp ---
curl -sfL -o "$WORK/skia.nupkg" \
    "https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux/${SKIASHARP_VERSION}/skiasharp.nativeassets.linux.${SKIASHARP_VERSION}.nupkg"
unzip -q -o -j "$WORK/skia.nupkg" "runtimes/linux-x64/native/libSkiaSharp.so" -d "$OUT_DIR"

# --- libdetex (built from source at a pinned commit) ---
git clone -q "$DETEX_REPO" "$WORK/detex"
git -C "$WORK/detex" checkout -q "$DETEX_COMMIT"
objs=()
for m in "${DETEX_MODULES[@]}"; do
    gcc -c -fPIC -std=c99 -D_POSIX_C_SOURCE=200809L -w -I"$WORK/detex" -O2 \
        "$WORK/detex/$m.c" -o "$WORK/detex/$m.o"
    objs+=("$WORK/detex/$m.o")
done
gcc -shared -o "$OUT_DIR/libdetex.so" "${objs[@]}"

# --- Check: each file is an ELF shared object, and detex exports the symbol CUE4Parse looks up ---
for lib in liboodle-data-shared.so libSkiaSharp.so libdetex.so; do
    head -c 4 "$OUT_DIR/$lib" | grep -q $'^\x7fELF' || { echo "not an ELF library: $lib" >&2; exit 1; }
done
nm -D "$OUT_DIR/libdetex.so" | grep -q " T detexDecompressTextureLinear" \
    || { echo "libdetex.so does not export detexDecompressTextureLinear" >&2; exit 1; }

chmod 755 "$OUT_DIR/liboodle-data-shared.so" "$OUT_DIR/libSkiaSharp.so" "$OUT_DIR/libdetex.so"

echo "Linux natives written to $OUT_DIR:"
ls -l "$OUT_DIR"/liboodle-data-shared.so "$OUT_DIR"/libSkiaSharp.so "$OUT_DIR"/libdetex.so
