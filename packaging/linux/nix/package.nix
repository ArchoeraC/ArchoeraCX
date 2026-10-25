# NixOS package definition for the prebuilt ArchoeraCX bundle.
#
# package.sh nix substitutes @VERSION@ and places this file next to:
#   bundle/                            (complete release bundle)
#   archoera-cx.desktop
#   awa.archoera.archoera_cx.metainfo.xml
#   awa.archoera.archoera_cx.png
#
# The bundle is prebuilt on Ubuntu 24.04. autoPatchelfHook sets the NixOS
# interpreter and adds store RUNPATHs for GTK/X11/dbus. The bundle's internal
# $ORIGIN lookups are re-added in postFixup because the hook replaces RUNPATHs.
{ lib
, stdenv
, autoPatchelfHook
, patchelf
, gtk3
, dbus
, libepoxy
, fontconfig
, fribidi
, libX11
, libXi
, at-spi2-atk
, libcloudproviders
, libxkbcommon
, wayland
, libGL
, libdrm
, zlib
, glibc
}:

stdenv.mkDerivation (finalAttrs: {
  pname = "archoera-cx";
  version = "@VERSION@";

  src = ./bundle;

  nativeBuildInputs = [
    autoPatchelfHook
    patchelf
  ];

  buildInputs = [
    gtk3
    dbus
    libepoxy
    fontconfig
    fribidi
    libX11
    libXi
    at-spi2-atk
    libcloudproviders
    libxkbcommon
    wayland
    libGL
    libdrm
    zlib
    glibc
    stdenv.cc.cc.lib
  ];

  # These libraries are part of the bundle and resolve through $ORIGIN.
  autoPatchelfIgnoreMissingDeps = [
    "libflutter_linux_gtk.so"
    "libfile_selector_linux_plugin.so"
    "libscreen_retriever_linux_plugin.so"
    "liburl_launcher_linux_plugin.so"
    "libwindow_manager_plugin.so"
    "libarchoera_core.so"
    "libarchoera_dl.so"
    "archoera_cs.so"
    # libdartjni.so is the optional Flutter JNI bridge. It links libjvm.so,
    # but this launcher launches an external JVM and never loads that bridge.
    "libjvm.so"
  ];

  installPhase = ''
    runHook preInstall
    mkdir -p $out/lib/archoera-cx $out/bin
    cp -a $src/. $out/lib/archoera-cx/
    chmod -R u+w $out/lib/archoera-cx
    chmod 0755 $out/lib/archoera-cx/archoera_cx
    chmod 0755 $out/lib/archoera-cx/lib/archoera-dl
    chmod 0755 $out/lib/archoera-cx/lib/archoera-vault
    ln -s $out/lib/archoera-cx/archoera_cx $out/bin/archoera_cx

    install -Dm644 ${./archoera-cx.desktop} \
      $out/share/applications/awa.archoera.archoera_cx.desktop
    install -Dm644 ${./awa.archoera.archoera_cx.metainfo.xml} \
      $out/share/metainfo/awa.archoera.archoera_cx.metainfo.xml
    install -Dm644 ${./awa.archoera.archoera_cx.png} \
      $out/share/icons/hicolor/512x512/apps/awa.archoera.archoera_cx.png
    runHook postInstall
  '';

  # autoPatchelfHook runs in fixupPhase and may replace the bundle's RUNPATHs.
  # Restore the relative lookups after it has added the Nix store paths.
  postFixup = ''
    patchelf --add-rpath '$ORIGIN/lib' $out/lib/archoera-cx/archoera_cx
    find $out/lib/archoera-cx/lib -type f -name '*.so*' \
      -exec patchelf --add-rpath '$ORIGIN' {} \;
  '';

  meta = {
    description = "Open-source Minecraft: Java Edition launcher";
    homepage = "https://github.com/BetaStudio2/ArchoeraCX";
    license = lib.licenses.agpl3Only;
    mainProgram = "archoera_cx";
    platforms = [ "x86_64-linux" ];
  };
})
