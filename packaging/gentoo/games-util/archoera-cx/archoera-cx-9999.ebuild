# Copyright 1999-2026 Gentoo Authors
# Distributed under the terms of the GNU General Public License v2

# Gentoo ebuild for the prebuilt release tarball.
# Place in an overlay as games-util/archoera-cx/archoera-cx-9999.ebuild, or
# rename it to the release version (for example archoera-cx-0.0.1.ebuild).
EAPI=8

DESCRIPTION="Open-source Minecraft: Java Edition launcher"
HOMEPAGE="https://github.com/BetaStudio2/ArchoeraCX"
SRC_URI="https://github.com/BetaStudio2/ArchoeraCX/releases/download/v${PV}/ArchoeraCX-${PV}-linux-x64.tar.gz"

LICENSE="AGPL-3"
SLOT="0"
KEYWORDS="~amd64"
RESTRICT="primaryuri strip"

RDEPEND="
	>=x11-libs/gtk+-3.24:3
	dev-libs/glib:2
	dev-libs/dbus-glib
	dev-libs/libepoxy
	media-libs/fontconfig
	media-libs/fribidi
	x11-libs/libX11
	x11-libs/libXi
	x11-libs/libxkbcommon
	media-libs/mesa
	x11-libs/libdrm
	sys-libs/zlib
	app-arch/xz-utils
"

S="${WORKDIR}/ArchoeraCX-${PV}-linux-x64"

src_install() {
	insinto /opt/archoera-cx
	doins -r "${S}"/.
	fperms 0755 /opt/archoera-cx/archoera_cx
	fperms 0755 /opt/archoera-cx/lib/archoera-dl
	fperms 0755 /opt/archoera-cx/lib/archoera-vault
	dosym /opt/archoera-cx/archoera_cx /usr/bin/archoera_cx

	doicon -s 256 "${S}/data/archoera/app_icon.png"
	newmenu - <<-EOF
		[Desktop Entry]
		Type=Application
		Name=ArchoeraCX
		GenericName=Minecraft Launcher
		Comment=Open-source Minecraft: Java Edition launcher
		Exec=/usr/bin/archoera_cx %U
		Icon=awa.archoera.archoera_cx
		Terminal=false
		Categories=Game;
		StartupWMClass=archoera_cx
	EOF
}
