#!/usr/bin/env bash
# End-to-end Modrinth smoke test: C# plan -> Rust executor.
#
# Usage:
#   ./tool/try-modrinth.sh [project] [game-version] [loader] [root]
#
# Example:
#   ./tool/try-modrinth.sh sodium 1.21.1 fabric
#
# The default root is <repo>/.local/modrinth-test (gitignored). No key is
# required for Modrinth.
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
app_dir="$(cd -- "$script_dir/.." && pwd)"
repo_dir="$(cd -- "$app_dir/.." && pwd)"

project="${1:-sodium}"
game_version="${2:-1.21.1}"
loader="${3:-fabric}"
root="${4:-$repo_dir/.local/modrinth-test}"

mkdir -p "$root"
plan="$root/modrinth-plan.json"

dotnet run --project "$app_dir/modules/business/Archoera.DevHost" -c Release -- \
  --modrinth-plan \
  --project "$project" \
  --kind mod \
  --game "$game_version" \
  --loader "$loader" \
  --root "$root" \
  --out "$plan"

cargo run --release \
  --manifest-path "$app_dir/modules/downloader/Cargo.toml" \
  --bin archoera-dl-cli -- "$plan"
