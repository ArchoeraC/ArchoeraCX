#!/usr/bin/env bash
# Build a single-file Flatpak from a prebuilt ArchoeraCX Linux bundle.
#
# Usage: packaging/linux/package-flatpak.sh <bundle-dir> <version> [arch]
#
# This is the "wrap a prebuilt bundle" path: no flatpak-builder run, no source
# compilation. The bundle keeps its original layout under /app so the C++ core,
# C# module and Rust helpers still resolve siblings relative to the executable.
set -euo pipefail

APP_NAME="ArchoeraCX"
APP_ID="awa.archoera.archoera_cx"
BIN="archoera_cx"
RUNTIME="org.freedesktop.Platform//24.08"
ARCH="${3:-x64}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${HERE}/../.." && pwd)"
DIST="${DIST_DIR:-${ROOT}/dist}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/archoera-flatpak.XXXXXX")"
trap 'rm -rf "${WORK}"' EXIT

BUNDLE="${1:?usage: package-flatpak.sh <bundle-dir> <version> [arch]}"
VERSION="${2:?missing version}"
[[ -d "${BUNDLE}" ]] || { echo "error: bundle not found: ${BUNDLE}" >&2; exit 1; }
BUNDLE="$(cd "${BUNDLE}" && pwd)"

# upload-artifact strips the executable bit.
for program in archoera_cx lib/archoera-dl lib/archoera-vault; do
  [[ -f "${BUNDLE}/${program}" ]] && chmod 0755 "${BUNDLE}/${program}"
done

# Rewrite Flutter's build-machine RUNPATHs to $ORIGIN before wrapping the
# bundle (same normalization as the Linux package script).
if command -v patchelf >/dev/null 2>&1; then
  patchelf --set-rpath '$ORIGIN/lib' "${BUNDLE}/archoera_cx"
  for library in "${BUNDLE}/lib/"*.so*; do
    [[ -f "${library}" ]] || continue
    patchelf --set-rpath '$ORIGIN' "${library}" 2>/dev/null || true
  done
fi

command -v flatpak >/dev/null 2>&1 || {
  echo "error: flatpak is required" >&2
  exit 1
}

appdir="${WORK}/app"
repo="${WORK}/repo"
mkdir -p "${DIST}" "${appdir}"

flatpak --user remote-add --if-not-exists flathub \
  https://flathub.org/repo/flathub.flatpakrepo
flatpak --user install -y --noninteractive "${RUNTIME}"

# The Platform runtime is used for both build and run: this packaging path does
# not compile anything, so a full SDK is unnecessary.
flatpak build-init "${appdir}" "${APP_ID}" "${RUNTIME}" "${RUNTIME}"

cp -a "${BUNDLE}/." "${appdir}/files/"

mkdir -p "${appdir}/files/bin" \
  "${appdir}/files/share/applications" \
  "${appdir}/files/share/metainfo" \
  "${appdir}/files/share/icons/hicolor/512x512/apps"
cat > "${appdir}/files/bin/${BIN}" <<EOF
#!/bin/sh
exec /app/${BIN} "\$@"
EOF
chmod 0755 "${appdir}/files/bin/${BIN}"

sed "s|@EXEC@|/app/bin/${BIN} %U|g" "${HERE}/archoera-cx.desktop.in" \
  > "${appdir}/files/share/applications/${APP_ID}.desktop"
sed -e "s|@VERSION@|${VERSION}|g" \
    -e "s|@DATE@|$(date -u +%Y-%m-%d)|g" \
  "${HERE}/awa.archoera.archoera_cx.metainfo.xml.in" \
  > "${appdir}/files/share/metainfo/${APP_ID}.metainfo.xml"
install -m 0644 "${ROOT}/app/assets/branding/archoera_icon_512.png" \
  "${appdir}/files/share/icons/hicolor/512x512/apps/${APP_ID}.png"

# The launcher needs the network for APIs/downloads, the home directory for
# instances, and read-only access to the host OS so a system Java runtime can
# be launched. Notifications/FileManager are used by the native core.
flatpak build-finish \
  --command="${BIN}" \
  --share=ipc --share=network \
  --socket=x11 --socket=wayland --socket=pulseaudio \
  --device=dri --filesystem=home --filesystem=host-os:ro --filesystem=host-etc:ro \
  --talk-name=org.freedesktop.Notifications \
  --talk-name=org.freedesktop.FileManager1 \
  --talk-name=org.freedesktop.portal.Desktop \
  --talk-name=org.kde.StatusNotifierWatcher \
  "${appdir}"

flatpak build-export --no-update-summary "${repo}" "${appdir}"
flatpak build-bundle "${repo}" \
  "${DIST}/${APP_NAME}-${VERSION}-flatpak-${ARCH}.flatpak" "${APP_ID}"
(
  cd "${DIST}"
  sha256sum "$(basename "${DIST}/${APP_NAME}-${VERSION}-flatpak-${ARCH}.flatpak")" \
    > "$(basename "${DIST}/${APP_NAME}-${VERSION}-flatpak-${ARCH}.flatpak").sha256"
)
echo "Packaged: ${DIST}/${APP_NAME}-${VERSION}-flatpak-${ARCH}.flatpak"
