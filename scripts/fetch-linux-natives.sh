#!/usr/bin/env bash
# Fetch or build the Linux native libraries the exporter loads, into one directory.
# The release zip ships them next to the executable, so the export needs no setup.
#
#   liboodle-data-shared.so  Oodle decompression (OodleUE release, same as CUE4Parse)
#   libSkiaSharp.so          PNG encoding (SkiaSharp.NativeAssets.Linux.NoDependencies, same version as
#                            the project; the NoDependencies build needs no system fontconfig/freetype/png)
#   libdetex.so              BC texture decoding (built from hglm/detex; CUE4Parse ships only a Windows build)
#
# Usage: scripts/fetch-linux-natives.sh <output-dir>
# Needs curl, unzip, gcc, git. Everything is pinned below.
set -euo pipefail

OUT_DIR="${1:?usage: $0 <output-dir>}"

OODLE_RELEASE="2026-06-04-1357"
OODLE_ZIP_SHA256="458c66945ea6124d4f6fcf64cec5601ab230f61f6677a6ecab6cca621a9d0e11"
SKIASHARP_VERSION="3.119.1"
SKIASHARP_NUPKG_SHA256="4a9ad38720294e16c3a1e4e7fc969a4bb4ca766f5290ca153bc57c1ee0b2a692"
DETEX_REPO="https://github.com/hglm/detex.git"
DETEX_COMMIT="cab11584d9be140602a66fd9a88ef0b99f08a97a"

# libdetex.so built from DETEX_COMMIT with Debian gcc 14.2 (2026-10-17). It is byte-identical to
# the Detex the WRF Exporter built and used for its Linux exports. A different gcc can produce
# different bytes from the same source, so a mismatch only warns.
DETEX_REFERENCE_SHA256="942676712c2e338b65df6e98f20017719c4d8304620ac2a6c0a0db73e8b2aa63"

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
echo "${OODLE_ZIP_SHA256}  $WORK/oodle.zip" | sha256sum -c --quiet -
unzip -q -o -j "$WORK/oodle.zip" "lib/liboodle-data-shared.so" -d "$OUT_DIR"

# --- libSkiaSharp ---
curl -sfL -o "$WORK/skia.nupkg" \
    "https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux.nodependencies/${SKIASHARP_VERSION}/skiasharp.nativeassets.linux.nodependencies.${SKIASHARP_VERSION}.nupkg"
echo "${SKIASHARP_NUPKG_SHA256}  $WORK/skia.nupkg" | sha256sum -c --quiet -
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
if ldd "$OUT_DIR/libSkiaSharp.so" | grep -qiE "fontconfig|freetype|libpng"; then
    echo "libSkiaSharp.so links system graphics libraries; the release must not depend on them" >&2
    exit 1
fi
nm -D "$OUT_DIR/libdetex.so" | grep -q " T detexDecompressTextureLinear" \
    || { echo "libdetex.so does not export detexDecompressTextureLinear" >&2; exit 1; }

detex_sha="$(sha256sum "$OUT_DIR/libdetex.so" | cut -d' ' -f1)"
if [ "$detex_sha" != "$DETEX_REFERENCE_SHA256" ]; then
    echo "warning: libdetex.so sha256 $detex_sha differs from the reference build (gcc version?)" >&2
fi

chmod 755 "$OUT_DIR/liboodle-data-shared.so" "$OUT_DIR/libSkiaSharp.so" "$OUT_DIR/libdetex.so"

echo "Linux natives written to $OUT_DIR:"
ls -l "$OUT_DIR"/liboodle-data-shared.so "$OUT_DIR"/libSkiaSharp.so "$OUT_DIR"/libdetex.so
