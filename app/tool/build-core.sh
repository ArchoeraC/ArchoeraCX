#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

cmake -S .. -B build/native -DCMAKE_BUILD_TYPE=Release
cmake --build build/native -j

case "$(uname -s)" in
  Darwin) name="libarchoera_core.dylib" ;;
  *) name="libarchoera_core.so" ;;
esac

echo "Built: $(pwd)/build/native/${name}"
