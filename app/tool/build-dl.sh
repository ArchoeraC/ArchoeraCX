#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

MANIFEST="modules/downloader/Cargo.toml"
cargo build --release --locked --manifest-path "${MANIFEST}"

case "$(uname -s)" in
  Darwin) LIB_NAME="libarchoera_dl.dylib" ;;
  MINGW*|MSYS*|CYGWIN*) LIB_NAME="archoera_dl.dll" ;;
  *) LIB_NAME="libarchoera_dl.so" ;;
esac

ARTIFACT="$(pwd)/modules/downloader/target/release/${LIB_NAME}"

if [[ ! -f "${ARTIFACT}" ]]; then
  echo "error: downloader artifact not found: ${ARTIFACT}" >&2
  exit 1
fi

# The C++ module registry loads archoera_dl from build/native (and the Linux
# CMake install / macOS bundle copy it from there). Keeping only the CLI in
# build/native left the cdylib out of every bundle.
mkdir -p build/native
install -m 0755 "${ARTIFACT}" "build/native/${LIB_NAME}"
echo "Built: ${ARTIFACT}"
echo "Module: build/native/${LIB_NAME}"

# The Flutter/dev bridge invokes the CLI next to the native core; keep a copy
# under build/native so a bundle can find it without cargo paths.
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) CLI_NAME="archoera-dl.exe" ;;
  *) CLI_NAME="archoera-dl" ;;
esac
CLI_ARTIFACT="$(pwd)/modules/downloader/target/release/${CLI_NAME}"
if [[ -f "${CLI_ARTIFACT}" ]]; then
  mkdir -p build/native
  cp "${CLI_ARTIFACT}" "build/native/${CLI_NAME}"
  echo "CLI: build/native/${CLI_NAME}"
fi
