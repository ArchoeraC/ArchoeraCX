#!/usr/bin/env bash
# Local macOS verification for the C++ core, using a macOS SDK on Linux.
#
# CI builds on a real macOS runner. This script compiles the exact core
# sources (Objective-C++ included) and the C ABI smoke tests for arm64/x86_64
# with clang + ld64.lld, which catches macOS-only compile and link errors.
# It does not run the binaries: that still requires macOS.
#
# Requirements:
#   - clang/clang++ and ld64.lld
#   - macOS SDK at ~/.local/share/macos-sdk/MacOSX14.0.sdk
#     (override with ARCHOERACX_MACOS_SDK; see Reference/README.md for the
#      download-only-for-local-builds rule)
#
# Usage:
#   bash app/tool/verify-macos-core.sh
#   bash app/tool/verify-macos-core.sh --arch arm64
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP="${ROOT}/app"
SDK="${ARCHOERACX_MACOS_SDK:-${HOME}/.local/share/macos-sdk/MacOSX14.0.sdk}"
ARCHS=(arm64 x86_64)

while [[ $# -gt 0 ]]; do
  case "$1" in
    --arch)
      ARCHS=("${2:?--arch needs arm64 or x86_64}")
      shift 2
      ;;
    --sdk)
      SDK="${2:?--sdk needs a path}"
      shift 2
      ;;
    -h|--help)
      sed -n '2,22p' "$0"
      exit 0
      ;;
    *)
      echo "error: unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

[[ -d "${SDK}" ]] || { echo "error: macOS SDK not found: ${SDK}" >&2; exit 1; }
command -v clang++ >/dev/null 2>&1 || { echo "error: clang++ not found" >&2; exit 1; }
command -v ld64.lld >/dev/null 2>&1 || { echo "error: ld64.lld not found" >&2; exit 1; }

CORE_SRC="${APP}/core/src"
CORE_SOURCES=(
  archoera_core method_registry module_host module_registry core_json
  core_result core_tasks core_status core_actions core_appearance
  core_module_bridge platform_system_common platform_status_common
  platform_gpu_stub platform_appearance_macos platform_system_macos
  platform_status_macos
)
INCLUDES=(-I "${APP}/contracts/abi" -I "${APP}/contracts/module" -I "${CORE_SRC}")

BUILD_DIR="${ARCHOERACX_VERIFY_BUILD:-$(mktemp -d "${TMPDIR:-/tmp}/archoera-macos-verify.XXXXXX")}"
rm -rf "${BUILD_DIR}"
mkdir -p "${BUILD_DIR}"

for arch in "${ARCHS[@]}"; do
  target="${arch}-apple-macos12.0"
  out="${BUILD_DIR}/${arch}"
  mkdir -p "${out}"
  echo "── ${arch} core objects"
  for source in "${CORE_SOURCES[@]}"; do
    extension="cpp"
    case "${source}" in
      platform_appearance_macos|platform_system_macos) extension="mm" ;;
    esac
    clang++ -target "${target}" -isysroot "${SDK}" -std=c++20 -fobjc-arc \
      "${INCLUDES[@]}" -c "${CORE_SRC}/${source}.${extension}" \
      -o "${out}/${source}.o"
  done

  echo "── ${arch} core dylib"
  clang++ -target "${target}" -isysroot "${SDK}" -dynamiclib -fuse-ld=lld \
    -o "${out}/libarchoera_core.dylib" "${out}"/*.o \
    -framework AppKit -framework Foundation -framework UserNotifications

  echo "── ${arch} C ABI smoke tests"
  for test in abi_smoke async_smoke module_smoke event_queue_smoke; do
    if [[ "${test}" == "abi_smoke" ]]; then
      clang -target "${target}" -isysroot "${SDK}" -std=c11 \
        -I "${APP}/contracts/abi" -c "${APP}/core/tests/${test}.c" \
        -o "${out}/${test}.o"
    else
      clang++ -target "${target}" -isysroot "${SDK}" -std=c++20 \
        -I "${APP}/contracts/abi" -I "${APP}/contracts/module" \
        -I "${CORE_SRC}" \
        -DARCHOERACX_FAKE_MODULE_PATH="\"${out}/libarchoera_fake_module.dylib\"" \
        -c "${APP}/core/tests/${test}.cpp" -o "${out}/${test}.o"
    fi
  done

  # The fake module is the module-host oracle; build it with the same export
  # macro the real modules use.
  clang++ -target "${target}" -isysroot "${SDK}" -std=c++20 \
    -DARCHOERACX_MODULE_BUILD -I "${APP}/contracts/abi" \
    -I "${APP}/contracts/module" \
    -dynamiclib -fuse-ld=lld -o "${out}/libarchoera_fake_module.dylib" \
    "${APP}/core/tests/fake_module.cpp"

  for test in abi_smoke async_smoke module_smoke event_queue_smoke; do
    clang++ -target "${target}" -isysroot "${SDK}" -fuse-ld=lld \
      -o "${out}/${test}" "${out}/${test}.o" \
      "${out}/libarchoera_core.dylib"
  done

  file "${out}/libarchoera_core.dylib"
  file "${out}/abi_smoke" "${out}/async_smoke" "${out}/module_smoke" \
    "${out}/event_queue_smoke"
done

echo "macOS core compile/link verification passed (binaries were not run)"
