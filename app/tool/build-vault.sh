#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

VAULT_DIR="core/vault-rs"
OUT_DIR="build/native"
mkdir -p "$OUT_DIR"

case "$(uname -s)" in
  Darwin) NAME="archoera-vault" ;;
  MINGW*|MSYS*|CYGWIN*) NAME="archoera-vault.exe" ;;
  *) NAME="archoera-vault" ;;
esac

if [[ -x "$VAULT_DIR/build/archoera-vault" && "$(uname -s)" == "Linux" ]]; then
  install -m 0755 "$VAULT_DIR/build/archoera-vault" "$OUT_DIR/$NAME"
else
  (cd "$VAULT_DIR" && ./build.sh)
  install -m 0755 "$VAULT_DIR/build/archoera-vault" "$OUT_DIR/$NAME"
fi

echo "Built: $(pwd)/$OUT_DIR/$NAME"
