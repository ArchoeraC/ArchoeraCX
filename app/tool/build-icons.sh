#!/usr/bin/env bash
# Rebuild the bundled UI icon font and its Dart glyph table.
#
# Run this only when `tool/icons/tabler-icons.txt` changes. Normal builds use
# the committed `assets/fonts/AppIcons.ttf` and need neither network access nor
# fontTools.
set -euo pipefail
cd "$(dirname "$0")/.."
python3 tool/build_icons.py "$@"
if command -v dart >/dev/null 2>&1; then
  dart format lib/theme/icons/app_icon_data.dart >/dev/null
fi
