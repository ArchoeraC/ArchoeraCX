#!/usr/bin/env bash
# Package a built macOS .app for release.
#
# Usage: packaging/macos/package.sh <path-to-.app> <version> [arch]
#
# Produces dist/ArchoeraCX-<version>-macos-<arch>.tar.gz plus a .sha256 file.
# Native libraries and the ad-hoc signature are produced by
# `flutter build macos --release` (Runner.xcodeproj calls tool/bundle-macos.sh).
set -euo pipefail

APP="${1:?usage: package.sh <path-to-.app> <version> [arch]}"
VERSION="${2:?missing version}"
ARCH="${3:-arm64}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_ROOT="${ROOT}/app"
DIST="${DIST_DIR:-${ROOT}/dist}"
NAME="ArchoeraCX-${VERSION}-macos-${ARCH}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/archoera-macos-package.XXXXXX")"
trap 'rm -rf "${WORK}"' EXIT

APP="${APP%/}"
[[ -d "${APP}" ]] || { echo "error: .app not found: ${APP}" >&2; exit 1; }
mkdir -p "${WORK}/${NAME}/licenses" "${DIST}"

# Fail closed if the native slice is incomplete. Do not re-run the bundling
# script here: the Xcode phase already signed the modules and the outer app;
# copying them again would change nested signatures and invalidate the app's
# CodeResources seal.
native="${APP}/Contents/Frameworks"
for file in \
  libarchoera_core.dylib \
  libarchoera_cs.dylib \
  libe_sqlite3.dylib \
  libarchoera_dl.dylib \
  archoera-dl \
  archoera-vault; do
  [[ -f "${native}/${file}" ]] || { echo "error: missing ${file} in ${native}" >&2; exit 1; }
done
codesign --verify --deep --strict "${APP}"

# Keep the .app bytes untouched after signing: documentation and licenses live
# next to it in the archive, not inside Contents/Resources.
cp -a "${APP}" "${WORK}/${NAME}/ArchoeraCX.app"
cp "${ROOT}/README.md" "${ROOT}/LICENSE" "${ROOT}/PRIVACY.md" \
  "${ROOT}/THIRD-PARTY-NOTICES.md" "${WORK}/${NAME}/"
cp "${APP_ROOT}/assets/fonts/OFL.txt" \
  "${APP_ROOT}/assets/licenses/Tabler-Icons-MIT.txt" \
  "${APP_ROOT}/assets/icons/loaders/LICENSE-APACHE-2.0.txt" \
  "${APP_ROOT}/assets/icons/loaders/LICENSE-LGPL-2.1.txt" \
  "${WORK}/${NAME}/licenses/"
install -m 0644 "${APP_ROOT}/assets/branding/SOURCES.md" \
  "${WORK}/${NAME}/licenses/branding-SOURCES.md"
install -m 0644 "${APP_ROOT}/assets/icons/loaders/SOURCES.md" \
  "${WORK}/${NAME}/licenses/loader-icons-SOURCES.md"
install -m 0644 "${APP_ROOT}/assets/icons/ui/SOURCES.md" \
  "${WORK}/${NAME}/licenses/ui-icons-SOURCES.md"

# COPYFILE_DISABLE stops bsdtar from adding AppleDouble ._ files to the archive.
COPYFILE_DISABLE=1 tar -C "${WORK}" -czf "${DIST}/${NAME}.tar.gz" "${NAME}"
(
  cd "${DIST}"
  shasum -a 256 "${NAME}.tar.gz" > "${NAME}.tar.gz.sha256"
)

echo "Packaged: ${DIST}/${NAME}.tar.gz"
ls -lh "${DIST}/${NAME}.tar.gz"
