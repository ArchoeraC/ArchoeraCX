#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

CHINA_FLAG=""
if [[ "${ARCHOERACX_CHINA_BUILD:-0}" == "1" ]]; then
  CHINA_FLAG="-DARCHOERACX_CHINA_BUILD=ON"
fi

cmake -S .. -B build/native -DCMAKE_BUILD_TYPE=Release ${CHINA_FLAG}
cmake --build build/native -j

case "$(uname -s)" in
  Darwin) name="libarchoera_core.dylib" ;;
  *) name="libarchoera_core.so" ;;
esac

echo "Built: $(pwd)/build/native/${name}"
