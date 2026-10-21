#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

VAULT_DIR="core/vault-rs"
OUT_DIR="build/native"
mkdir -p "$OUT_DIR"

case "$(uname -s)" in
  Linux)
    (cd "$VAULT_DIR" && ./build.sh)
    install -m 0755 "$VAULT_DIR/build/archoera-vault" "$OUT_DIR/archoera-vault"
    ;;
  Darwin)
    (cd "$VAULT_DIR" && cargo build --release)
    install -m 0755 "$VAULT_DIR/target/release/archoera-vault" "$OUT_DIR/archoera-vault"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    (cd "$VAULT_DIR" && cargo build --release)
    install -m 0755 "$VAULT_DIR/target/release/archoera-vault.exe" "$OUT_DIR/archoera-vault.exe"
    ;;
  *)
    (cd "$VAULT_DIR" && cargo build --release)
    install -m 0755 "$VAULT_DIR/target/release/archoera-vault" "$OUT_DIR/archoera-vault"
    ;;
esac

echo "Built: $(pwd)/$OUT_DIR"
