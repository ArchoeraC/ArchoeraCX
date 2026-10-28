# PCL 的实例/加载器目录模型

来源：`Meloong-Git/PCL` @ `93ede5d9e638c99fcec853a9ba1eecd91ceee7f5`（2026-10-24）。
许可证为《PCL 分发有限许可》，不是常规开源许可证；这里只记录行为观察，没有复制源码、文案或资源。

## 结论

PCL 把“实例”和“加载器 profile”放在同一个版本文件夹里：

- 实例路径固定为 `<游戏根>/versions/<实例名>/`（`McInstance.PathVersion`）。
- 新建实例时，默认实例名直接把加载器写进去，例如 `1.20.1-Forge_47.2.0`
  （`PageDownloadInstall.xaml.vb` 的 `GetSelectName`）。
- 安装开始时 `InstanceFolder = <游戏根>/versions/<实例名>/`
  （同文件 `BtnStart_Click`）。
- 给已有实例补装/更换加载器时，`Request.InstanceFolder = TargetInstance.PathVersion`
  （`PageDownloadModify.xaml.vb` 的 `StartInstall`），不会另建一个根级 profile 文件夹。
- 整合包安装（CurseForge/Modrinth/MMC/HMCL）同样先确定
  `<游戏根>/versions/<实例名>/`，再在该目录内安装加载器
  （`ModModpack.vb`）。
- 合并后的 profile 写在实例文件夹的 `<实例名>.json`，保留 `inheritsFrom` 指向基础版本；
  `McInstance.Check()` 会检查同级的 `versions/<基础版本>/<基础版本>.json` 是否存在。
- 共享的 `libraries/`、`assets/` 始终在游戏根；版本隔离（`PathIndie`）只决定
  mods/saves 等游戏目录，不改变 profile 的位置。

## 与 ArchoeraCX 旧实现的差异

旧实现把 `installRoot` 当成 profile 根：版本隔离实例的 `installRoot` 是父游戏根，
于是 `loader.commit` 写到 `<游戏根>/versions/<加载器 id>/<加载器 id>.json`，
`version.commit` 写到 `<游戏根>/versions/<游戏版本>/<游戏版本>.json`。
实例文件夹只有 `.archoera/instance.json`，启动时再去根目录借 profile；
`versions/<游戏版本>` 还会被实例发现逻辑当成一个根实例。

## 采用的修正

- `loader.plan` / `version.plan` 返回 `<实例>/<实例>.json` 的绝对路径；
  `loader.commit` / `version.commit` 优先写这个实例 profile。
- `libraries/`、processor 产物、installer 和 client jar 仍在安装根；
  “共享加载器文件”只影响库，不再影响 profile 归属。
- 自动修复、整合包安装共用 `CommitInstanceProfile`；复制实例时同步重命名 profile。
- 旧布局（根 `versions/<加载器 id>`）仍可解析，实例发现对旧 profile 的过滤保留。
- 判断原版不能只看 profile id：复制/重命名或 PCL 风格命名都可能让纯原版 JSON 带上加载器名字；
  以 loader library、`--fml.*` 参数和 mainClass 判断，无法确认时宁可报 `LOADER_NOT_INSTALLED`。
