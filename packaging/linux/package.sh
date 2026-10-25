#!/usr/bin/env bash
# Package the relocatable Linux Flutter bundle.
#
# Usage: packaging/linux/package.sh <bundle-dir> <version> [arch]
#
# Produces dist/ArchoeraCX-<version>-linux-<arch>.tar.gz plus a .sha256 file.
# The bundle must already contain lib/libarchoera_core.so etc. (tool/build-*.sh
# then `flutter build linux --release`); CI verifies that before calling here.
set -euo pipefail

BUNDLE="${1:?usage: package.sh <bundle-dir> <version> [arch]}"
VERSION="${2:?missing version}"
ARCH="${3:-x64}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP="${ROOT}/app"
DIST="${DIST_DIR:-${ROOT}/dist}"
NAME="ArchoeraCX-${VERSION}-linux-${ARCH}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/archoera-linux-package.XXXXXX")"
trap 'rm -rf "${WORK}"' EXIT

[[ -d "${BUNDLE}" ]] || { echo "error: bundle not found: ${BUNDLE}" >&2; exit 1; }
mkdir -p "${WORK}/${NAME}/licenses" "${DIST}"

cp -a "${BUNDLE}/." "${WORK}/${NAME}/"
cp "${ROOT}/README.md" "${ROOT}/LICENSE" "${ROOT}/PRIVACY.md" \
  "${ROOT}/THIRD-PARTY-NOTICES.md" "${WORK}/${NAME}/"
cp "${APP}/assets/fonts/OFL.txt" \
  "${APP}/assets/licenses/Tabler-Icons-MIT.txt" \
  "${APP}/assets/icons/loaders/LICENSE-APACHE-2.0.txt" \
  "${APP}/assets/icons/loaders/LICENSE-LGPL-2.1.txt" \
  "${WORK}/${NAME}/licenses/"
install -m 0644 "${APP}/assets/branding/SOURCES.md" \
  "${WORK}/${NAME}/licenses/branding-SOURCES.md"
install -m 0644 "${APP}/assets/icons/loaders/SOURCES.md" \
  "${WORK}/${NAME}/licenses/loader-icons-SOURCES.md"
install -m 0644 "${APP}/assets/icons/ui/SOURCES.md" \
  "${WORK}/${NAME}/licenses/ui-icons-SOURCES.md"

tar -C "${WORK}" --owner=0 --group=0 --numeric-owner \
  -czf "${DIST}/${NAME}.tar.gz" "${NAME}"
(
  cd "${DIST}"
  sha256sum "${NAME}.tar.gz" > "${NAME}.tar.gz.sha256"
)

echo "Packaged: ${DIST}/${NAME}.tar.gz"
ls -lh "${DIST}/${NAME}.tar.gz"
