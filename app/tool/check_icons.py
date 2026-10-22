#!/usr/bin/env python3
"""Verify every `Icons.*` used by the app has a glyph in the built icon font.

Flutter release builds tree-shake `MaterialIcons-Regular.otf` down to the icons
the app references. Two failure modes leave blank squares in the UI:

* an icon constant exists in the SDK's `icons.dart` but its glyph is missing
  from the icon font artifact, and
* the incremental build reuses a stale subset after new icons were added
  (delete `build/flutter_assets` and rebuild).

Run this after `flutter build linux --release` (or the equivalent for the
target platform):

    python3 tool/check_icons.py [bundle-dir]
"""

from __future__ import annotations

import re
import struct
import sys
from pathlib import Path

APP_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_BUNDLE = APP_ROOT / "build/linux/x64/release/bundle/data/flutter_assets"
ICONS_DART_CANDIDATES = [
    Path("/opt/flutter/packages/flutter/lib/src/material/icons.dart"),
]


def find_icons_dart() -> Path | None:
    for candidate in ICONS_DART_CANDIDATES:
        if candidate.exists():
            return candidate
    # Fall back to the SDK next to the `flutter` on PATH.
    for entry in Path("/").glob("**/packages/flutter/lib/src/material/icons.dart"):
        return entry
    return None


def referenced_icons() -> set[str]:
    names: set[str] = set()
    for path in (APP_ROOT / "lib").rglob("*.dart"):
        names.update(
            re.findall(r"\bIcons\.([A-Za-z0-9_]+)", path.read_text(errors="ignore"))
        )
    return names


def codepoints(icons_dart: Path) -> dict[str, int]:
    text = icons_dart.read_text(errors="ignore")
    return {
        match.group(1): int(match.group(2), 16)
        for match in re.finditer(
            r"static const IconData (\w+) = IconData\(\s*(0x[0-9a-fA-F]+)", text
        )
    }


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


def main() -> int:
    bundle = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_BUNDLE
    font = bundle / "fonts/MaterialIcons-Regular.otf"
    icons_dart = find_icons_dart()
    if not font.exists():
        print(f"error: icon font not found: {font}", file=sys.stderr)
        print("build the app first (flutter build linux --release)", file=sys.stderr)
        return 2
    if icons_dart is None:
        print("error: flutter icons.dart not found", file=sys.stderr)
        return 2

    names = referenced_icons()
    points = codepoints(icons_dart)
    available = font_codepoints(font)

    unknown = sorted(name for name in names if name not in points)
    missing = sorted(
        (name, hex(points[name]))
        for name in names
        if name in points and points[name] not in available
    )

    if unknown:
        print("error: icon names not found in the SDK:", ", ".join(unknown))
    for name, code in missing:
        print(f"missing glyph: Icons.{name} ({code})")
    if unknown or missing:
        print(
            "hint: delete build/flutter_assets and rebuild; if the glyph stays "
            "missing the SDK icon font does not ship it — pick another icon.",
            file=sys.stderr,
        )
        return 1

    print(f"icons ok: {len(names)} referenced, all present in {font.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
