#!/usr/bin/env bash
# One-stop native build for Linux and macOS.
#
# CI and local development share this entry point so a change to the build
# order or the injected Microsoft client id never has to be repeated in the
# workflow. The Windows equivalent is tool/build-native.bat.
set -euo pipefail

cd "$(dirname "$0")/.."

./tool/build-core.sh
./tool/build-cs.sh "$@"
./tool/build-dl.sh
./tool/build-vault.sh
