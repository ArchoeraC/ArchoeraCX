#!/usr/bin/env bash
# Copy the native slice into a built macOS .app and ad-hoc sign it.
#
# Xcode calls this from a Run Script phase after Flutter's embed phase, so
# `flutter build macos --release` produces a runnable bundle without a second
# manual copy step. Debug builds may run before the native modules exist; in
# that case the script warns and leaves the app alone (CoreClient still probes
# app/build/native during development).
#
# Set ARCHOERACX_BUNDLE_REQUIRE=1 to turn a missing module into an error;
# packaging/macos/package.sh independently fails when a module is missing.
set -euo pipefail

APP="${1:-}"
if [[ -z "${APP}" ]]; then
  echo "usage: $0 <path-to-.app>" >&2
  exit 2
fi
APP="${APP%/}"
if [[ ! -d "${APP}" ]]; then
  echo "warning: $APP does not exist; skipping native bundling" >&2
  exit 0
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${ROOT}/build/native"
DEST="${APP}/Contents/Frameworks"
mkdir -p "${DEST}"

# Keep the list in sync with the module registry: the core loads archoera_cs,
# the C# module spawns archoera-dl/archoera-vault, and SQLitePCLRaw loads
# libe_sqlite3 from the same directory.
LIBS=(
  libarchoera_core.dylib
  libarchoera_cs.dylib
  libe_sqlite3.dylib
  libarchoera_dl.dylib
  archoera-dl
  archoera-vault
)

missing=0
for name in "${LIBS[@]}"; do
  rm -f "${DEST}/${name}"
  if [[ -f "${SRC}/${name}" ]]; then
    cp -f "${SRC}/${name}" "${DEST}/${name}"
    chmod 0755 "${DEST}/${name}"
  else
    missing=1
    echo "warning: native module not found: ${SRC}/${name}" >&2
  fi
done

if [[ "${missing}" == 1 && "${ARCHOERACX_BUNDLE_REQUIRE:-0}" == 1 ]]; then
  echo "error: macOS bundle is incomplete (ARCHOERACX_BUNDLE_REQUIRE=1)" >&2
  exit 1
fi

if [[ "${missing}" == 0 ]]; then
  # Ad-hoc signing must follow every byte change. Xcode signs the outer app
  # after this phase; nested code needs its own signature.
  for name in "${LIBS[@]}"; do
    codesign --force --sign - "${DEST}/${name}"
  done
  echo "macOS native slice bundled into ${DEST}"
fi
