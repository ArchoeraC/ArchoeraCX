# UI icon sources

The app UI uses a single outline icon set, cut down to the glyphs the launcher
actually references and bundled as `assets/fonts/AppIcons.ttf`.

| Item | Value |
|---|---|
| Upstream | [tabler/tabler-icons](https://github.com/tabler/tabler-icons) `@tabler/icons-webfont` **3.49.0** |
| License | MIT — full text in [`assets/licenses/Tabler-Icons-MIT.txt`](../../licenses/Tabler-Icons-MIT.txt) |
| Font family | `AppIcons` (declared in `app/pubspec.yaml`) |
| Glyph list | [`app/tool/icons/tabler-icons.txt`](../../../tool/icons/tabler-icons.txt) |
| Generator | [`app/tool/build_icons.py`](../../../tool/build_icons.py) via `./tool/build-icons.sh` |
| Semantic layer | [`app/lib/theme/icons/app_icons.dart`](../../../lib/theme/icons/app_icons.dart) |

## Why a bundled subset

- **Only the artifact is versioned**: `assets/fonts/AppIcons.ttf` (≈24 KiB) and
  the generated `lib/theme/icons/app_icon_data.dart` are committed. The upstream
  package, webfont, extracted SVGs and the generator venv are ignored
  (`app/.cache/`, `app/tool/icons/*.ttf|*.tgz`, `app/assets/icons/ui/*.svg`) —
  the repository never carries a third-party icon set.
- **Offline / China-friendly**: the font is committed, so neither `flutter pub
  get` nor the build touches GitHub, jsDelivr or npmmirror at runtime.
- **Stable**: glyph codepoints are pinned in `tabler-icons.txt`; an upstream
  release bump is a deliberate, reviewable change.
- **Small**: ~24 KiB instead of the ~1.8 MiB upstream webfont; unused glyphs are
  never shipped.
- **Semantic**: pages reference `AppIcons.download` (intent), not a library
  constant, so the visual set can be swapped without touching UI code.

## Adding or replacing an icon

1. Find the icon name and codepoint in the pinned release
   (`@tabler/icons-webfont@3.49.0/dist/tabler-icons.css`).
2. Append `<name>\t<codepoint>` to `app/tool/icons/tabler-icons.txt`.
3. Run `./tool/build-icons.sh` (downloads the pinned webfont into
   `app/.cache/icons/` on first use, bootstraps fontTools there).
4. Expose an intent-based constant in `lib/theme/icons/app_icons.dart`.
5. Run `python3 tool/check_icons.py`.

Only outline glyphs are used. Filled variants, brand logos and loader marks stay
out of this set: loader identities live in `assets/icons/loaders/` and keep their
own licenses.

## Attribution

Tabler Icons — MIT, © 2020-2026 Paweł Kuna. The license text is redistributed
with the app in `assets/licenses/Tabler-Icons-MIT.txt`, registered into the
in-app license page by `lib/app/licenses.dart`, and must stay alongside
`assets/fonts/AppIcons.ttf`.

Special thanks: the launcher's entire outline icon language comes from this set.
If it saves you time, star <https://github.com/tabler/tabler-icons>.
