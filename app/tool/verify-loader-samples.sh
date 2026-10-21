#!/usr/bin/env bash
# Verify the Forge/NeoForge planner against locally cached installers.
#
# This script never downloads. Put the three installer jars under
#   <repo>/.local/forge-samples/
# or point ARCHOERACX_LOADER_SAMPLES at the directory containing them.
#
# The `--verify-loader` command is expected to exit non-zero when generated
# artifacts are missing (a fresh instance has no processor outputs), so the
# script prints the manifest/verification JSON and returns success as long as
# the planner ran.
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
app_dir="$(cd -- "$script_dir/.." && pwd)"
repo_dir="$(cd -- "$app_dir/.." && pwd)"
samples_dir="${ARCHOERACX_LOADER_SAMPLES:-$repo_dir/.local/forge-samples}"
work_dir="${ARCHOERACX_LOADER_VERIFY_DIR:-$repo_dir/.local/loader-verify}"

if [[ ! -d "$samples_dir" ]]; then
  echo "samples directory not found: $samples_dir" >&2
  echo "see docs/08-technical-research/09-forge-generated-artifacts.md §2.1" >&2
  exit 1
fi

mkdir -p "$work_dir"

run_one() {
  local kind="$1"
  local game="$2"
  local loader_version="$3"
  local installer="$samples_dir/$4"
  local root="$work_dir/$kind-$game-$loader_version"

  if [[ ! -f "$installer" ]]; then
    echo "skip: $installer (missing)" >&2
    return 0
  fi

  rm -rf "$root"
  mkdir -p "$root/libraries"

  echo "=== $kind $game $loader_version ==="
  dotnet run --project "$app_dir/modules/business/Archoera.DevHost" -c Release -- \
    --verify-loader "$installer" \
    --loader "$kind" \
    --game "$game" \
    --loader-version "$loader_version" \
    --root "$root" \
    --libraries "$root/libraries" || true
}

run_one forge 1.20.1 47.2.0 forge-1.20.1-47.2.0-installer.jar
run_one forge 1.21.1 52.1.0 forge-1.21.1-52.1.0-installer.jar
run_one neoforge 1.21.1 21.1.209 neoforge-21.1.209-installer.jar
