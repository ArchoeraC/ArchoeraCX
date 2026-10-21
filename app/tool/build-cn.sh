#!/usr/bin/env bash
set -euo pipefail

# Mainland-China distribution build:
# - no online game/resource/loader downloads in the C++ route table
# - no download methods registered in the C# business module
# - no Download tab in Flutter
# Account sign-in, local instances and local launch remain available.
cd "$(dirname "$0")/.."

export ARCHOERACX_CHINA_BUILD=1

./tool/build-core.sh
./tool/build-cs.sh
./tool/build-dl.sh

if [[ -x ./tool/build-vault.sh ]]; then
  ./tool/build-vault.sh
fi

flutter build linux --release \
  --dart-define=ARCHOERACX_CHINA_BUILD=true
