<p align="center">
  <img src="app/assets/branding/archoera_icon_512.png" width="128" height="128" alt="ArchoeraCX">
</p>

# ArchoeraCX

> 开源、多端、面向 Minecraft: Java Edition 的混合架构启动器

> [!WARNING]
> **开发版 / Development build**  
> 本项目目前仅通过 GitHub 发布开发版。功能不完整，可能损坏实例或配置数据；**请勿用于正式环境或重要实例**。不提供稳定性、兼容性或数据安全保证。

---

ArchoeraCX 是一个开源的**跨平台 Minecraft: Java Edition 启动器**，桌面为主（Linux / macOS / Windows）。UI 层采用 Flutter，C++ 作为唯一枢纽，业务/API/SQLite 由 C# NativeAOT 模块持有，下载执行交给 Rust；全程进程内 FFI，无 Node 进程、无本地监听端口。

- 实例管理：创建 / 选择 / 删除 / 搜索、自定义目录、SQLite 索引 + `instance.json` 权威元数据
- 原版文件安装与启动，Java 自动扫描（含手动回退）
- 加载器：Fabric / Quilt / Forge / NeoForge 安装、生成物校验与按 processor 重跑修复
- Microsoft 登录：Authorization Code + PKCE（loopback）→ XBL → XSTS → Minecraft，支持在线 / 离线启动
- Modrinth 搜索与下载；Modrinth 版本或本地 `.mrpack` 整合包安装（原版 + 加载器 + overrides）；安装一律转后台任务，可边浏览边下载，页面底部队列条与任务中心实时显示进度
- Rust `DownloadPlan` 执行器：HTTP、hash 校验、原子落盘、进度与取消
- 界面：底部 CommandBar 的 AppShell、5 个页面、明暗主题、中英文 i18n
- 外观：浅色 / 深色 / 跟随系统、系统或自定义主题色（C++ 读取 XDG Portal / Windows 注册表 / AppKit）、全局着色与减少动效；关于页含环境信息复制与许可入口
- 图标：Tabler Icons 3.49.0（MIT）裁剪子集字体 + `AppIcons` 语义层，构建与运行都不联网、不依赖图标包
- 系统集成：登录授权页由 C++ 调系统 API 打开默认浏览器，登录成功后发送系统桌面通知（显示账户名）

> 当前为 GitHub-only alpha：上述能力已贯通；CurseForge 源尚未完成。当前基线为 340 个 C# 测试、37 个 Flutter 测试、21 个 Rust 测试通过（Linux x64）。

## 快速开始

> 完整的环境依赖、逐模块构建、client ID 注入、测试矩阵与数据目录见 **[docs/12-build-and-test.md](docs/12-build-and-test.md)**。

```bash
git clone https://github.com/BetaStudio2/ArchoeraCX.git
cd ArchoeraCX/app

./tool/build-core.sh    # C++ 核心
./tool/build-cs.sh      # C# NativeAOT 模块
./tool/build-dl.sh      # Rust DownloadPlan 执行器

flutter pub get
flutter gen-l10n
flutter build linux --release   # 或 windows / macos
```

Microsoft 登录的 client ID 必须在构建 C# 模块时注入（否则登录返回 `MICROSOFT_CLIENT_ID_MISSING`）：

```bash
export ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>"
./tool/build-cs.sh
```

```bash
./build/linux/x64/release/bundle/archoera_cx
```

## 架构

```text
Flutter UI (Dart)
  │  CoreClient / C ABI
  ▼
archoera_core (C++20)
  │  ModuleRegistry + MethodRegistry + Task/Event/ModuleHost
  ├──────────────┐
  ▼              ▼
archoera_cs     archoera_dl        （Rust 模块后续接入 C++ 枢纽）
C# NativeAOT    Rust
业务/API/SQLite  DownloadPlan 执行
```

红线：

- Dart 不直连业务 API；图片加载是唯一例外。
- C++ 只做生命周期、路由、任务和事件，不写业务规则。
- C# 是 API、业务、SQLite 的唯一 owner。
- Rust 只消费 `DownloadPlan`，不解压、不写 SQLite。
- `.mrpack` 的解析与安全解压由 C# 负责；Rust 只下载计划中的文件。
- token 不跨 C ABI、不写日志；refresh token / Minecraft token 存在本地加密凭据保险库中。

## 文档

- [文档索引](docs/README.md) —— 全部公开文档的入口
- [构建、运行与测试](docs/12-build-and-test.md) —— 环境 / 逐模块构建 / 测试矩阵 / 数据目录
- [详细架构设计](docs/10-architecture-design.md) —— ABI、模块协议、任务与事件、存储、流程、安全
- [Microsoft / Minecraft 认证](docs/authentication.md) —— OAuth、XBL / XSTS、client_id 审核门槛
- [UI 设计系统](docs/11-ui-design-system.md) —— 设计令牌、AppShell、页面结构、动效与资产边界
- [发行与下载策略](docs/09-distribution-and-download-policy.md) —— 发行通道、Provider / Policy / `DownloadPlan`、密钥与镜像
- [许可证与资产边界](docs/04-licensing.md) —— AGPL-3.0-only、依赖组合、字体 / 图标 / 品牌红线
- [路线图](docs/05-roadmap.md) —— 阶段目标、当前进度、验收标准
- [生态 API](docs/06-ecosystem-apis.md) —— Mojang、Microsoft、加载器、Modrinth / CurseForge、Java
- [技术预研](docs/08-technical-research/README.md) —— 设计输入与验证记录
- 历史设计稿见 [`docs/archive/`](docs/archive/README.md)（仅作溯源，不是现行规范）

## 特别致谢

启动器现在的样子离不开这些项目和人（应用内也会在 **设置 → 关于 → 特别致谢** 列出）：

- **Tabler Icons** — Paweł Kuna 与贡献者（MIT）。界面里的线性图标全部来自这套图标，随包分发的 `app/assets/fonts/AppIcons.ttf` 是裁剪后的子集；喜欢的话可以去 <https://github.com/tabler/tabler-icons> 点个 star。
- **Noto Sans SC** — Google Fonts / notofonts（OFL-1.1），界面字体。
- **Flutter**、**.NET**、**Rust** — 启动器赖以构建的三个运行时。
- **Modrinth** — Mod / 整合包 / 资源包 / 光影的元数据与下载。
- **BMCLAPI** — 官方 Mojang / Forge 源不可达时使用的大陆镜像。

各资产的来源与许可证逐项记录在 `app/assets/icons/*/SOURCES.md` 与 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 许可证

代码以 **AGPL-3.0-only** 发布，正文见 [LICENSE](LICENSE)。
在遵守 AGPL 义务（分发时开放源码、保留声明并标注修改）的前提下，可自由使用、学习、修改与再分发；本项目不提供闭源商业授权。

第三方依赖与随发布产物分发的内嵌运行库见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)；许可证与资产边界细则见 [docs/04-licensing.md](docs/04-licensing.md)。

> 图标字体单独遵循上游 **MIT**（© 2020-2026 Paweł Kuna），正文随应用分发于 `app/assets/licenses/Tabler-Icons-MIT.txt`，不适用 AGPL-3.0-only。仓库只提交打包好的子集字体，不提交上游图标集本身。

## 隐私与安全

- [隐私政策](PRIVACY.md) —— 本地账户、令牌、皮肤缓存、未成年人说明与删除方式
- [安全策略](SECURITY.md) —— 漏洞的私密报告渠道
- 账户与数据默认存放于系统数据目录，可用 `ARCHOERACX_DATA_DIR` 覆盖（见[构建、运行与测试](docs/12-build-and-test.md#5-数据目录与本地文件)）。

---

## 联系与贡献

- 仓库：<https://github.com/BetaStudio2/ArchoeraCX>
- **当前不接受外部代码贡献**（含 PR、补丁、代码片段），原因与可参与方式见 [CONTRIBUTING.md](CONTRIBUTING.md)。
- 可提交 Issue、崩溃日志、功能建议和文档纠错；请先脱敏，勿粘贴 token 或他人的个人信息。

## 免责声明

本项目与 Mojang Studios、Microsoft 无关联。

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

启动器不分发游戏本体、资源文件或绕过正版验证的在线服务。
