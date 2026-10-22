# 03 · 参考项目清单与克隆规范

> **归档文档**（原 `docs/03-reference-projects.md`）。参考项目的许可证分级与使用规则，现行边界以许可证文档为准。
> 现行规范见 [`../04-licensing.md`](../04-licensing.md)；归档说明见 [`README.md`](README.md)。

> 目的：**用最低的法律风险，换取最高的信息密度。**
> 原则：先看许可证，再决定"读 / 抄 / 依赖"。许可证决定动作，不决定喜好。

---

## 1. 三档使用规则

| 档位 | 许可证 | 允许的动作 | 禁止的动作 |
|---|---|---|---|
| **A · 可依赖** | MIT / Apache-2.0 / BSD | 作为 NuGet/pub 依赖；可复制代码片段并保留版权声明 | 去掉版权头、冒用作者名义 |
| **B · 可读不可抄** | GPL-3.0 / LGPL-3.0 / AGPL-3.0 | 阅读源码、运行、观察行为、记录**自己的理解** | 复制/改写代码、复制资产与文案、并入本项目（会强制整体 GPL） |
| **C · 仅行为** | 闭源 / BUSL / 自定义许可 | 安装使用、观察 UI、看公开文档、抓包分析（合法范围内） | 反编译获取代码、复用图标/字体/文案/布局文件 |

> **本项目为 AGPL-3.0-only 开源，且不接受外部代码贡献**：B/C 档一律只读；A 档也需检查**传递依赖**——第三方 copyleft 代码会保留其许可，影响未来闭源。GitHub 上公开 ≠ 可并入本项目。

> 详见 `04-licensing.md`。

---

## 2. 主清单

### A 档 · 可直接依赖 / 可复制（MIT / Apache-2.0）

| 项目 | 语言 / UI | 许可证 | 定位 | 看什么 |
|---|---|---|---|---|
| [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) | C# | **MIT** | 最成熟的 .NET 启动核心库 | 安装/启动流水线、`MinecraftPath` 抽象、Forge 安装器、事件模型、认证 |
| [ProjBobcat](https://github.com/Corona-Studio/ProjBobcat) | C# | **MIT** | 功能最全的 .NET 核心（LauncherX 在用） | 资源安装器抽象、Java 探测、整合包解析、跨平台路径、下载限速 |
| [MinecraftLaunch](https://github.com/Lunova-Studio/MinecraftLaunch) | C# | **MIT** | 新一代 .NET 核心，API 较现代 | async 设计、Java 搜索、启动参数模型 |
| [XMCL](https://github.com/Voxelum/x-minecraft-launcher) | TS / Electron + Vue | **MIT** | 现代 UX + 磁盘高效 | 硬链接/符号链接去重、多实例、下载调度、资源索引 |
| [Kraft Launcher](https://github.com/KraftLauncher/kraft-launcher) | **Dart / Flutter** | **MIT** | Flutter 启动器，功能较完整 | 实例隔离、实例共享、Mod 安装流程、Flutter 工程结构（**可直接借鉴代码**） |
| [Foast Launcher](https://github.com/arctic-pot/foast-launcher) | **Dart / Flutter** | **MIT** | 轻量 Flutter 启动器 | 页面组织、主题、与核心的边界 |
| [FlutterCraft Launcher](https://github.com/fr-eed/fluttercraft-launcher) | **Dart / Flutter** | **BSD-3-Clause** | 极简 Flutter 启动器 | 最小可用闭环、Material 3 用法 |
| [dartcraft](https://pub.dev/packages/dartcraft) | Dart（pub 包） | **MIT** | 纯 Dart 启动库 | 若想用 Dart 写核心的可行性评估（生态较新，谨慎） |

> 生态组件例外：[authlib-injector](https://github.com/yushijinhun/authlib-injector)（Java，**AGPL-3.0**）用于第三方认证。它应以**独立进程 / `-javaagent`** 方式调用，不要静态集成；分发时提供其源码链接。

### B 档 · 只读（GPL-3.0 / LGPL-3.0）

| 项目 | 语言 / UI | 许可证 | 定位 | 看什么 |
|---|---|---|---|---|
| [HMCL](https://github.com/HMCL-dev/HMCL) | Java / JavaFX | **GPL-3.0** | 功能广度天花板、中文生态最全 | 下载源与镜像策略、Java 管理、整合包、i18n 规模、信息架构 |
| [Prism Launcher](https://github.com/PrismLauncher/PrismLauncher) | C++ / Qt | **GPL-3.0** | 实例与组件模型最干净 | 实例隔离、meta/组件系统、Java 校验、账户抽象、日志与崩溃处理 |
| [MultiMC](https://github.com/MultiMC/Launcher) | C++ / Qt | **GPL-3.0**（部分 Apache-2.0） | Prism 上游，架构原点 | 实例模型的历史设计取舍 |
| [Modrinth App](https://github.com/modrinth/code) | Rust / Tauri + Vue | **GPL-3.0** | 现代 UX 与资源管理 | SQLite schema、`.mrpack` 实现、资源安装事务、更新机制 |
| [ATLauncher](https://github.com/ATLauncher/ATLauncher) | Java | **GPL-3.0** | 老牌整合包向 | 整合包解析、实例配置、下载队列 |
| [SJMCL](https://github.com/UNIkeEN/SJMCL) | Rust / Tauri 2 + Next.js | **GPL-3.0 + 附加条款** | 现代 UX，Rust 核心 | 前后端 RPC 边界、下载中心、账户；**附加条款限制修改版分发，复用前必读** |
| [RPMLauncher](https://github.com/RPMTW/RPMLauncher) | **Dart / Flutter** | **GPL-3.0** | 中文社区 Flutter 启动器 | Flutter 桌面端页面组织、状态管理 |
| [FML](https://github.com/lxdklp/FML) | **Dart / Flutter** | **GPL-3.0** | 中文 Flutter 启动器 | Flutter 启动器的最小实现路径 |
| [BlockHelm Launcher](https://github.com/zqq-699/BlockHelm-Launcher) | C# | **GPL-3.0** | 轻量现代启动器 | 简洁实例/资源/账户模型 |
| [PojavLauncher](https://github.com/PojavLauncherTeam/PojavLauncher) | Java / C | **LGPL-3.0** | Android/iOS 跑 Java 版 | 仅 Android 方向参考（渲染、输入、JVM 适配） |
| [Fold Craft Launcher](https://github.com/FCL-Team/FoldCraftLauncher) | Java | **GPL-3.0** | Android 启动器 | 移动端实例管理与性能取舍 |
| [Zalith Launcher](https://github.com/ZalithLauncher/ZalithLauncher) | Java | **GPL-3.0** | Android 启动器 | 移动端 UI/渲染方案 |
| [PolyMC](https://github.com/PolyMC/PolyMC) | C++ / Qt | **GPL-3.0** | Prism 的历史分支 | 仅作历史参考，不建议投入 |

### C 档 · 仅行为参考

| 项目 | 语言 / UI | 许可状态 | 可参考 | 不可参考 |
|---|---|---|---|---|
| [LauncherX](https://corona.studio) | C# / Avalonia / NativeAOT | 闭源 | 设计令牌、背景/模糊、3D 皮肤、设置组织、启动流程（见本仓库报告） | 代码、图标、DINPro 字体、文案 |
| PCL2 / [PCL CE](https://github.com/PCL-Community/PCL-CE) | C# / WPF | 上游自定义许可；CE 中 `PCL.Core/` 为 **Apache-2.0** | 高性能 WPF 列表、下载模块、Windows 集成 | `Plain Craft Launcher 2/` 目录代码（自定义许可）；品牌与资源 |
| BakaXL | C# | 闭源（未核实） | 动效与视觉风格 | 代码与资产 |
| GDLauncher Carbon | Rust / React | **BUSL-1.1**（源码可见，非开源） | 现代 UX、更新器 | 任何代码复用（含竞争限制条款） |
| CurseForge App / Lunar / Feather / SKLauncher / TLauncher | 闭源 | 闭源 | 交互流程 | 代码、资产；后两者另有版权/盗版争议 |

### D 档 · 协议与规范（不是代码）

| 资料 | 用途 |
|---|---|
| [Mojang launchermeta](https://piston-meta.mojang.com/mc/game/version_manifest_v2.json) | 版本清单与下载地址（事实标准） |
| [Modrinth 文档](https://docs.modrinth.com) | API v2、`.mrpack` 规范 |
| [CurseForge API](https://docs.curseforge.com) | API v1、整合包 manifest |
| [Fabric Meta](https://meta.fabricmc.net) / [Quilt Meta](https://meta.quiltmc.org) | 加载器元数据 |
| [NeoForge](https://neoforged.net) / [Forge](https://files.minecraftforge.net) | 安装器与 Maven |
| [Minecraft Wiki · 版本清单格式](https://minecraft.wiki) | 字段语义、历史变更 |
| [authlib-injector 文档](https://authlib-injector.yushi.moe) | 第三方认证协议 |

---

## 3. 每个重点项目"看什么"（观察清单）

> 只记录**结论与自己的话**，不粘贴源码。每条笔记标注：项目 + commit + 日期 + 结论。

### CmlLib.Core（核心库首选）

- `MinecraftLauncher` 的安装→校验→启动三段式，事件如何合并进度。
- `MinecraftPath` 如何把"版本/资源/库/实例"路径抽象出来。
- Forge 安装：什么时候需要跑 Java installer，什么时候只解压。
- 认证：微软链路的 token 生命周期处理在哪一层。
- 失败重试与镜像 fallback 的策略。

### ProjBobcat（功能对照）

- 资源安装器的接口分层（Version/Mod/ResourcePack/Shader/Modpack）。
- Java 自动探测：注册表 / 常见路径 / 环境变量 / 版本号解析。
- 整合包：`.mrpack` 与 CurseForge zip 的统一抽象。
- 跨平台路径与权限问题的处理方式。

### HMCL（产品广度）

- 下载源管理：官方 / BMCLAPI / 自定义镜像的切换与测速。
- Java 管理：下载、校验、版本兼容提示。
- 整合包导入导出、Mod 更新检查。
- 设置项分组与 i18n 文案组织（信息架构，不是文案照抄）。
- 崩溃后的用户可读提示。

### Prism Launcher（架构）

- 实例目录结构与"组件（component）"概念。
- meta 系统：版本元数据如何缓存与增量更新。
- Java 版本与游戏版本的兼容矩阵。
- 账户体系（在线/离线/第三方）与 UI 解耦方式。
- 日志窗口、崩溃报告、"复制诊断信息"的设计。

### Modrinth App（现代 UX）

- Tauri 前后端边界：哪些逻辑在前端，哪些在 Rust。
- SQLite 表结构与迁移策略。
- 资源安装的"事务"：失败回滚、并发写、文件锁。
- `.mrpack` 的完整实现路径。
- 更新器与签名/校验。

### Flutter 形态（Kraft Launcher / RPMLauncher）

- **Kraft Launcher（MIT）**：工程结构、状态管理、实例隔离与共享、Mod 安装向导——可以**直接读代码并复用**。
- **RPMLauncher（GPL-3.0）**：中文桌面端的页面组织、加载/下载页面的交互表达。
- **FlutterCraft（BSD-3）**：最小闭环，适合对照"一个启动器最少需要哪些页面"。
- 重点观察：它们把游戏逻辑放在 Dart 还是原生侧；如果用纯 Dart，是否遇到进程管理/文件锁的坑。
- 反例参考：**SJMCL 采用 Tauri 2 + Rust + Next.js**（并非 Flutter），说明"桌面重逻辑 + Web UI"是同类项目的另一条主流路线。

### XMCL（磁盘效率）

- 硬链接/符号链接去重的判定条件与失败降级。
- 资源 hash 索引与"同一文件只存一份"的实现。
- 大规模资源列表的虚拟滚动与搜索。

### LauncherX（已分析，视觉参考）

- ThemeX 设计令牌：强调色、内容背景层级、前景色、字号阶梯、动画时长/缓动。
- 背景系统：纯色/图片/Acrylic/Mica + 渐进模糊缓存。
- 3D 皮肤查看器的降级策略（2D 回退）。
- 设置页信息架构与"危险操作"的呈现方式。
- **注意**：DINPro 是商业字体，不可复用；图标与插画同理。

---

## 4. 克隆规范

### 4.1 目录布局

```
Reference/
├─ upstream/                 # 浅克隆，第三方代码，永不参与构建
│  ├─ HMCL/
│  ├─ PrismLauncher/
│  └─ ...
├─ notes/                    # 自己的观察笔记（唯一允许进版本库的部分）
│  ├─ hmcl/download-mirror.md
│  └─ prism/instance-model.md
├─ MANIFEST.md               # 每个仓库的 commit / 许可证 / 用途
└─ LauncherX/                # 已有证据与分析材料
```

### 4.2 克隆脚本（浅克隆，节省空间）

```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p Reference/upstream

clone() { # $1=url  $2=dir
  [ -d "Reference/upstream/$2" ] && { echo "skip $2"; return; }
  git clone --depth=1 "$1" "Reference/upstream/$2"
  printf '%-28s %s\n' "$2" "$(git -C "Reference/upstream/$2" rev-parse HEAD)"
}

clone https://github.com/CmlLib/CmlLib.Core.git            CmlLib.Core
clone https://github.com/Corona-Studio/ProjBobcat.git      ProjBobcat
clone https://github.com/Lunova-Studio/MinecraftLaunch.git MinecraftLaunch
clone https://github.com/Voxelum/x-minecraft-launcher.git   XMCL
clone https://github.com/KraftLauncher/kraft-launcher.git    KraftLauncher
clone https://github.com/arctic-pot/foast-launcher.git       Foast
clone https://github.com/RPMTW/RPMLauncher.git               RPMLauncher
clone https://github.com/HMCL-dev/HMCL.git                   HMCL
clone https://github.com/PrismLauncher/PrismLauncher.git    PrismLauncher
clone https://github.com/MultiMC/Launcher.git               MultiMC
clone https://github.com/modrinth/code.git                  ModrinthApp
clone https://github.com/ATLauncher/ATLauncher.git          ATLauncher
clone https://github.com/UNIkeEN/SJMCL.git                  SJMCL
clone https://github.com/PojavLauncherTeam/PojavLauncher.git PojavLauncher
clone https://github.com/FCL-Team/FoldCraftLauncher.git      FCL
```

> 源码体积较大（HMCL/Prism/Modrinth 合计可达数百 MB）。若磁盘紧张，用 `--filter=blob:none --sparse` 只取需要的目录。

### 4.3 `Reference/MANIFEST.md` 模板

| 仓库 | Commit | 许可证 | 许可证文件 SHA-256 | 克隆日期 | 用途 | 是否进发布物 |
|---|---|---|---|---|---|---|
| HMCL | `abc1234` | GPL-3.0 | `...` | 2026-10-20 | 镜像策略参考 | 否 |
| CmlLib.Core | `def5678` | MIT | `...` | 2026-10-20 | 候选依赖 | 是（NuGet） |

记录规范：

- **每次升级/重克隆都必须更新 commit**，否则笔记失效。
- 许可证文件单独存 hash，用于日后证明"当时看到的是这个版本"。
- "用途"一栏写清是"依赖 / 只读参考 / 行为参考"。

### 4.4 版本库隔离（重要）

- `Reference/upstream/` 加入 `.gitignore`，**绝不进入产品仓库历史**。
- 若必须入库，放在独立仓库（`archoera-reference`），避免与产品代码同仓造成"衍生作品"争议。
- 发布包（安装器、源码包）中**不得**包含任何 B/C 档代码或资产。

---

## 5. 观察笔记规范（Clean-room）

1. 只写**结论**：这个项目怎么解决某个问题，为什么。
2. 不粘贴超过 3 行的源码；不复制注释、变量名、文案。
3. UI 参考用**截图 + 自己的描述**，不导出对方的布局/样式文件。
4. 每条笔记标注来源（项目 + commit + 文件路径），便于复核。
5. 建立 `feature → reference` 映射表：

| 我们的功能 | 参考项目 | 借鉴的是"方法"还是"实现" | 状态 |
|---|---|---|---|
| 镜像测速 | HMCL | 方法 | 已记录 |
| 实例组件模型 | Prism | 方法 | 待记录 |
| 磁盘去重 | XMCL | 实现（MIT，可复制） | 可复用 |

> 这张表在**来源/合规审计**时就是你的"独立实现"证据链。

---

## 6. 建议的克隆优先级

| 优先级 | 项目 | 理由 |
|---|---|---|
| P0 | CmlLib.Core / ProjBobcat / MinecraftLaunch | 直接决定核心能否少走弯路 |
| P0 | Prism / HMCL | 架构与产品广度，先建立"应该有什么"的全局观 |
| P1 | Modrinth App / XMCL | 现代 UX 与资源管理 |
| P1 | Kraft Launcher / Foast / RPMLauncher | Flutter 形态的直接先例（前两者 MIT 可复用） |
| P2 | ATLauncher / MultiMC | 历史设计取舍，按需 |
| P2 | LauncherX 报告 | 视觉与 C# 技术栈参考（已有） |
| P3 | Pojav / FCL / Zalith | 仅在确定做 Android 时 |
