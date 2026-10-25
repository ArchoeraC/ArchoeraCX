#!/usr/bin/env bash
# Local Windows verification for the C++ core, using msvc-wine.
#
# CI builds on a real Windows runner. This script is the fast local loop: it
# compiles the exact core sources and C ABI smoke tests with MSVC, then runs
# them under Wine. NativeAOT (C#) and the Rust modules still need CI because
# their cross targets are not available on a Linux host.
#
# Requirements:
#   - msvc-wine at /opt/msvc (override with MSVC_INSTALL_DIR)
#   - wine
#
# Usage:
#   bash app/tool/verify-windows-core.sh
#   ARCHOERACX_VERIFY_BUILD=/tmp/archoera-win bash app/tool/verify-windows-core.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MSVC_INSTALL_DIR="${MSVC_INSTALL_DIR:-/opt/msvc}"
TOOLCHAIN="${MSVC_INSTALL_DIR}/cmake/toolchain-x64.cmake"

[[ -x "${MSVC_INSTALL_DIR}/bin/x64/cl" ]] || {
  echo "error: msvc-wine cl not found: ${MSVC_INSTALL_DIR}/bin/x64/cl" >&2
  echo "       set MSVC_INSTALL_DIR or install msvc-wine." >&2
  exit 1
}
[[ -f "${TOOLCHAIN}" ]] || { echo "error: toolchain not found: ${TOOLCHAIN}" >&2; exit 1; }
command -v wine >/dev/null 2>&1 || { echo "error: wine not found" >&2; exit 1; }

BUILD_DIR="${ARCHOERACX_VERIFY_BUILD:-$(mktemp -d "${TMPDIR:-/tmp}/archoera-win-verify.XXXXXX")}"
# A fresh configure keeps msvc-wine's dependency files away from the previous
# run (stale .d files with non-ASCII source paths confuse make).
rm -rf "${BUILD_DIR}"
mkdir -p "${BUILD_DIR}"

echo "msvc-wine: ${MSVC_INSTALL_DIR}"
echo "build dir: ${BUILD_DIR}"

cmake -S "${ROOT}" -B "${BUILD_DIR}" \
  -DCMAKE_TOOLCHAIN_FILE="${TOOLCHAIN}" \
  -DCMAKE_BUILD_TYPE=Release
cmake --build "${BUILD_DIR}" -j"$(nproc 2>/dev/null || echo 2)"

for exe in abi_smoke.exe async_smoke.exe module_smoke.exe event_queue_smoke.exe; do
  echo "── ${exe}"
  ( cd "${BUILD_DIR}" && WINEDEBUG="${WINEDEBUG:--all}" wine "${exe}" )
done

echo "Windows core verification passed"
