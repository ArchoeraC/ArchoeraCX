# RPM packaging definition (Fedora / RHEL / openSUSE).
#
# Invoked by packaging/linux/package.sh rpm:
#   rpmbuild -bb --define "stage_path <abs>" --define "app_version <ver>" ...
#
# The payload is the prebuilt Linux bundle. It is built on Ubuntu 24.04
# (glibc 2.39); Fedora/RHEL/openSUSE releases with glibc >= 2.39 can run it.
Name:           archoera-cx
Version:        %{app_version}
Release:        1
Summary:        Open-source Minecraft: Java Edition launcher
License:        AGPL-3.0-only
URL:            https://github.com/BetaStudio2/ArchoeraCX
BuildArch:      x86_64
Group:          Applications/Games
AutoReqProv:    no
%global debug_package %{nil}
Requires:       glibc
Requires:       libstdc++
Requires:       zlib
Requires:       gtk3
Requires:       dbus-libs
Requires:       libepoxy
Requires:       fontconfig
Requires:       fribidi
Requires:       libX11
Requires:       libXi
Requires:       at-spi2-atk
Requires:       libcloudproviders
Requires:       libxkbcommon
Requires:       mesa-libGL
Requires:       libdrm
Requires:       xz-libs

%description
ArchoeraCX manages Minecraft: Java Edition instances, installs
Fabric/Quilt/Forge/NeoForge loaders, signs in with Microsoft, and launches the
game with a native C++/C#/Rust backend.

%prep

%build

%install
mkdir -p %{buildroot}/opt/archoera-cx
cp -a %{stage_path}/bundle/. %{buildroot}/opt/archoera-cx/
mkdir -p %{buildroot}/usr/bin
ln -s ../../opt/archoera-cx/archoera_cx %{buildroot}/usr/bin/archoera_cx
mkdir -p %{buildroot}/usr/share/applications
install -m 644 %{stage_path}/awa.archoera.archoera_cx.desktop \
  %{buildroot}/usr/share/applications/awa.archoera.archoera_cx.desktop
mkdir -p %{buildroot}/usr/share/metainfo
install -m 644 %{stage_path}/awa.archoera.archoera_cx.metainfo.xml \
  %{buildroot}/usr/share/metainfo/awa.archoera.archoera_cx.metainfo.xml
mkdir -p %{buildroot}/usr/share/icons
cp -a %{stage_path}/icons/hicolor %{buildroot}/usr/share/icons/

%files
/opt/archoera-cx
/usr/bin/archoera_cx
/usr/share/applications/awa.archoera.archoera_cx.desktop
/usr/share/metainfo/awa.archoera.archoera_cx.metainfo.xml
/usr/share/icons/hicolor/*/apps/awa.archoera.archoera_cx.png
