#!/usr/bin/env bash
# Package the relocatable Linux Flutter bundle.
#
# Usage:
#   packaging/linux/package.sh [tar|deb|appimage|rpm|arch|nix|all] \
#     <bundle-dir> <version> [arch]
#
# Older callers that pass only <bundle-dir> <version> [arch] still get the
# portable tar.gz, so local notes and scripts keep working. Each format writes
# its archive plus a .sha256 next to it under dist/ (override with DIST_DIR).
#
# `arch` and `nix` only stage a work directory; the workflow runs makepkg /
# nix build in the matching environment. `all` is the portable/distro-neutral
# set (tar.gz + deb + AppImage) used by the Ubuntu job.
#
# The bundle must already contain lib/libarchoera_core.so etc. (tool/build-*.sh
# then `flutter build linux --release`); CI verifies that before calling here.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${HERE}/../.." && pwd)"
APP="${ROOT}/app"
DIST="${DIST_DIR:-${ROOT}/dist}"
DISTRO_TAG="${DISTRO_TAG:-linux}"
DISTRO_NOTE="${DISTRO_NOTE:-}"
BUILD_BASE="${BUILD_BASE:-Ubuntu 24.04}"

FORMAT="tar"
case "${1:-}" in
  tar|deb|appimage|rpm|arch|nix|all)
    FORMAT="$1"
    shift
    ;;
esac

BUNDLE="${1:?usage: package.sh [tar|deb|appimage|rpm|arch|nix|all] <bundle-dir> <version> [arch]}"
VERSION="${2:?missing version}"
ARCH="${3:-x64}"

[[ -d "${BUNDLE}" ]] || { echo "error: bundle not found: ${BUNDLE}" >&2; exit 1; }
BUNDLE="$(cd "${BUNDLE}" && pwd)"
NAME="ArchoeraCX-${VERSION}-linux-${ARCH}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/archoera-linux-package.XXXXXX")"
trap 'rm -rf "${WORK}"' EXIT

# Package-manager version rules differ; the portable archive keeps the full
# Flutter version, while deb accepts '+' (only '-' is the revision separator).
DEB_VERSION="${VERSION}"
DEB_ARCH="amd64"
[[ "${ARCH}" == "arm64" || "${ARCH}" == "aarch64" ]] && DEB_ARCH="arm64"
RPM_VERSION="${VERSION//[+-]/_}"
ARCH_VERSION="${VERSION//[+-]/_}"

mkdir -p "${DIST}" "${WORK}/stage"

# ── Common bundle payload ────────────────────────────────────────────────
# Keep the bundle layout untouched: the C++ core, C# module and Rust helpers
# resolve siblings relative to the executable.
cp -a "${BUNDLE}/." "${WORK}/bundle/"

# upload-artifact does not preserve the executable bit. Restore the three
# programs the launcher relies on so every package built from an artifact
# stays runnable.
for program in archoera_cx lib/archoera-dl lib/archoera-vault; do
  if [[ -f "${WORK}/bundle/${program}" ]]; then
    chmod 0755 "${WORK}/bundle/${program}"
  fi
done

cp "${ROOT}/README.md" "${ROOT}/LICENSE" "${ROOT}/PRIVACY.md" \
  "${ROOT}/THIRD-PARTY-NOTICES.md" "${WORK}/bundle/"
mkdir -p "${WORK}/bundle/licenses"
cp "${APP}/assets/fonts/OFL.txt" \
  "${APP}/assets/licenses/Tabler-Icons-MIT.txt" \
  "${APP}/assets/icons/loaders/LICENSE-APACHE-2.0.txt" \
  "${APP}/assets/icons/loaders/LICENSE-LGPL-2.1.txt" \
  "${WORK}/bundle/licenses/"
install -m 0644 "${APP}/assets/branding/SOURCES.md" \
  "${WORK}/bundle/licenses/branding-SOURCES.md"
install -m 0644 "${APP}/assets/icons/loaders/SOURCES.md" \
  "${WORK}/bundle/licenses/loader-icons-SOURCES.md"
install -m 0644 "${APP}/assets/icons/ui/SOURCES.md" \
  "${WORK}/bundle/licenses/ui-icons-SOURCES.md"

manifest_glibc() {
  local max=""
  if command -v objdump >/dev/null 2>&1; then
    max="$(find "${WORK}/bundle" -type f \
        \( -name '*.so*' -o -perm -u+x \) -print0 2>/dev/null \
      | xargs -0 -r objdump -T 2>/dev/null \
      | grep -o 'GLIBC_[0-9.]*' | sort -V | uniq | tail -1 || true)"
  fi
  printf '%s' "${max:-unknown}"
}

cat > "${WORK}/bundle/BUILD-INFO.txt" <<EOF
ArchoeraCX ${VERSION}
Version   : ${VERSION}
Target    : ${DISTRO_TAG}-${ARCH}
Baseline  : ${BUILD_BASE} (glibc $(manifest_glibc))
Built at  : $(date -u +%Y-%m-%dT%H:%M:%SZ)
Commit    : ${GITHUB_SHA:-$(git -C "${ROOT}" rev-parse --short HEAD 2>/dev/null || echo unknown)}
License   : AGPL-3.0-only (see LICENSE); third-party notices in THIRD-PARTY-NOTICES.md
EOF

stage_desktop() {
  mkdir -p "${WORK}/stage"
  sed "s|@EXEC@|archoera_cx %U|g" "${HERE}/archoera-cx.desktop.in" \
    > "${WORK}/stage/awa.archoera.archoera_cx.desktop"
  sed -e "s|@VERSION@|${VERSION}|g" \
      -e "s|@DATE@|$(date -u +%Y-%m-%d)|g" \
    "${HERE}/awa.archoera.archoera_cx.metainfo.xml.in" \
    > "${WORK}/stage/awa.archoera.archoera_cx.metainfo.xml"
}

stage_icons() {
  for size in 256 512 1024; do
    local icon="${APP}/assets/branding/archoera_icon_${size}.png"
    [[ -f "${icon}" ]] || continue
    install -Dm0644 "${icon}" \
      "${WORK}/stage/icons/hicolor/${size}x${size}/apps/awa.archoera.archoera_cx.png"
  done
}

write_hash() {
  local file="$1"
  (
    cd "${DIST}"
    sha256sum "$(basename "${file}")" > "$(basename "${file}").sha256"
  )
}

persist_workdir() {
  local name="$1"
  local target="${HERE}/work/${name}"
  rm -rf "${target}"
  mkdir -p "${HERE}/work"
  cp -a "${WORK}/${name}" "${target}"
  printf '%s' "${target}"
}

# ── Portable tar.gz ───────────────────────────────────────────────────────
pkg_tar() {
  local dir="${WORK}/tar/${NAME}"
  mkdir -p "${WORK}/tar"
  cp -a "${WORK}/bundle" "${dir}"
  tar -C "${WORK}/tar" --owner=0 --group=0 --numeric-owner \
    -czf "${DIST}/${NAME}.tar.gz" "${NAME}"
  write_hash "${DIST}/${NAME}.tar.gz"
  echo "Packaged: ${DIST}/${NAME}.tar.gz"
}

# ── Debian/Ubuntu .deb ───────────────────────────────────────────────────
pkg_deb() {
  command -v dpkg-deb >/dev/null 2>&1 || {
    echo "error: dpkg-deb is required to build the .deb" >&2
    exit 1
  }

  local root="${WORK}/deb"
  local opt="/opt/archoera-cx"
  local doc="/usr/share/doc/archoera-cx"
  local deb_name="ArchoeraCX-${VERSION}-${DISTRO_TAG}-${ARCH}.deb"
  mkdir -p "${root}${opt}" "${root}/usr/bin" \
    "${root}/usr/share/applications" "${root}/usr/share/metainfo" \
    "${root}/usr/share/icons/hicolor" "${root}${doc}/licenses" \
    "${root}/DEBIAN"

  cp -a "${WORK}/bundle/." "${root}${opt}/"
  ln -s "${opt}/archoera_cx" "${root}/usr/bin/archoera_cx"

  stage_desktop
  install -m 0644 "${WORK}/stage/awa.archoera.archoera_cx.desktop" \
    "${root}/usr/share/applications/awa.archoera.archoera_cx.desktop"
  install -m 0644 "${WORK}/stage/awa.archoera.archoera_cx.metainfo.xml" \
    "${root}/usr/share/metainfo/awa.archoera.archoera_cx.metainfo.xml"

  stage_icons
  cp -a "${WORK}/stage/icons/hicolor/." "${root}/usr/share/icons/hicolor/"

  install -m 0644 "${ROOT}/LICENSE" "${root}${doc}/copyright"
  install -m 0644 "${ROOT}/README.md" "${ROOT}/PRIVACY.md" \
    "${ROOT}/THIRD-PARTY-NOTICES.md" "${root}${doc}/"
  cp -a "${WORK}/bundle/licenses/." "${root}${doc}/licenses/"

  local installed_size
  installed_size="$(du -sk "${root}/opt" | awk '{print $1}')"

  # DEBIAN/control is a dpkg format file; comments are not allowed inside it.
  cat > "${root}/DEBIAN/control" <<EOF
Package: archoera-cx
Version: ${DEB_VERSION}
Section: games
Priority: optional
Architecture: ${DEB_ARCH}
Maintainer: BetaStudio2 (ArchoeraCX) <noreply@github.com>
Homepage: https://github.com/BetaStudio2/ArchoeraCX
Installed-Size: ${installed_size}
Depends: libc6, libstdc++6, zlib1g, libgtk-3-0 | libgtk-3-0t64, libdbus-1-3, libepoxy0, libfontconfig1, libfribidi0, libx11-6, libxi6, liblzma5
Description: Open-source Minecraft: Java Edition launcher
 ArchoeraCX manages Minecraft: Java Edition instances, installs
 Fabric/Quilt/Forge/NeoForge loaders, signs in with Microsoft, and launches
 the game with a native C++/C#/Rust backend.
EOF
  if [[ -n "${DISTRO_NOTE}" ]]; then
    printf ' Built for: %s\n' "${DISTRO_NOTE}" >> "${root}/DEBIAN/control"
  fi

  cat > "${root}/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
fi
exit 0
EOF
  chmod 0755 "${root}/DEBIAN/postinst"
  cp "${root}/DEBIAN/postinst" "${root}/DEBIAN/postrm"

  dpkg-deb --root-owner-group --build "${root}" \
    "${DIST}/${deb_name}" >/dev/null
  write_hash "${DIST}/${deb_name}"
  echo "Packaged: ${DIST}/${deb_name}"
}

# ── AppImage (portable) ──────────────────────────────────────────────────
pkg_appimage() {
  command -v appimagetool >/dev/null 2>&1 || {
    echo "error: appimagetool is required to build the AppImage" >&2
    exit 1
  }

  local appdir="${WORK}/AppDir"
  mkdir -p "${appdir}/usr/bin" "${appdir}/usr/share/applications" \
    "${appdir}/usr/share/metainfo" \
    "${appdir}/usr/share/icons/hicolor/256x256/apps"

  cp -a "${WORK}/bundle/." "${appdir}/usr/bin/"
  chmod 0755 "${appdir}/usr/bin/archoera_cx"

  stage_desktop
  install -m 0644 "${WORK}/stage/awa.archoera.archoera_cx.desktop" \
    "${appdir}/usr/share/applications/awa.archoera.archoera_cx.desktop"
  install -m 0644 "${WORK}/stage/awa.archoera.archoera_cx.metainfo.xml" \
    "${appdir}/usr/share/metainfo/awa.archoera.archoera_cx.appdata.xml"
  install -m 0644 "${APP}/assets/branding/archoera_icon_256.png" \
    "${appdir}/usr/share/icons/hicolor/256x256/apps/awa.archoera.archoera_cx.png"
  cp "${APP}/assets/branding/archoera_icon_256.png" \
    "${appdir}/awa.archoera.archoera_cx.png"
  ln -sf awa.archoera.archoera_cx.png "${appdir}/.DirIcon"
  cp "${appdir}/usr/share/applications/awa.archoera.archoera_cx.desktop" \
    "${appdir}/awa.archoera.archoera_cx.desktop"

  cat > "${appdir}/AppRun" <<'EOF'
#!/bin/sh
set -e
HERE="$(dirname "$(readlink -f "$0")")"
# linuxdeploy puts the collected GTK/X11 closure in usr/lib; the bundled
# libflutter_linux_gtk.so keeps its own $ORIGIN runpath, so extend the search
# path here for the AppImage case.
export LD_LIBRARY_PATH="$HERE/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec "$HERE/usr/bin/archoera_cx" "$@"
EOF
  chmod 0755 "${appdir}/AppRun"

  # linuxdeploy collects the GTK/X11 closure and rewrites RUNPATH. It is
  # optional locally, but CI supplies it so the image runs on more than the
  # build machine's distribution.
  if [[ -n "${LINUXDEPLOY:-}" && -x "${LINUXDEPLOY}" ]]; then
    local lib_args=()
    for lib in \
      "${appdir}/usr/bin/lib/libarchoera_core.so" \
      "${appdir}/usr/bin/lib/archoera_cs.so" \
      "${appdir}/usr/bin/lib/libarchoera_dl.so"; do
      [[ -f "${lib}" ]] && lib_args+=(--library "${lib}")
    done
    "${LINUXDEPLOY}" --appdir "${appdir}" \
      --executable "${appdir}/usr/bin/archoera_cx" \
      "${lib_args[@]}" \
      --desktop-file "${appdir}/usr/share/applications/awa.archoera.archoera_cx.desktop" \
      --icon-file "${appdir}/usr/share/icons/hicolor/256x256/apps/awa.archoera.archoera_cx.png"
  else
    echo "warning: LINUXDEPLOY not set; AppImage keeps the host's GTK/X11 ABI" >&2
  fi

  local runtime_args=()
  if [[ -n "${APPIMAGE_RUNTIME_FILE:-}" && -f "${APPIMAGE_RUNTIME_FILE}" ]]; then
    runtime_args=(--runtime-file "${APPIMAGE_RUNTIME_FILE}")
  fi

  # -n: AppStream validation fetches the homepage/bugtracker URLs and fails
  # the package on a slow network. The metadata is still shipped; only the
  # network-dependent check is skipped.
  ARCH="${ARCH}" appimagetool --appimage-extract-and-run --no-appstream \
    "${runtime_args[@]}" "${appdir}" "${DIST}/${NAME}.AppImage"
  write_hash "${DIST}/${NAME}.AppImage"
  echo "Packaged: ${DIST}/${NAME}.AppImage"
}

# ── Fedora/RHEL/openSUSE .rpm ────────────────────────────────────────────
pkg_rpm() {
  command -v rpmbuild >/dev/null 2>&1 || {
    echo "error: rpmbuild is required to build the .rpm" >&2
    exit 1
  }

  local topdir="${WORK}/rpmbuild"
  local stage="${WORK}/rpm-stage"
  local rpm_name="ArchoeraCX-${VERSION}-${DISTRO_TAG}-${ARCH}.rpm"
  mkdir -p "${topdir}"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}
  mkdir -p "${stage}"
  cp -a "${WORK}/bundle" "${stage}/bundle"
  stage_desktop
  stage_icons
  cp "${WORK}/stage/awa.archoera.archoera_cx.desktop" "${stage}/"
  cp "${WORK}/stage/awa.archoera.archoera_cx.metainfo.xml" "${stage}/"
  cp -a "${WORK}/stage/icons" "${stage}/icons"
  cp "${HERE}/archoera-cx.spec" "${topdir}/SPECS/"

  rpmbuild --define "_topdir ${topdir}" \
    --define "stage_path ${stage}" \
    --define "app_version ${RPM_VERSION}" \
    --define "_rpmfilename %{NAME}-%{VERSION}-%{RELEASE}.%{ARCH}.rpm" \
    -bb "${topdir}/SPECS/archoera-cx.spec"

  local src
  src="$(find "${topdir}/RPMS" -name '*.rpm' \
    ! -name '*-debuginfo-*' ! -name '*-debugsource-*' | head -1)"
  [[ -n "${src}" ]] || { echo "error: rpmbuild produced no package" >&2; exit 1; }
  cp "${src}" "${DIST}/${rpm_name}"
  write_hash "${DIST}/${rpm_name}"
  echo "Packaged: ${DIST}/${rpm_name}"
}

# ── Arch Linux pkg.tar.zst (makepkg runs in the workflow) ────────────────
pkg_arch() {
  local dir="${WORK}/arch"
  mkdir -p "${dir}"
  tar --zstd -C "${WORK}" -cf "${dir}/bundle.tar.zst" bundle
  cp "${HERE}/PKGBUILD" "${dir}/PKGBUILD"
  sed -i "s|^pkgver=.*|pkgver=${ARCH_VERSION}|" "${dir}/PKGBUILD"
  stage_desktop
  stage_icons
  cp "${WORK}/stage/awa.archoera.archoera_cx.desktop" "${dir}/archoera-cx.desktop"
  cp "${WORK}/stage/awa.archoera.archoera_cx.metainfo.xml" "${dir}/awa.archoera.archoera_cx.metainfo.xml"
  cp "${APP}/assets/branding/archoera_icon_512.png" "${dir}/awa.archoera.archoera_cx.png"
  local target
  target="$(persist_workdir arch)"
  echo "Staged: ${target} (workflow runs makepkg -f --skipinteg --nocheck --nodeps)"
}

# ── NixOS package (nix build runs in the workflow) ───────────────────────
pkg_nix() {
  local dir="${WORK}/nix"
  mkdir -p "${dir}"
  cp "${HERE}/nix/flake.nix" "${dir}/flake.nix"
  sed "s|@VERSION@|${VERSION}|g" "${HERE}/nix/package.nix" > "${dir}/package.nix"
  cp -a "${WORK}/bundle" "${dir}/bundle"
  stage_desktop
  cp "${WORK}/stage/awa.archoera.archoera_cx.desktop" "${dir}/archoera-cx.desktop"
  cp "${WORK}/stage/awa.archoera.archoera_cx.metainfo.xml" "${dir}/awa.archoera.archoera_cx.metainfo.xml"
  cp "${APP}/assets/branding/archoera_icon_512.png" "${dir}/awa.archoera.archoera_cx.png"
  local target
  target="$(persist_workdir nix)"
  echo "Staged: ${target} (workflow runs nix build path:$target#default)"
}

case "${FORMAT}" in
  tar) pkg_tar ;;
  deb) pkg_deb ;;
  appimage) pkg_appimage ;;
  rpm) pkg_rpm ;;
  arch) pkg_arch ;;
  nix) pkg_nix ;;
  all)
    pkg_tar
    pkg_deb
    pkg_appimage
    ;;
  *)
    echo "error: unknown format ${FORMAT}" >&2
    exit 2
    ;;
esac

if [[ "${FORMAT}" != "arch" && "${FORMAT}" != "nix" ]]; then
  ls -lh "${DIST}"/"${NAME}"* "${DIST}"/"ArchoeraCX-${VERSION}-${DISTRO_TAG}-${ARCH}"* 2>/dev/null || true
fi
