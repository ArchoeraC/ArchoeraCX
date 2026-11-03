#!/usr/bin/env bash
# Stamp the version from app/pubspec.yaml into the static site.
#
# index.html always keeps a concrete value so the folder still opens directly;
# run this after bumping the version in app/pubspec.yaml.
set -euo pipefail

site_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repo_root="$(cd "${site_dir}/.." && pwd)"
pubspec="${repo_root}/app/pubspec.yaml"
index="${site_dir}/index.html"

version="$(sed -n 's/^version:[[:space:]]*//p' "${pubspec}" | head -n 1 | tr -d '[:space:]')"
if [[ -z "${version}" ]]; then
  echo "error: could not read version from ${pubspec}" >&2
  exit 1
fi

if ! grep -q 'data-app-version' "${index}"; then
  echo "error: ${index} has no data-app-version markers" >&2
  exit 1
fi

sed -i -E "s#(<span data-app-version>)[^<]*(</span>)#\1${version}\2#g" "${index}"
echo "site version -> ${version}"
