#!/usr/bin/env python3
"""Verify the bundled icon font covers every icon the UI references.

ArchoeraCX ships its own icon font (`assets/fonts/AppIcons.ttf`), a curated
Tabler Icons subset built by `tool/build-icons.sh`. Two failure modes leave
blank squares in the UI:

* a glyph was added to `lib/theme/icons/app_icons.dart` without rebuilding the
  font, and
* a release build tree-shook a glyph that the app still references.

Run it straight after editing the icon layer (no build required) or after
`flutter build linux --release` to also check the tree-shaken artifact:

    python3 tool/check_icons.py [bundle-dir]

The optional bundle directory defaults to
`build/linux/x64/release/bundle/data/flutter_assets`.
"""

from __future__ import annotations

import re
import struct
import sys
from pathlib import Path

APP_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_BUNDLE = APP_ROOT / "build/linux/x64/release/bundle/data/flutter_assets"
LIB = APP_ROOT / "lib"
FONT = APP_ROOT / "assets/fonts/AppIcons.ttf"
GLYPHS_DART = LIB / "theme/icons/app_icon_data.dart"
ICONS_DART = LIB / "theme/icons/app_icons.dart"


def font_codepoints(font: Path) -> set[int]:
    data = font.read_bytes()
    table_count = struct.unpack(">H", data[4:6])[0]
    tables: dict[str, tuple[int, int]] = {}
    for index in range(table_count):
        offset = 12 + index * 16
        tag = data[offset : offset + 4].decode("latin1")
        start, length = struct.unpack(">II", data[offset + 8 : offset + 16])
        tables[tag] = (start, length)

    cmap_start, _ = tables["cmap"]
    subtables = struct.unpack(">H", data[cmap_start + 2 : cmap_start + 4])[0]
    found: set[int] = set()
    for index in range(subtables):
        record = cmap_start + 4 + index * 8
        _, _, offset = struct.unpack(">HHI", data[record : record + 8])
        table = cmap_start + offset
        fmt = struct.unpack(">H", data[table : table + 2])[0]
        if fmt == 4:
            seg_x2 = struct.unpack(">H", data[table + 6 : table + 8])[0]
            segments = seg_x2 // 2
            ends = struct.unpack(
                ">%dH" % segments, data[table + 14 : table + 14 + seg_x2]
            )
            starts = struct.unpack(
                ">%dH" % segments,
                data[table + 16 + seg_x2 : table + 16 + 2 * seg_x2],
            )
            deltas = struct.unpack(
                ">%dh" % segments,
                data[table + 16 + 2 * seg_x2 : table + 16 + 3 * seg_x2],
            )
            range_offsets_at = table + 16 + 3 * seg_x2
            for seg in range(segments):
                start, end = starts[seg], ends[seg]
                if start == 0xFFFF:
                    continue
                delta = deltas[seg]
                ro = struct.unpack(
                    ">H", data[range_offsets_at + seg * 2 : range_offsets_at + seg * 2 + 2]
                )[0]
                for code in range(start, end + 1):
                    if ro == 0:
                        glyph = (code + delta) & 0xFFFF
                    else:
                        addr = range_offsets_at + seg * 2 + ro + (code - start) * 2
                        glyph = struct.unpack(">H", data[addr : addr + 2])[0]
                        if glyph:
                            glyph = (glyph + delta) & 0xFFFF
                    if glyph:
                        found.add(code)
        elif fmt == 12:
            groups = struct.unpack(">I", data[table + 12 : table + 16])[0]
            for group in range(groups):
                item = table + 16 + group * 12
                start, end, glyph = struct.unpack(">III", data[item : item + 12])
                if glyph == 0:
                    continue
                found.update(range(start, end + 1))
    return found


def glyph_codepoints() -> dict[str, int]:
    text = GLYPHS_DART.read_text(errors="ignore")
    return {
        match.group(1): int(match.group(2), 16)
        for match in re.finditer(
            r"static const IconData (\w+)\s*=\s*IconData\(\s*(0x[0-9a-fA-F]+)", text
        )
    }


def registered_names() -> dict[str, str]:
    text = ICONS_DART.read_text(errors="ignore")
    return {
        match.group(1): match.group(2)
        for match in re.finditer(
            r"static const IconData (\w+)\s*=\s*AppIconData\.(\w+);", text
        )
    }


def referenced_icons() -> set[str]:
    names: set[str] = set()
    for path in LIB.rglob("*.dart"):
        if path.parent == ICONS_DART.parent:
            continue
        names.update(
            re.findall(r"\bAppIcons\.([A-Za-z0-9_]+)", path.read_text(errors="ignore"))
        )
    return names


def check(font: Path, *, label: str) -> list[str]:
    glyphs = glyph_codepoints()
    registered = registered_names()
    referenced = referenced_icons()

    problems = [
        f"undefined constant: AppIcons.{name}"
        for name in sorted(referenced - registered.keys())
    ]
    problems += [
        f"unknown glyph: AppIcons.{name} -> AppIconData.{glyph}"
        for name, glyph in sorted(registered.items())
        if name in referenced and glyph not in glyphs
    ]

    available = font_codepoints(font)
    problems += [
        f"missing glyph in {label}: {glyph} (0x{code:04x})"
        for glyph, code in sorted(glyphs.items())
        if code not in available
    ]
    return problems


def main() -> int:
    if not FONT.exists():
        print(f"error: icon font not found: {FONT}", file=sys.stderr)
        print("run tool/build-icons.sh", file=sys.stderr)
        return 2
    if not GLYPHS_DART.exists() or not ICONS_DART.exists():
        print("error: icon layer files are missing", file=sys.stderr)
        return 2

    used = len(referenced_icons())
    problems = check(FONT, label=FONT.name)

    bundle = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_BUNDLE
    bundled = next(
        (
            candidate
            for candidate in (
                bundle / "assets/fonts/AppIcons.ttf",
                bundle / "fonts/AppIcons.ttf",
            )
            if candidate.exists()
        ),
        None,
    )
    if bundled is not None:
        problems += check(bundled, label=f"{bundled.name} (bundled)")

    if problems:
        for problem in problems:
            print(f"error: {problem}")
        print(
            "hint: add the glyph to tool/icons/tabler-icons.txt and run "
            "tool/build-icons.sh; for a stale bundle delete build/flutter_assets.",
            file=sys.stderr,
        )
        return 1

    print(f"icons ok: {used} referenced, all present in {FONT.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
