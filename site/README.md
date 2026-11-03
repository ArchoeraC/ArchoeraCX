# ArchoeraCX web presence

Static introduction page for the launcher. No build step, no package manager, no
network requests: open `site/index.html` directly, or serve the folder.

```bash
cd site
python3 -m http.server 8000
# http://localhost:8000/
```

## Design language

The page is built from the launcher's own viewfinder mark, not a generic landing
page grammar:

- four corner brackets frame the hero and the system map;
- the screenshot, contact sheet and terminal use the same panel silhouette as
  the launcher: a 22 px cut at the top-right and 8 px at the bottom-left;
- a hairline spine runs down the left margin and connects diamond nodes;
- the page scrollbar is hidden; wheel, keys and touch still scroll, and the
  paper or ink edge stays continuous;
- the diamond core from the application icon marks sections and the C++
  architecture hub;
- mono annotations, rulers and contact-sheet captions carry the technical tone;
- the page has the launcher's light and dark token sets; the build block uses
  the primary blue and the footer keeps the deeper ink frame in both themes.

The site follows the OS theme until a choice is made; the choice is saved in
`localStorage` and mirrored in a first-party cookie, so a refresh keeps it even
when storage is blocked. The topbar icon shows the current theme (sun for light,
moon for dark) and toggles to the other. The hero's `THEME` switch controls the
same site theme, so the headline, panels and app screenshot change together; the
contact sheet still shows both themes side by side. Theme changes are revealed
as a circle from the control that triggered them (`View Transition`), and fall
back to an instant swap when that API or motion is unavailable.

## Motion

The page borrows the launcher's short, explanatory motion instead of generic
scroll fades. Nothing loops in the viewport except the two marks that echo the
launch animation.

**On load** — the opening is a neutral gray one step away from the page: a
deeper gray with dark text in the light theme, a lighter gray with light text in
the dark theme, so the reveal is a gentle lift or dip rather than an inverted
flash. A blinking caret waits at the centre and the first headline line types
out; while it flies and scales into its real position in the `h1`, the
background eases to the page theme and the text settles into its final color.
The second line then types in place, the side column staggers in, the diamond
mark draws its stroke, the four corner brackets grow, the grid fades, and the
window is revealed by a clip path before a single scan line passes over it.

**On scroll** — each section spine draws downward and its square node turns into
the diamond; ledger rows, contact cells, architecture notes and platform columns
stagger in; the diagram frame opens from its corners; the system map draws the
diamond, then each link, then each node; the terminal fades in last.

**On interaction** — the header carries a scroll-progress hairline; nav and
footer links grow an underline; buttons move their arrow on hover; contact cells
and diagram nodes take an accent border; the theme switch runs a scan sweep
over the screenshot; the hero screenshot follows a fine pointer with a soft
accent glow; the diagram hub has a slow travelling dash; the terminal caret
blinks.

**Reduced motion** — with `prefers-reduced-motion: reduce` the intro is skipped
and the full headline is shown immediately; the reveals resolve to their final
state, the scans and glows are removed, the caret stops blinking and both smooth
scrolling and the mark loops are disabled. The theme toggle still works. All
content is readable without JavaScript.

## Files

```
site/
├─ index.html
├─ assets/
│  ├─ site.css          # light/dark tokens, line language, mirrors tokens.dart
│  ├─ site.js           # theme, typed intro, theme switch, copy, nav,
│  │                     # progress, glow, reveal
│  ├─ branding/         # the application icon (same SVG as the app)
│  └─ shots/            # screenshots rendered from the real widget tree
├─ NOTICE.md            # asset sources and licenses
└─ README.md
```

## Screenshots

`assets/shots/*.png` are produced by the launcher itself, not by a browser or a
third-party image. The harness lives at
`app/tool/screenshots/capture_test.dart` and renders the real pages with the
bundled fonts, design tokens, the sample instances the shell already uses when
the native core is unavailable, and a display-only account so the shell shows
its signed-in state:

```bash
cd app
flutter test tool/screenshots/capture_test.dart
# Defaults to ../site/assets/shots; override with ARCHOERACX_SHOT_DIR.
```

The harness renders two sizes, because the page uses the images at two sizes:

| File | Size | Used by |
| --- | --- | --- |
| `home-dark.png`, `home-light.png` | 1440 × 900 | hero theme screenshot, up to 1000 px wide |
| `instances-*.png`, `settings-*.png` | 1000 × 700 | 2×2 contact sheet, ~630 px cells |

The contact images are 10:7 rather than 16:10 so the UI text stays legible at
the smaller cell width; the CSS keeps the same ratio and must match the files.

Re-run it whenever the UI changes. Do not edit the PNGs by hand and do not paste
third-party launcher screenshots into this folder.

## Rules

- The page follows the Archoera UI language in both themes: one accent,
  hairlines, restrained 120/180/240 ms motion, readable contrast. Panel
  surfaces use the launcher's bevel cut; buttons, tags and segmented controls
  stay at its 2–6 px radii. Amber is reserved for the diamond core, not for
  controls or status.
- The light and dark token sets mirror `app/lib/theme/tokens.dart`; the build
  block keeps the primary button blue in both and the footer keeps the ink. A
  theme choice is stored in `localStorage` and mirrored in a first-party cookie;
  with no choice the site follows the OS, including live changes.
- Asset sources and licenses are recorded in [NOTICE.md](NOTICE.md).
- Copy, icons, line artwork and layout are original to this project. The loader
  marks inside the app screenshots belong to their upstream projects and keep
  the notices recorded in `app/assets/icons/loaders/SOURCES.md`.
- Keep `index.html` self-contained: no CDN fonts, analytics, iframes or trackers.
- Every image needs meaningful `alt` text; every icon-only control needs an
  accessible name.
- Test at 1440, 1024, 768 and 390 px wide, in light and dark, with
  `prefers-reduced-motion: reduce` enabled. Check the theme toggle and the
  neutral opening in both themes.
