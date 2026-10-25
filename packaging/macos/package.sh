#!/usr/bin/env bash
# Package a built macOS .app for release.
#
# Usage:
#   packaging/macos/package.sh [tar|dmg|all] <path-to-.app> <version> [arch]
#
# Older callers that pass only <path-to-.app> <version> [arch] still get the
# portable tar.gz. Every format writes its archive plus a .sha256 under dist/
# (override with DIST_DIR).
#
# Native libraries and the ad-hoc signature are produced by
# `flutter build macos --release` (Runner.xcodeproj calls tool/bundle-macos.sh).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${HERE}/../.." && pwd)"
APP_ROOT="${ROOT}/app"
DIST="${DIST_DIR:-${ROOT}/dist}"

FORMAT="tar"
case "${1:-}" in
  tar|dmg|all)
    FORMAT="$1"
    shift
    ;;
esac

APP="${1:?usage: package.sh [tar|dmg|all] <path-to-.app> <version> [arch]}"
VERSION="${2:?missing version}"
ARCH="${3:-arm64}"

NAME="ArchoeraCX-${VERSION}-macos-${ARCH}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/archoera-macos-package.XXXXXX")"
trap 'rm -rf "${WORK}"' EXIT

APP="${APP%/}"
[[ -d "${APP}" ]] || { echo "error: .app not found: ${APP}" >&2; exit 1; }
mkdir -p "${DIST}" "${WORK}/stage"

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
cp -a "${APP}" "${WORK}/stage/ArchoeraCX.app"
cp "${ROOT}/README.md" "${ROOT}/LICENSE" "${ROOT}/PRIVACY.md" \
  "${ROOT}/THIRD-PARTY-NOTICES.md" "${WORK}/stage/"
mkdir -p "${WORK}/stage/licenses"
cp "${APP_ROOT}/assets/fonts/OFL.txt" \
  "${APP_ROOT}/assets/licenses/Tabler-Icons-MIT.txt" \
  "${APP_ROOT}/assets/icons/loaders/LICENSE-APACHE-2.0.txt" \
  "${APP_ROOT}/assets/icons/loaders/LICENSE-LGPL-2.1.txt" \
  "${WORK}/stage/licenses/"
install -m 0644 "${APP_ROOT}/assets/branding/SOURCES.md" \
  "${WORK}/stage/licenses/branding-SOURCES.md"
install -m 0644 "${APP_ROOT}/assets/icons/loaders/SOURCES.md" \
  "${WORK}/stage/licenses/loader-icons-SOURCES.md"
install -m 0644 "${APP_ROOT}/assets/icons/ui/SOURCES.md" \
  "${WORK}/stage/licenses/ui-icons-SOURCES.md"

cat > "${WORK}/stage/BUILD-INFO.txt" <<EOF
ArchoeraCX ${VERSION}
Version   : ${VERSION}
Target    : macos-${ARCH}
Built at  : $(date -u +%Y-%m-%dT%H:%M:%SZ)
Commit    : ${GITHUB_SHA:-$(git -C "${ROOT}" rev-parse --short HEAD 2>/dev/null || echo unknown)}
License   : AGPL-3.0-only (see LICENSE); third-party notices in THIRD-PARTY-NOTICES.md
EOF

write_hash() {
  local file="$1"
  (
    cd "${DIST}"
    shasum -a 256 "$(basename "${file}")" > "$(basename "${file}").sha256"
  )
}

pkg_tar() {
  # COPYFILE_DISABLE stops bsdtar from adding AppleDouble ._ files to the archive.
  local dir="${WORK}/tar/${NAME}"
  mkdir -p "${WORK}/tar"
  cp -a "${WORK}/stage/." "${dir}/"
  COPYFILE_DISABLE=1 tar -C "${WORK}/tar" -czf "${DIST}/${NAME}.tar.gz" "${NAME}"
  write_hash "${DIST}/${NAME}.tar.gz"
  echo "Packaged: ${DIST}/${NAME}.tar.gz"
}

pkg_dmg() {
  command -v hdiutil >/dev/null 2>&1 || {
    echo "error: hdiutil is required to build the .dmg (run on macOS)" >&2
    exit 1
  }
  ln -s /Applications "${WORK}/stage/Applications"
  hdiutil create \
    -volname "ArchoeraCX ${VERSION}" \
    -srcfolder "${WORK}/stage" \
    -ov -format UDZO \
    "${DIST}/${NAME}.dmg" >/dev/null
  write_hash "${DIST}/${NAME}.dmg"
  echo "Packaged: ${DIST}/${NAME}.dmg"
}

case "${FORMAT}" in
  tar) pkg_tar ;;
  dmg) pkg_dmg ;;
  all)
    pkg_tar
    pkg_dmg
    ;;
  *)
    echo "error: unknown format ${FORMAT}" >&2
    exit 2
    ;;
esac

ls -lh "${DIST}"/"${NAME}"* 2>/dev/null || true
