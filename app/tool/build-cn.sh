#!/usr/bin/env bash
set -euo pipefail

# Deprecated compatibility wrapper.
# ArchoeraCX now ships one binary: mainland-China download gating is decided
# at runtime from the coarse network region plus an optional compliance.json.
# This script intentionally sets no ARCHOERACX_CHINA_BUILD flag.
cd "$(dirname "$0")/.."

echo "note: build-cn.sh is deprecated; building the single runtime-gated distribution." >&2

./tool/build-core.sh
./tool/build-cs.sh
./tool/build-dl.sh

if [[ -x ./tool/build-vault.sh ]]; then
  ./tool/build-vault.sh
fi

flutter build linux --release
