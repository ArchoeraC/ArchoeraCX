# app/assets · 资产

计划中的目录：

```
assets/
├─ branding/   # 自绘 ArchoeraCX 图标（SVG + PNG）
├─ fonts/      # Noto Sans SC Medium + AppIcons 子集图标字体（生成物）
├─ icons/      # loaders/ 加载器标识位图；ui/ 图标字体来源与许可证说明
└─ licenses/   # 字体、图标、第三方资产许可正文
```

红线：

- 不使用 LauncherX 的图标、插画、文案、截图或 DINPro 字体；
- 不内置未授权商业字体/素材；
- 不整包引入第三方图标集：图标只以**打包好的子集字体**入库，上游 webfont / SVG / 缓存全部走 `.gitignore`。
- 每个资产记录来源与许可证（`icons/*/SOURCES.md`），许可正文放 `licenses/`。
- 当前字体：`NotoSansSC-Medium.otf`，来源 notofonts/noto-cjk，OFL-1.1，见 `fonts/OFL.txt`。
- UI 图标字体：`AppIcons.ttf`，Tabler Icons 3.49.0（MIT）子集，由 `tool/build-icons.sh` 生成，来源与许可见 `icons/ui/SOURCES.md`；许可正文 `licenses/Tabler-Icons-MIT.txt` 随包分发（`lib/app/licenses.dart` 注册到应用内许可页）；页面只经 `lib/theme/icons/app_icons.dart` 引用。

- 应用图标：`branding/archoera_icon.svg`，自绘，来源与许可见 `branding/SOURCES.md`。
