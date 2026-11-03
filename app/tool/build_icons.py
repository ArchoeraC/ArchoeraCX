#!/usr/bin/env python3
"""Build the bundled ArchoeraCX UI icon font.

The app does not depend on an icon package at runtime. A curated subset of
Tabler Icons is cut from the pinned upstream release and committed as
`assets/fonts/AppIcons.ttf`, so every build (and every clone) works offline.

The set is driven by `tool/icons/tabler-icons.txt`. Add a glyph there, rerun
this script, then reference the generated `AppIconData` constant from
`lib/theme/icons/app_icons.dart`.

Only needed when the icon set changes:

    ./tool/build-icons.sh

Requirements: python3 with `venv` support. The first run downloads the pinned
Tabler webfont release (npmmirror first, jsDelivr as fallback) into
`app/.cache/icons/` and bootstraps fontTools inside `app/.cache/icons/venv`.
Neither is committed, and neither is touched by normal builds.
"""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
from pathlib import Path

APP_ROOT = Path(__file__).resolve().parent.parent
MANIFEST = APP_ROOT / "tool/icons/tabler-icons.txt"
FONT_OUT = APP_ROOT / "assets/fonts/AppIcons.ttf"
DART_OUT = APP_ROOT / "lib/theme/icons/app_icon_data.dart"
CACHE = APP_ROOT / ".cache/icons"

VERSION = "3.49.0"
SOURCE_SHA256 = "d291676dc4c28b386552753bf16cf2bef0e93e5543f8e0b5c5d72c0da41ae1bc"
SOURCE_URLS = (
    f"https://registry.npmmirror.com/@tabler/icons-webfont/-/icons-webfont-{VERSION}.tgz",
    f"https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@{VERSION}/dist/fonts/tabler-icons.ttf",
)
TARBALL_MEMBER = "package/dist/fonts/tabler-icons.ttf"

DART_HEADER = """\
// GENERATED FILE — do not edit by hand.
//
// Source: Tabler Icons {version} — MIT — https://github.com/tabler/tabler-icons
// Font:   assets/fonts/AppIcons.ttf (curated subset)
// Rebuild: app/tool/build-icons.sh, driven by app/tool/icons/tabler-icons.txt
//
// Pages must not reference this class directly; use `AppIcons` instead.

import 'package:flutter/widgets.dart';

/// Raw glyphs of the bundled UI icon font.
abstract final class AppIconData {{
  static const String fontFamily = 'AppIcons';
"""


def camel(name: str) -> str:
    head, *rest = name.split("-")
    return head + "".join(part[:1].upper() + part[1:] for part in rest)


def read_manifest() -> list[tuple[str, int]]:
    entries: list[tuple[str, int]] = []
    for line in MANIFEST.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        name, code = line.split()
        entries.append((name, int(code, 16)))
    if not entries:
        raise SystemExit(f"error: no glyphs listed in {MANIFEST}")
    seen: dict[int, str] = {}
    for name, code in entries:
        if code in seen:
            raise SystemExit(f"error: {name} reuses {seen[code]}'s codepoint")
        seen[code] = name
    return entries


def download(url: str, target: Path) -> None:
    print(f"  fetch {url}")
    with urllib.request.urlopen(url, timeout=120) as response:
        data = response.read()
    if url.endswith(".tgz"):
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / "icons.tgz"
            archive.write_bytes(data)
            with tarfile.open(archive) as tar:
                member = tar.extractfile(TARBALL_MEMBER)
                if member is None:
                    raise SystemExit(f"error: {TARBALL_MEMBER} missing from {url}")
                data = member.read()
    target.write_bytes(data)


def source_font() -> Path:
    target = CACHE / f"tabler-icons-{VERSION}.ttf"
    if target.exists() and sha256(target) == SOURCE_SHA256:
        return target
    CACHE.mkdir(parents=True, exist_ok=True)
    for url in SOURCE_URLS:
        try:
            download(url, target)
        except Exception as exc:  # noqa: BLE001 - fall through to the next mirror
            print(f"  failed: {exc}")
            continue
        if sha256(target) == SOURCE_SHA256:
            return target
        print("  failed: checksum mismatch")
        target.unlink(missing_ok=True)
    raise SystemExit(
        "error: could not download a verified Tabler webfont release.\n"
        f"       expected sha256 {SOURCE_SHA256}\n"
        f"       place it at {target} manually and rerun."
    )


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def subsetter() -> str:
    override = os.environ.get("PYFTSUBSET")
    if override:
        return override
    venv = CACHE / "venv"
    binary = venv / "bin/pyftsubset"
    if not binary.exists():
        print("  bootstrapping fontTools into .cache/icons/venv")
        CACHE.mkdir(parents=True, exist_ok=True)
        try:
            subprocess.run([sys.executable, "-m", "venv", str(venv)], check=True)
            subprocess.run(
                [str(venv / "bin/pip"), "install", "--quiet", "fonttools"],
                check=True,
            )
        except (OSError, subprocess.CalledProcessError) as exc:
            raise SystemExit(
                "error: cannot bootstrap fontTools.\n"
                f"       {exc}\n"
                "       install python3-venv, or set PYFTSUBSET to an existing\n"
                "       pyftsubset binary."
            ) from exc
    return str(binary)


def build_font(entries: list[tuple[str, int]]) -> None:
    binary = subsetter()
    source = source_font()
    FONT_OUT.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False) as handle:
        handle.write(",".join(f"U+{code:04X}" for _, code in entries))
        unicodes = handle.name
    try:
        print(f"  subset {len(entries)} glyphs -> {FONT_OUT.relative_to(APP_ROOT)}")
        subprocess.run(
            [
                binary,
                str(source),
                f"--unicodes-file={unicodes}",
                f"--output-file={FONT_OUT}",
                "--no-hinting",
                "--desubroutinize",
                "--name-IDs=*",
                "--recalc-bounds",
            ],
            check=True,
        )
        recenter_glyphs(FONT_OUT, binary)
    finally:
        Path(unicodes).unlink(missing_ok=True)


# The upstream webfont draws every glyph flush against the left edge of its
# em box (plus spans [0, 666] of a 1000-unit advance, check [4, 712]). CSS
# icon usage centers the text box itself so the bias goes unnoticed, but
# Skia paints the outline at the pen position, so in Flutter the ink lands
# ~15% left of its box — visible on badges like the accent-swatch check.
# Re-center each glyph on its advance width so the ink centers in its box.
_RECENTER = r"""
import sys

from fontTools.misc.transform import Transform
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

path = sys.argv[1]
font = TTFont(path)
glyph_set = font.getGlyphSet()
hmtx = font["hmtx"]
moved = 0
for name in font.getGlyphOrder():
    pen = BoundsPen(glyph_set)
    glyph_set[name].draw(pen)
    if pen.bounds is None:
        continue
    xmin, _, xmax, _ = pen.bounds
    advance, _ = hmtx[name]
    ink_width = xmax - xmin
    # Re-center the outline on the advance and mirror that offset in hmtx:
    # renderers that paint the outline at the pen (FreeType) and renderers
    # that align the bitmap's left edge to pen + lsb (Skia) then both center
    # the ink inside the glyph's box.
    delta = advance / 2 - (xmin + xmax) / 2
    if abs(delta) >= 0.5:
        rebuild = TTGlyphPen(glyph_set)
        glyph_set[name].draw(TransformPen(rebuild, Transform(1, 0, 0, 1, delta, 0)))
        font["glyf"][name] = rebuild.glyph()
        moved += 1
    new_xmin = xmin + delta if abs(delta) >= 0.5 else xmin
    hmtx[name] = (advance, round(new_xmin))
font.save(path)
print(f"  recentered {moved} glyphs on their advance")
"""


def recenter_glyphs(font_path: Path, subset_binary: str) -> None:
    python = Path(subset_binary).with_name("python")
    runner = python if python.exists() else Path(sys.executable)
    subprocess.run([str(runner), "-c", _RECENTER, str(font_path)], check=True)


def validate(entries: list[tuple[str, int]]) -> None:
    sys.path.insert(0, str(APP_ROOT / "tool"))
    from check_icons import font_codepoints  # local helper, stdlib only

    available = font_codepoints(FONT_OUT)
    missing = [name for name, code in entries if code not in available]
    if missing:
        raise SystemExit("error: subset font is missing " + ", ".join(missing))
    extra = available - {code for _, code in entries}
    if extra:
        print(f"  note: {len(extra)} unexpected codepoints in the subset")


def write_dart(entries: list[tuple[str, int]]) -> None:
    DART_OUT.parent.mkdir(parents=True, exist_ok=True)
    names = [camel(name) for name, _ in entries]
    if len(set(names)) != len(names):
        raise SystemExit("error: two glyphs collapse to the same Dart identifier")
    lines = [DART_HEADER.format(version=VERSION)]
    for (source_name, code), name in zip(entries, names):
        lines.append(
            f"\n  /// `{source_name}`\n"
            f"  static const IconData {name} = "
            f"IconData(0x{code:04x}, fontFamily: fontFamily);"
        )
    lines.append("\n}\n")
    DART_OUT.write_text("".join(lines), encoding="utf-8")
    print(f"  wrote {DART_OUT.relative_to(APP_ROOT)} ({len(entries)} glyphs)")


def main() -> int:
    entries = read_manifest()
    print(f"icons: {len(entries)} glyphs from Tabler Icons {VERSION}")
    build_font(entries)
    validate(entries)
    write_dart(entries)
    size = FONT_OUT.stat().st_size
    print(f"  font size {size / 1024:.1f} KiB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
