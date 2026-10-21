# 10 · 架构设计总纲（完整版）

> **状态**：Draft v1 · 2026-10-20
> **适用范围**：ArchoeraCX 0.x – 1.x
> **规范性**：本文是架构的 **单一事实来源（SSOT）**。与 `02` / `07` / `08` / `09` 冲突时，以本文为准：
> `02` 保留 5 分钟摘要，`07` 保留 ABI 草案与踩坑记录，`08` 保留技术预研证据，`09` 保留发行与下载策略。
>
> **标记约定**
> - **[已定]**：已做决策，实现不得随意偏离；变更需新增 ADR。
> - **[待验证]**：设计成立，但必须用真实样本/基准确认。
> - **[待决策]**：产品/商业问题，架构只预留接口，不替你做决定。
>
> **阅读顺序**：§1 → §2 → §3 → §6 → §7 → §8/§9/§10 → §11/§12 → 其余按需。

---

## 目录

- [0. 文档定位](#0-文档定位)
- [1. 目标、约束与非目标](#1-目标约束与非目标)
- [2. 架构决策记录（ADR）](#2-架构决策记录adr)
- [3. 系统上下文与总体架构](#3-系统上下文与总体架构)
- [4. 仓库与工程结构](#4-仓库与工程结构)
- [5. 核心运行时（C++ Hub）](#5-核心运行时c-hub)
- [6. 对外 ABI（v1）](#6-对外-abiv1)
- [7. 模块协议（内部 ABI）](#7-模块协议内部-abi)
- [8. C# 业务核心（archoera_cs）](#8-c-业务核心archoera_cs)
- [9. Rust 下载器（archoera_dl）](#9-rust-下载器archoera_dl)
- [10. Flutter UI（archoera_cx）](#10-flutter-uiarchoera_cx)
- [11. 数据与存储](#11-数据与存储)
- [12. 关键流程](#12-关键流程)
- [13. 网络与 API 设计](#13-网络与-api-设计)
- [14. 安全设计](#14-安全设计)
- [15. 合规与发行架构](#15-合规与发行架构)
- [16. 性能与容量](#16-性能与容量)
- [17. 可观测性与诊断](#17-可观测性与诊断)
- [18. 测试与质量](#18-测试与质量)
- [19. 构建、打包与发布](#19-构建打包与发布)
- [20. 实施路线与验收](#20-实施路线与验收)
- [21. 风险与未决问题](#21-风险与未决问题)
- [22. 附录](#22-附录)

---

## 0. 文档定位

本文回答六个问题：

1. 系统由哪些部分组成，边界在哪里；
2. 跨边界传什么、谁拥有数据、谁可以写；
3. 一次安装/启动/登录/搜索/导入的完整链路；
4. 失败、取消、崩溃、退出时发生什么；
5. 安全、合规、性能、可观测性如何落进结构；
6. 如何构建、测试、发布，以及先写哪一块。

本文**不**回答：视觉稿、定价、市场策略、具体法律意见。这些分别属于设计、商业与法务文档。

| 文档 | 关系 |
|---|---|
| [`08-technical-research/`](08-technical-research/) | 版本合并、加载器、搜索、崩溃、下载、整合包、合规的预研证据 |
| [`09-distribution-and-download-policy.md`](09-distribution-and-download-policy.md) | 发行通道、Provider/Policy/Plan、密钥与镜像策略 |
| [`06-ecosystem-apis.md`](06-ecosystem-apis.md) | 外部接口索引与字段说明 |
| [`05-roadmap.md`](05-roadmap.md) | 阶段、验收标准与排期 |
| [`authentication.md`](authentication.md) | Microsoft Device Code、XBL/XSTS、Minecraft client_id 审核 |

---

## 1. 目标、约束与非目标

### 1.1 产品目标

| 目标 | 可验收的定义 |
|---|---|
| 装得上 | 原版 + Fabric/Quilt/Forge/NeoForge + 主流整合包可一键安装 |
| 跑得动 | 启动参数、Java、账户、日志、崩溃诊断正确；失败可读、可恢复 |
| 看得舒服 | 主题/背景/动效/3D 皮肤；中文排版与 150% 缩放不错位 |
| 磁盘高效 | 多实例共享 libraries/assets；内容库按 hash 去重；删除实例不误伤共享库 |
| 可诊断 | 任何失败都有 taskId、阶段、错误码、可导出诊断包 |
| 可维护 | 4 门语言但边界清晰；业务可在无 UI 的 CLI/测试中跑通 |

### 1.2 质量属性

| 属性 | 场景 | 目标 |
|---|---|---|
| 启动性能 | SSD、冷启动到主界面可交互 | < 1.5s；核心 init < 400ms |
| 交互性能 | 10 万行日志滚动、1000 个文件进度 | UI 60fps；事件合并后不掉帧 |
| 可靠性 | 下载中断、进程被杀、磁盘满 | 可续传/可回滚，不产生幽灵实例 |
| 可恢复 | SQLite 损坏、实例目录被手改 | 从 `instance.json`/磁盘重建索引，不删除用户数据 |
| 安全 | 恶意整合包、脏镜像、token 泄漏 | 路径逃逸/zip bomb/日志泄漏全部有明确拒绝点 |
| 可移植 | Windows 10/11 x64、Linux x64、macOS arm64 | 三平台同一 ABI；平台差异集中在 C++/C# |
| 合规 | 不同发行渠道 | 构建期裁剪 + 运行期默认分级（cn L0 / global L1）+ 审计，不靠 UI 灰按钮 |
| 可观测 | 用户报障 | 一键导出日志、模块版本、任务快照、minidump |

### 1.3 硬约束

| 约束 | 内容 | 后果 |
|---|---|---|
| 许可证 | **AGPL-3.0-only**，不接受外部代码贡献 | 分发/网络服务需提供源码；未来可闭源新版本但不可撤回旧版本 |
| UI | **Flutter / Dart** | 双运行时；平台插件债；UI 不得含业务 |
| 对外边界 | **唯一 C ABI，C++ 枢纽** | Dart 只加载 `archoera_core` 一个库 |
| 业务核心 | **C# / .NET 10 / NativeAOT** | 无托管调试器；业务必须放纯类库 |
| 下载 | **Rust cdylib** | 多一套工具链；只做“字节 → 文件” |
| 进程模型 | **单启动器进程 + 必要子进程** | 模块崩溃不隔离；JVM/更新器/processor 是子进程 |
| 网络 | **API 全归 C#；文件下载归 Rust；图片例外走 Flutter** | 两套 HTTP 栈，必须统一代理/UA/错误语义 |
| 数据 | **SQLite 只有 C# 能写** | Rust 用文件系统内容库，不碰数据库 |
| 内容 | 不分发游戏本体/assets/libraries；不提供绕过正版验证的服务 | 下载必须来自官方或白名单镜像 |

### 1.4 非目标（本阶段明确不做）

- 运行时插件系统、第三方扩展 SDK（NativeAOT 不支持动态加载，安全边界也不允许）。
- 移动端、Web 端、多窗口 IDE 式布局。
- 云同步、社交、启动器内广告联盟、遥测默认开启。
- Mac App Store（AGPL 与 DRM/附加限制冲突）。
- 自建游戏文件镜像、内置 CurseForge key、绕过平台 API 的抓取。
- 在启动器内执行任意用户脚本/宏。

### 1.5 架构原则

1. **单一对外 ABI**：Dart 只认识 `archoera_core`；模块由 C++ 加载。
2. **单一 owner**：每个关注点只有一个模块能写/能出网/能决策。
3. **UI 与枢纽零业务**：Flutter 只渲染与交互；C++ 只做生命周期、路由、任务、事件。
4. **模块不直接互调**：一律经 C++ 路由；跨模块协作走“效果协议”（§5.7）。
5. **跨边界数据不可变**：消息要么只读，要么明确移交所有权；禁止共享可变状态。
6. **UI 线程永不阻塞**：所有可能阻塞的调用都是“投递任务 + 事件回调”。
7. **路径与哈希在 owner 校验**：谁写文件谁负责规范化；不能只靠上层。
8. **SQLite 单写者**：C# 是唯一写入方；Rust/Flutter 不写库。
9. **策略在构建期，默认值在运行期**：发行通道是编译期常量；运行期只能降权。
10. **可观测性是功能**：taskId、阶段、错误码、模块版本、诊断包从第一天就有。
11. **先证据后实现**：协议没有权威规范时，样本与快照测试就是规范。
12. **失败必须可恢复**：任何写操作都要能回答“中断后留下什么、怎么回滚”。

### 1.6 关键质量场景

| # | 场景 | 期望 | 验证 |
|---|---|---|---|
| Q1 | 全新安装 1.21.4 并启动 | 哈希全匹配、进入主菜单 | 集成测试 + 假 JVM |
| Q2 | 安装到 80% 断网 | 保留 `.part`；重跑只补缺失 | 本地假 HTTP |
| Q3 | 安装中强杀进程 | 无半成品实例；重启可续跑 | kill -9 / TerminateProcess |
| Q4 | 导入恶意 `.mrpack` | 拒绝路径逃逸/zip bomb，实例不被污染 | 恶意样本库 |
| Q5 | 10 万行日志滚动 | 60fps，内存不持续增长 | 性能基准 |
| Q6 | 任一模块 abort | 生成 minidump；下次启动可见崩溃报告 | 崩溃注入 |
| Q7 | 热重启 Flutter | 原生任务继续；UI 重新 attach 后看到快照 | 开发期反复热重启 |
| Q8 | 用户手改 `instance.json` | 启动时收养/修复，不丢实例 | 对账测试 |

---

## 2. 架构决策记录（ADR）

| 编号 | 决策 | 状态 | 理由与后果 |
|---|---|---|---|
| A1 | 分发采用 **AGPL-3.0-only**，不接受外部贡献 | 已定 | 强 copyleft + 网络条款；版权集中，未来可闭源新版本；App Store 不可用 |
| A2 | UI 采用 **Flutter** | 已定 | 表现力/动效/跨端一致；接受双运行时与插件债 |
| A3 | 对外边界为 **C ABI + C++ 枢纽** | 已定 | ABI 稳定、可换 UI、事件汇聚；代价是崩溃不隔离 |
| A4 | 业务核心为 **C# NativeAOT 共享库** | 已定 | 团队熟悉、生态成熟、SQLite/注册表/HTTP 方便 |
| A5 | 下载器为 **Rust cdylib** | 已定（P1 压测后复核） | 并发/校验/内存安全；代价是多一套工具链 |
| A6 | **API/HTTP 全部归 C#** | 已定 | 唯一 API owner；代理/证书/限流/缓存一致；可 headless |
| A7 | **单启动器进程 + 必要子进程** | 已定 | 低延迟；JVM/更新器/processor 子进程隔离；模块崩溃仍会带走整个启动器 |
| A8 | **SQLite 只有 C# 写** | 已定 | 避免多语言并发写；Rust 用文件系统内容库 |
| A9 | **图片由 Flutter 直接加载**（唯一例外） | 已定 | 避免图片字节穿过 ABI；失败回退本地缓存/占位图 |
| A10 | **取消 C 网络层** | 已定 | 少一门语言；API 归 C#，文件归 Rust |
| A11 | **模块加载后不卸载** | 已定 | NativeAOT 与 tokio 线程无法安全 `dlclose`；只在 `shutdown` 时停服务，进程退出回收 |
| A12 | **跨模块协作走“效果协议”** | 已定 | C# 产出下载计划，C++ 路由给 Rust，完成后回调 C#；C++ 不理解业务语义 |
| A13 | **`instance.json` 是实例配置的权威源** | 已定 | 便携/可恢复/可手改；SQLite 是索引与运行态缓存，启动时对账 |
| A14 | **Flutter 状态管理用 Riverpod** | 已定（PoC 验证） | 依赖注入/测试友好/编译期安全；避免把业务塞进 Widget |
| A15 | **C# 与 Rust 两套 HTTP 栈** | 已定 | C# 管 API，Rust 管文件；代理配置由 C# 解析后显式传给 Rust，UA/超时/重试语义对齐 |
| A16 | **不做运行时插件系统** | 已定 | NativeAOT 动态加载受限；插件会破坏安全与合规边界 |
| A17 | **更新器是独立进程** | 已定 | 替换自身文件时不能占用自己；失败可回滚 |
| A18 | **支持 attach/detach 热重启** | 已定 | Flutter 热重启不卸载原生库；任务继续，UI 重连后拉快照 |
| A19 | **合规门控 + capability** | 已定 | 构建期通道 + 运行期默认分级（cn L0 / global L1）+ 同意/审计；capability 只防进程内绕过，不是 DRM |
| A20 | **发行通道是编译期常量** | 已定 | `official-cn` / `official-global` / `dev`；`official-cn` 裁剪 L2 注册，`official-global` 保留 L2 但默认需确认 |
| A21 | **遥测默认关闭** | 已定 | 任何上传必须 opt-in；崩溃转储默认本地 |
| A22 | **契约版本化** | 已定 | ABI、JSON schema、DB migration、模块版本各自独立演进 |
| A23 | **默认发行通道为 `official-cn`（L0）** | 已定 | 主发行版采用 cn 构建，默认禁用内容下载；`official-global` 作为单独构建（L1/L2）保留；通道仍是编译期常量 |
| A24 | **macOS 仅支持 Apple Silicon（arm64）** | 已定 | 降低构建/签名/测试矩阵；x64 Mac 暂不支持，下载页与关于页明确标注 |

**被否决的方案（存档）**

| 方案 | 否决原因 |
|---|---|
| Dart 作为 API 网关/安装核心 | 下载计划依赖 rules/继承/索引展开；UI 变厚；headless 依赖 Dart |
| C + libcurl 统一传输层 | 第 5 门语言；C# 异步流式跨 ABI 成本高；收益不足 |
| Rust 管所有 HTTP | C# 认证/JSON 逻辑隔着 ABI 调 Rust，开发体验差 |
| stdio JSON-RPC 作为主通道 | 延迟/生命周期/事件合并都更差；只保留为开发期 CLI |
| 多进程模块化（每个模块一个进程） | 崩溃隔离好但延迟/打包/调试成本高；作为未来退路，不作为 v1 |

---

## 3. 系统上下文与总体架构

### 3.1 角色与外部系统

| 角色/系统 | 关系 | 协议 |
|---|---|---|
| 用户 | 安装、启动、管理实例/账户/Mod | UI |
| Mojang launchermeta / resources / libraries | 版本清单、版本 JSON、资源、库、Java runtime | HTTPS |
| Microsoft Entra / Xbox Live / Minecraft Services | 正版登录、档案、拥有验证 | OAuth 2.0 device code + HTTPS |
| Fabric / Quilt Meta | 加载器 profile | HTTPS |
| Forge / NeoForge Maven | installer、库、processor 产物 | HTTPS |
| Modrinth / CurseForge | 搜索、详情、版本、下载 | HTTPS API |
| 本地 JVM | 运行游戏、Forge/NeoForge processors | 子进程 + argv/stdout/stderr |
| authlib-injector | 第三方认证（Yggdrasil） | `-javaagent` 注入游戏 JVM |
| OS | 文件、注册表、凭据库、窗口、托盘、签名 | 平台 API |
| 更新通道 | 启动器自更新 | HTTPS + 签名 manifest |

### 3.2 上下文图

```
                         ┌──────────────────────────────┐
                         │           用户                │
                         └──────────────┬───────────────┘
                                        │ UI
┌───────────────────────────────────────▼───────────────────────────────────────┐
│                         ArchoeraCX 启动器进程                                  │
│  Flutter UI ──C ABI──▶ C++ Hub ──▶ C# 业务/API ──▶ Mojang / 微软 / Modrinth / CF │
│                              │                                                │
│                              └────▶ Rust 下载器 ──▶ 官方源 / 白名单镜像          │
│                                       │                                        │
│                                       └──▶ 本地磁盘（内容库 / 实例 / 共享库）    │
└───────────────────────────────┬───────────────────────────────────────────────┘
                                │ spawn
                    ┌───────────▼───────────┐
                    │ 游戏 JVM / processors │──▶ 本地文件 / 网络（游戏自身）
                    └───────────────────────┘
```

### 3.3 运行时容器

| 容器 | 形态 | 职责 | 生命周期 |
|---|---|---|---|
| 启动器进程 | 单进程，加载 4 个模块 | UI、编排、业务、下载 | 用户启动到退出 |
| 游戏 JVM | 子进程 | 运行 Minecraft + 加载器 + Mod | 每次启动 |
| Processor JVM | 子进程 | Forge/NeoForge processors | 安装期，短生命周期 |
| 更新器 | 独立可执行文件 | 替换启动器文件、回滚 | 更新时 |
| authlib-injector | 游戏 JVM 内的 javaagent | 第三方认证 | 随游戏 JVM |

> “单进程”指**启动器本体**；游戏、processor、更新器天然是子进程。模块崩溃仍会带走整个启动器，这是 A7 已接受的代价。

### 3.4 模块视图

```
┌──────────────────────────────────────────────────────────────┐
│ Flutter UI (Dart)                                            │
│  presentation · application(state) · design · l10n           │
│  CoreClient(dart:ffi) · EventPump · NativeCallable.listener   │
└──────────────────────────┬───────────────────────────────────┘
                           │ 唯一 C ABI（§6）
┌──────────────────────────▼───────────────────────────────────┐
│ archoera_core (C++20)                                        │
│  Abi · Dispatcher · TaskRegistry · EffectRunner · EventPump   │
│  ModuleHost · Config · Log · Crash · Platform                 │
└───────┬──────────────────────────────────────┬───────────────┘
        │ module ABI（§7）                      │ module ABI（§7）
┌───────▼────────────────────────┐   ┌─────────▼───────────────┐
│ archoera_cs (C# NativeAOT)     │   │ archoera_dl (Rust)      │
│ 实例/Java/版本/账户/安装/启动   │   │ 下载/校验/分片/断点/去重 │
│ API/搜索/Mod/整合包/崩溃/合规   │   │ 只认 DownloadPlan        │
└───────┬────────────────────────┘   └─────────┬───────────────┘
        │                                      │
        ▼                                      ▼
  SQLite / 文件系统 / HTTP API            HTTP 文件 / 磁盘
```

### 3.5 依赖规则（硬性）

1. `archoera_cx` 只能依赖 `contracts/abi` 与 Flutter 生态；**不得**直接读文件、起进程、连业务 API（图片加载与 WebView 展示除外，见 §10.7/§10.9）。
2. `archoera_core` 只能依赖模块协议与平台库；**不得**出现 Mojang/Modrinth/加载器业务规则。
3. `archoera_cs` 不得调用 `archoera_dl` 的内部函数；需要下载时返回效果，由 C++ 路由。
4. `archoera_dl` 不得理解“版本/Mod/加载器”；只接受 `DownloadPlan`。
5. 模块之间不得共享可变内存；不得互相 `dlopen`。
6. `contracts/` 不依赖任何实现；实现依赖契约。
7. 任何反向依赖都视为架构违规，CI 用依赖图/符号检查拦截。

### 3.6 部署视图

| 平台 | 启动器 | 原生产物 | 备注 |
|---|---|---|---|
| Windows 10/11 x64 | `ArchoeraCX.exe` + Flutter 数据 | `archoera_core.dll`、`archoera_cs.dll`、`archoera_dl.dll` | 应用目录绝对路径加载；禁止 PATH 搜索 |
| Linux x64 | `archoeracx` + `lib/` | `libarchoera_core.so` 等 | `$ORIGIN` RPATH；AppImage/Flatpak |
| macOS arm64 | `ArchoeraCX.app` | `libarchoera_core.dylib` 等 | 仅 Apple Silicon；arm64 构建；嵌套库逐个签名 |
| 便携模式 | 目录内 `data/` | 同上 | `--portable` 或标记文件；需可写 |

### 3.7 进程与线程总览

| 执行体 | 归属 | 职责 | 禁止 |
|---|---|---|---|
| UI isolate | Flutter | 渲染、输入、`poll`、状态合并 | 阻塞调用、文件/网络 |
| Hub 线程 | C++ | 路由、任务状态、效果、事件队列 | 业务解析、阻塞 I/O |
| C++ 平台线程 | C++ | 托盘、单实例、窗口消息、深链 | 业务 |
| .NET 线程池 | C# | API、文件、安装、启动、SQLite | 直接调 Dart |
| tokio runtime | Rust | 下载、校验、落盘 | 写 SQLite、理解业务 |
| JVM 主线程 | 子进程 | 游戏 | — |
| Crashpad 线程 | C++ | minidump | 业务 |

---

## 4. 仓库与工程结构

### 4.1 Monorepo 布局

```
ArchoeraCX/
├─ app/                         # 单应用根（Flutter + 原生模块）
│  ├─ lib/
│  │  ├─ app/                   # bootstrap / app / router / shell
│  │  ├─ pages/                 # 页面（按页面拆分）
│  │  ├─ services/              # CoreClient / 事件 / 平台服务
│  │  ├─ stores/                # Riverpod providers / prefs
│  │  ├─ theme/                 # tokens / app_theme
│  │  ├─ widgets/               # common / layout
│  │  ├─ utils/                 # 无状态工具
│  │  └─ l10n/{*.arb,generated}
│  ├─ core/                     # C++ 枢纽（archoera_core）
│  │  ├─ include/archoera/abi.h
│  │  ├─ src/{abi,dispatcher,task,effect,event,module,config,log,crash,platform}
│  │  └─ tests/
│  ├─ modules/
│  │  ├─ business/              # C#（archoera_cs）
│  │  │  ├─ Archoera.Domain/
│  │  │  ├─ Archoera.Application/
│  │  │  ├─ Archoera.Infrastructure/
│  │  │  ├─ Archoera.Exports/
│  │  │  └─ Archoera.DevHost/
│  │  └─ downloader/            # Rust（archoera_dl + archoera_updater）
│  │     ├─ src/{lib,plan,scheduler,limiter,http,file,hash,dedup,post,event,error}.rs
│  │     ├─ src/bin/updater.rs  # 独立更新器
│  │     └─ tests/
│  ├─ native/                   # 平台桥接（窗口/托盘/深链/单实例）
│  ├─ contracts/                # ABI / method / event / error schema
│  │  ├─ abi/archoera_abi.h
│  │  ├─ module/archoera_module_abi.h
│  │  ├─ methods/*.schema.json
│  │  ├─ events/*.schema.json
│  │  └─ errors/codes.json
│  ├─ assets/{fonts,icons,licenses}
│  ├─ tool/                     # build-core.sh / 辅助脚本
│  ├─ test/
│  ├─ pubspec.yaml
│  └─ .fvmrc
├─ tools/
│  ├─ archoera-cli/             # 开发/CI/排障入口
│  ├─ schema-check/
│  └─ fixtures/
├─ tests/
│  ├─ integration/              # 跨语言、假 HTTP、假 JVM
│  ├─ security/                 # 恶意 zip / 路径 / fuzz
│  └─ e2e/
├─ docs/
├─ Reference/                   # 只读参考，不参与构建
├─ CMakeLists.txt
├─ CMakePresets.json
├─ global.json
├─ rust-toolchain.toml
└─ README.md
```

### 4.2 产物与命名

| 产物 | 名称 | 说明 |
|---|---|---|
| 对外库 | `archoera_core` | Dart 唯一加载 |
| 业务模块 | `archoera_cs` | C# NativeAOT 共享库 |
| 下载模块 | `archoera_dl` | Rust cdylib |
| UI | `archoera_cx` | Flutter 应用 |
| CLI | `archoera-cli` | 同方法集的 headless 入口 |
| 更新器 | `archoera_updater` | 独立 Rust 可执行文件；下载/验签/替换/回滚 |

### 4.3 版本与契约

| 版本 | 载体 | 规则 |
|---|---|---|
| 应用版本 | SemVer `MAJOR.MINOR.PATCH+BUILD` | 发布标签；关于页展示 |
| ABI 版本 | `ARCHOERA_ABI_VERSION` | 主版本不兼容即拒绝加载；次版本只加可选字段 |
| JSON schema | `contracts/methods/*.schema.json` | 每个 method 一个；CI 校验请求/响应/事件 |
| DB schema | `schema_migrations.version` | 只前进；迁移前备份；失败回滚 |
| 模块版本 | `archoera_module_v1.version` | 与 ABI 独立；握手时校验 |
| 合并 schema | `mergeSchemaVersion` | 版本合并规则变化时递增，使有效版本缓存失效 |

### 4.4 分支与发布

- 主干开发；发布走 `release/x.y` 分支 + tag。
- 每个 tag 产出：源码归档、SBOM、第三方许可、三平台安装包、SHA-256。
- 契约变更必须同时更新 schema、文档、契约测试；否则 CI 失败。

---

## 5. 核心运行时（C++ Hub）

### 5.1 职责与非职责

**做**

- 加载/初始化模块，版本握手；
- 把 `archoera_call` 路由到模块；
- 维护 taskId、状态机、取消、超时、效果链；
- 汇聚事件、合并进度、批量投递；
- 统一日志、崩溃处理、配置、平台服务；
- 校验 ABI 入参、UTF-8、长度、空指针。

**不做**

- 不解析版本 JSON、不拼启动参数、不判断 Mod 兼容性；
- 不直接读写 SQLite；
- 不直接调 Dart；
- 不实现 HTTP（API 在 C#，文件在 Rust）。

### 5.2 组件

| 组件 | 职责 | 关键约束 |
|---|---|---|
| `Abi` | 导出 C 函数、参数校验、错误封装 | 不信任任何跨边界数据 |
| `Dispatcher` | `method → module` 路由、方法白名单 | 显式注册表；未知方法返回 `E_UNSUPPORTED` |
| `TaskRegistry` | taskId、状态机、父子任务、取消、超时 | 不阻塞；状态迁移原子 |
| `EffectRunner` | 执行 `download` 效果，回调生产者 | 不理解计划内容 |
| `EventPump` | MPSC 队列、seq、批量、合并、backpressure | 不丢状态事件；进度可合并 |
| `ModuleRegistry` + `ModuleHost` | 多模块加载、函数表、版本握手、按名字路由/取消/关闭 | 只从白名单文件名/环境路径加载；永不卸载 |
| `Config` | 分层配置、schema 校验、热更新（只降权） | 密钥不落日志 |
| `Log` | 结构化日志、轮转、脱敏 | 不记录 token |
| `Crash` | 信号/SEH、minidump、日志 flush | 安装于模块加载之前 |
| `Platform` | 托盘、单实例、深链、文件关联、窗口 | 平台差异集中于此 |

### 5.3 启动序列

```
1. 进程启动（Flutter 已加载 archoera_core）
2. archoera_init(config_json)
   a. 解析配置：dataDir/cacheDir/logDir/moduleDir/channel/locale/safeMode
   b. 初始化日志（先写启动日志）
   c. 安装崩溃处理器（Crashpad + signal/SEH）
   d. 初始化平台服务（单实例、深链、托盘延迟）
   e. 加载并握手 archoera_cs（关键模块）
   f. C# 打开 SQLite、迁移、加载设置/合规状态
   g. 加载并握手 archoera_dl（非关键，失败降级）
   h. 恢复未完成任务（可选；默认只标记，不自动重跑）
   i. 返回 core 句柄；UI 可交互
3. UI 调 attach(0) + set_wakeup，开始 poll
4. 后台任务：清单刷新、崩溃报告检查、更新检查（受门控）
```

**失败策略**

| 失败 | 处理 |
|---|---|
| C# 加载/握手失败 | `init` 返回错误；UI 进入诊断页，可导出日志 |
| SQLite 迁移失败 | 回滚 + 备份；拒绝进入主界面，提供“修复/导出” |
| Rust 加载失败 | 禁用下载并提示；不影响启动已有实例；不实现第二套下载器 |
| 崩溃处理器安装失败 | 记录 warning；仍继续（风险提示） |
| 配置无效 | 使用安全默认值；记录具体 JSON Pointer |

### 5.4 关闭序列

```
1. shutdown 标记：拒绝新 call
2. 取消所有任务；等待宽限期（默认 5s）
3. 通知模块 shutdown：
   - C#：停止 HTTP、flush SQLite、关闭日志
   - Rust：取消 token、等待 JoinSet、写 .part.meta
4. 停止平台服务（托盘/单实例）
5. flush 日志/崩溃处理器
6. 不 dlclose；返回；进程退出
```

- `shutdown` 幂等；重复调用直接返回。
- 超过宽限期：记录哪些任务未收尾；**不**调用 `std::process::exit`（会让 Rust/C# 无机会落盘）。
- 强杀场景由 `.part.meta`、SQLite WAL、事务 journal 保证可恢复。

### 5.5 线程模型

| 线程 | 数量 | 职责 |
|---|---|---|
| Hub | 1 | 任务路由、状态迁移、效果调度、事件入队 |
| C++ worker | 0–4 | 平台/杂项；不做业务 |
| .NET 线程池 | 运行时管理 | C# 异步业务 |
| tokio | 1 runtime，多 worker | Rust 下载 |
| Crashpad | 1 | minidump |
| UI isolate | 1 | 渲染、poll |

**规则**

1. `archoera_call` 可从任意线程调用，但内部统一入队 Hub；UI 只在 UI isolate 调。
2. 模块回调可来自任意线程；C++ 只做入队，不执行长逻辑。
3. 模块只向 Hub 投事件，不直接回调 Dart。
4. 跨线程数据不可变；需要所有权时显式 move。
5. 禁止在持锁时调用模块/回调；锁只保护短临界区。

### 5.6 任务状态机

```
                 ┌──────────┐
                 │  Queued  │
                 └────┬─────┘
                      │ dispatch
                 ┌────▼─────┐
       ┌─────────│ Running  │─────────┐
       │         └────┬─────┘         │
       │ effect       │ done          │ cancel
┌──────▼──────┐  ┌───▼────┐    ┌──────▼──────┐
│ WaitingEffect│  │Completed│    │  Cancelled  │
└──────┬──────┘  └────────┘    └─────────────┘
       │ effect done
       └──────────────▶ Running
                      │ error
                 ┌────▼─────┐
                 │  Failed  │
                 └──────────┘
```

- 每个任务有 `deadline`（可空）、`parentTaskId`（可空）、`cancelToken`。
- 取消父任务级联取消子任务/效果。
- `Completed/Failed/Cancelled` 是终态；结果保留到 `archoera_release` 或 TTL（默认 10 分钟）。
- 同步完成的调用也走状态机：`call` 返回 taskId，事件队列中立即有 `Completed`。

### 5.7 效果协议（跨模块协作）

**问题**：安装由 C# 编排，但下载在 Rust；模块又不能直接互调。

**方案**：C# 在结果中返回一个“效果”，C++ 的 `EffectRunner` 执行后，把结果作为 continuation 回调给 C#。

```json
{
  "ok": true,
  "data": { "phase": "download", "planId": "8f2c..." },
  "effect": {
    "type": "download",
    "plan": { "schemaVersion": 1, "planId": "8f2c...", "root": "...", "files": [] },
    "continuation": "opaque-token-from-csharp"
  }
}
```

C++ 行为：

1. 识别 `effect.type == "download"`；
2. 把 `plan` 提交给 `archoera_dl`（同一 taskId 或子 taskId，父任务进入 `WaitingEffect`）；
3. 转发 Rust 的进度事件（带父 taskId，标记 `source=download`）；
4. 下载完成/失败后，调用 C# `task.continue`，参数：
   ```json
   { "continuation": "opaque-token", "result": { "status": "completed", "failed": [] } }
   ```
5. C# 继续校验/解压/提交，最终返回 `ok` 或错误。

**规则**

- C++ **不解析** `plan` 的业务字段，只做 schema/大小/路径根校验与路由。
- `continuation` 对 C++ 不透明；C# 自己保证可重放/幂等。
- v1 只支持 `download` 效果；其他效果（如 `pending_processors`）由 C# 内部完成。
- 效果失败：C++ 把 Rust 结果原样回调 C#，由 C# 决定重试/回滚/报错。
- 效果取消：取消父任务 → 取消 Rust 任务 → 回调 C# `CANCELLED`。
- 效果链最多 8 段；超过报 `E_INTERNAL`，防止 C#/C++ 循环。

### 5.8 取消、超时与幂等

| 机制 | 规则 |
|---|---|
| 取消 | 协作式；每个长任务检查 token；C# `CancellationToken`、Rust `CancellationToken` |
| 超时 | 每个 task 可设 deadline；到期转 `Cancelled` 并记录 `TIMEOUT` 原因 |
| 子任务 | 父取消级联；子完成不自动完成父（由编排逻辑决定） |
| 幂等 | `planId` 重复提交复用；同一 `dest` 加锁；`cancel` 对终态返回 OK |
| 结果保留 | 终态结果保留 10 分钟或直到 `release`；大结果分页/落临时文件 |

### 5.9 事件泵与背压

- 事件有单调 `seq`（uint64），进程内唯一。
- `TASK_STATE` 与终态结果不可丢；`TASK_PROGRESS` 可合并（同一 task 只保留最新）。
- `LOG` 进入环形缓冲（默认 8 MiB/通道），`poll` 只取增量；`log.tail` 可分页读历史。
- **事件驱动，不做定时轮询**：C++ 在入队后调用 `wake()`；Dart 用 `NativeCallable.listener` 接收，收到后批量 `poll(64)` drain，直到队列空或达到预算。
- 禁止 `Timer.periodic` / 固定间隔轮询；`wake()` 只做“有事件”的合并通知，drain 由事件触发。
- 生产端超过高水位（默认 4096 条）时：
  - 进度事件合并；
  - 日志丢弃 `DEBUG/TRACE` 并计数；
  - 状态事件永不丢，必要时阻塞生产端（短临界区）。
- 进度在 C++/Rust 侧按 100–200ms 聚合；UI 再按帧节流。

### 5.10 热重启（attach/detach）

```
Flutter 热重启
  ├─ 旧 UI detach：停止投递、保存 last_seq
  ├─ 原生任务继续；事件仍入队（状态事件保留，进度合并）
  └─ 新 UI attach(last_seq)：
       ├─ 返回当前 seq 与活跃任务快照
       ├─ 从 last_seq 之后重放仍保留的事件（若未溢出）
       └─ 之后正常 poll
```

- `attach` 后必须重新 `set_wakeup`（旧 NativeCallable 已失效）。
- `detach` 不取消任务、不卸载模块。
- 事件队列溢出时，`attach` 以快照为准；UI 必须能“全量刷新”。
- 应用真正退出才 `shutdown`。

### 5.11 模块宿主

| 阶段 | 行为 |
|---|---|
| 发现 | 只扫描应用目录 `modules/` 下的固定文件名；不接受任意路径 |
| 加载 | `LoadLibraryExW(..., LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR)` / `dlopen`（`RTLD_NOW` + `RTLD_LOCAL`） |
| 握手 | 取 `archoera_module_entry()`；校验 ABI 主版本、名称、模块版本 |
| 初始化 | 传 host 函数表 + 配置；失败则关键模块拒绝启动 |
| 运行 | 只通过函数表调用；回调只入队 |
| 关闭 | 调 `shutdown`；**不** `FreeLibrary/dlclose` |
| 诊断 | `module.info` 返回名称/版本/能力/状态 |

**为什么不能卸载**：NativeAOT 运行时、.NET 线程池、tokio runtime、Crashpad 都可能持有线程/静态状态；`dlclose` 后回调会踩空。模块生命周期 = 进程生命周期。

### 5.12 崩溃处理

- C++ 安装 Crashpad（Windows SEH、Linux signal、macOS Mach exception）。
- 崩溃时：写 minidump、flush 日志、记录模块版本/当前任务/最近事件（脱敏）。
- .NET 未捕获异常在 ABI 边界 catch；`Environment.FailFast`/`StackOverflow` 等不可捕获，交给进程级 handler。
- 下次启动检测 `crashes/*.dmp`，展示“上次崩溃”并允许导出；上传需 opt-in。
- minidump 可能含 token/路径：默认本地，上传前提示并脱敏文件名。

### 5.13 配置分层

```
内置默认值
  < 安装目录 policy（机器级，只降权）
  < 用户 config.json（可手改）
  < 实例 instance.json（实例级）
  < 本次启动参数/环境（仅 Debug/CI，发布版不提供升权开关）
```

- `config.json` 用 JSON Schema 校验；未知字段保留并警告。
- 合规配置独立：`compliance.json`（用户手写）+ `state/consent.json`（应用写）+ `audit/*.jsonl`。
- 配置写入原子化：`.tmp` → fsync → rename；保留 `.bak`。
- 任何“升权”配置（下载等级）只让 UI 出现确认入口，不直接生效。

### 5.14 平台服务

| 能力 | 实现 | 说明 |
|---|---|---|
| 单实例 | C++ 命名互斥/文件锁 + 本地 socket | 第二实例唤醒第一个并传参 |
| 托盘 | C++ 原生 API | 最小化到托盘、菜单、通知 |
| 深链 | C++ 注册协议 | 只接受白名单动作（导入/启动/登录回调） |
| 文件关联 | C++ 注册 | `.mrpack`/zip 导入；不自动执行 |
| 开机自启 | C++ | 注册表/LaunchAgent/.desktop |
| 窗口 | Flutter 插件 + C++ 平台调用 | 无边框、Mica/Acrylic、DPI |
| 打开路径/URL | C++ | 只允许 `https` 白名单与本地路径；禁止 `file:` 任意执行 |
| 通知 | C++/系统 API | 下载完成、崩溃、更新 |

### 5.15 错误映射

```
模块错误（domain code + message + details）
  → C++ 归一化为 ABI status + JSON error
  → Dart CoreException(status, code, message, details, taskId)
  → UI 错误面板（可重试/去官网/看日志/导出诊断）
```

- 领域错误码保留在 `error.code`（如 `PROVIDER_AUTH`、`HASH_MISMATCH`）；
- ABI status 只表达大类（网络/磁盘/取消/策略/内部）；
- 禁止把原始异常栈直接展示给用户；日志保留。

---

## 6. 对外 ABI（v1）

> 本节是 **规范性文本**。`docs/07` §3 的早期草案与本节目冲突时，以本节为准。
> 头文件最终落在 `contracts/abi/archoera_abi.h`；本文与头文件必须同步。

### 6.1 设计原则

1. 只暴露 C：不导出 C++ 类、STL、异常、`std::string`。
2. 不透明句柄：`typedef struct archoera_core archoera_core;`
3. 调用方分配、提供长度：小字符串两段式（查长度 → 填充）。
4. 谁分配谁释放：跨边界字符串必须由分配方提供生命周期说明。
5. 错误不抛异常：返回 `int32_t` 状态码 + 错误结构。
6. 版本可查询：ABI 版本 + 构建信息 + 每模块版本。
7. 错误按线程隔离：每个线程一份，避免并发覆盖。
8. 所有调用立即返回：长任务返回 taskId，结果经事件/`archoera_result`。
9. 不信任跨边界数据：空指针、长度、UTF-8、JSON 深度/大小全部校验。

### 6.2 头文件

```c
#pragma once
#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

#define ARCHOERA_ABI_VERSION 1u

typedef struct archoera_core archoera_core;

/* ---------- 状态码 ---------- */
typedef enum {
  ARCHOERA_OK            = 0,
  ARCHOERA_E_INVALID     = 1,   /* 参数/状态非法 */
  ARCHOERA_E_NOT_FOUND   = 2,
  ARCHOERA_E_AUTH        = 3,
  ARCHOERA_E_NETWORK     = 4,
  ARCHOERA_E_DISK        = 5,
  ARCHOERA_E_CANCELLED   = 6,
  ARCHOERA_E_UNSUPPORTED = 7,
  ARCHOERA_E_INTERNAL    = 8,
  ARCHOERA_E_BUFFER      = 9,   /* 缓冲区不足；out_len 回填所需长度 */
  ARCHOERA_E_BUSY        = 10,  /* 同一资源被占用/任务在跑 */
  ARCHOERA_E_POLICY      = 11   /* 合规/渠道策略拒绝 */
} archoera_status;

typedef struct {
  int32_t     code;        /* archoera_status */
  const char* message;     /* UTF-8；属于 core，直到本线程下一次调用 */
  const char* detail;      /* 可为 NULL；同上 */
} archoera_error;

/* ---------- 任务状态 ---------- */
typedef enum {
  ARCHOERA_TASK_QUEUED    = 0,
  ARCHOERA_TASK_RUNNING   = 1,
  ARCHOERA_TASK_WAITING   = 2,   /* 等待效果（如下载） */
  ARCHOERA_TASK_COMPLETED = 3,
  ARCHOERA_TASK_FAILED    = 4,
  ARCHOERA_TASK_CANCELLED = 5
} archoera_task_state;

/* ---------- 事件 ---------- */
typedef enum {
  ARCHOERA_EV_TASK_STATE    = 1,
  ARCHOERA_EV_TASK_PROGRESS = 2,
  ARCHOERA_EV_LOG           = 3,
  ARCHOERA_EV_DOMAIN        = 4,   /* 实例/账户/下载/合规等领域事件 */
  ARCHOERA_EV_MODULE        = 5    /* 模块加载/握手/降级 */
} archoera_event_type;

typedef struct {
  int32_t     type;          /* archoera_event_type */
  int32_t     state;         /* TASK_STATE 时有效；否则 0 */
  int32_t     code;          /* 领域提示码；0 表示无 */
  uint32_t    flags;         /* 保留，必须为 0 */
  uint64_t    seq;           /* 进程内单调递增 */
  uint64_t    task_id;       /* 0 表示非任务事件 */
  int64_t     done;          /* 进度：已完成量 */
  int64_t     total;         /* 进度：总量；0 表示未知 */
  int64_t     bytes_per_sec; /* 进度：瞬时速度；0 表示未知 */
  const char* json;          /* UTF-8 负载；core 所有，直到下一次 poll */
} archoera_event;

/* ---------- 生命周期 ---------- */
uint32_t      archoera_abi_version(void);
const char*   archoera_build_info(void); /* 静态 JSON，进程生命周期有效 */

archoera_status archoera_init(const char* config_json,
                              archoera_core** out,
                              archoera_error* err);
void            archoera_shutdown(archoera_core* core);

/* ---------- 事件与 UI 连接 ---------- */
typedef void (*archoera_wake_fn)(void* user);

archoera_status archoera_set_wakeup(archoera_core* core,
                                    archoera_wake_fn fn,
                                    void* user);

/* attach/detach 用于 Flutter 热重启；last_seq=0 表示从当前开始 */
archoera_status archoera_attach(archoera_core* core,
                                uint64_t last_seq,
                                uint64_t* out_seq,
                                archoera_error* err);
archoera_status archoera_detach(archoera_core* core);

/* 返回事件条数（>=0）；-1 表示参数非法。只在 attach 的 UI 线程调用。 */
int32_t         archoera_poll(archoera_core* core,
                              archoera_event* out,
                              int32_t max);

/* ---------- 调用 ---------- */
archoera_status archoera_call(archoera_core* core,
                              const char* method,
                              const char* params_json,
                              uint64_t* out_task_id,
                              archoera_error* err);

archoera_status archoera_cancel(archoera_core* core, uint64_t task_id);

/* 两段式：buf=NULL 或 buf_len=0 时返回 OK 并回填 out_len（含 NUL）；
   缓冲区不足返回 E_BUFFER 并回填 out_len；成功写入 NUL 结尾 JSON。 */
archoera_status archoera_result(archoera_core* core,
                                uint64_t task_id,
                                char* buf,
                                size_t buf_len,
                                size_t* out_len,
                                archoera_error* err);

/* 任务结束后释放结果缓存；终态 TTL 默认 10 分钟，release 幂等。 */
void archoera_release(archoera_core* core, uint64_t task_id);

/* 活跃任务快照（JSON 数组），用于热重启/诊断；两段式。 */
archoera_status archoera_snapshot(archoera_core* core,
                                  char* buf,
                                  size_t buf_len,
                                  size_t* out_len,
                                  archoera_error* err);

/* 模块信息（JSON），index 从 0 开始；两段式。 */
archoera_status archoera_module_info(archoera_core* core,
                                     int32_t index,
                                     char* buf,
                                     size_t buf_len,
                                     size_t* out_len,
                                     archoera_error* err);

#ifdef __cplusplus
}
#endif
```

> 命名统一为 `ARCHOERA_*`（例如 `ARCHOERA_ABI_VERSION`、`ARCHOERA_OK`、`ARCHOERA_E_INVALID`）。头文件、C# 导出、Rust 导出必须逐字一致，不能出现第二套前缀。

### 6.3 状态码语义

| 状态 | 使用场景 | UI 动作 |
|---|---|---|
| `OK` | 成功/已受理 | 继续 |
| `E_INVALID` | 参数、JSON、状态非法 | 报 bug/修正输入 |
| `E_NOT_FOUND` | 实例/账户/版本/文件不存在 | 空态/引导创建 |
| `E_AUTH` | 登录失效、需要重新认证 | 重新登录 |
| `E_NETWORK` | DNS/TLS/超时/HTTP 5xx | 重试/检查代理 |
| `E_DISK` | 磁盘满/权限/占用/路径过长 | 清理/换目录/关游戏 |
| `E_CANCELLED` | 用户取消/超时/关闭 | 静默或提示 |
| `E_UNSUPPORTED` | 未知 method/平台/格式 | 升级/换方案 |
| `E_INTERNAL` | 未分类异常 | 导出诊断 |
| `E_BUFFER` | 两段式缓冲区不足 | 扩容重试 |
| `E_BUSY` | 同一资源已有任务 | 等待/取消旧任务 |
| `E_POLICY` | 合规/渠道拒绝 | 说明面板 |

### 6.4 任务与结果

- `archoera_call` 成功时 `out_task_id` **恒 > 0**；即使同步完成也入队 `Completed` 事件。
- `archoera_call` 立即失败时返回非 `OK`，`out_task_id=0`，`err` 填具体原因。
- `archoera_cancel` 对终态/已取消任务返回 `OK`（幂等）；未知任务返回 `E_NOT_FOUND`。
- 结果读取顺序：
  1. 收到 `TASK_STATE(state=COMPLETED/FAILED/CANCELLED)`；
  2. 调 `archoera_result` 两段式读取；
  3. 调 `archoera_release`。
- 结果 JSON 是方法定义的 envelope：
  ```json
  { "ok": true, "data": { }, "meta": { "schemaVersion": 1, "requestId": "..." } }
  ```
  失败：
  ```json
  { "ok": false, "error": { "code": "RATE_LIMITED", "message": "...", "retryable": true, "details": { } } }
  ```
- `archoera_result` 对非终态任务返回 `E_BUSY`；未知/已释放任务返回 `E_NOT_FOUND`。
- 大结果（> 8 MiB）建议方法分页；`archoera_result` 仍可返回，但 UI 应避免一次拉取。

### 6.5 事件负载

| 事件 | `json` 内容 | 合并 |
|---|---|---|
| `TASK_STATE` | `{ "method": "...", "phase": "...", "message": "..." }` | 不合并 |
| `TASK_PROGRESS` | `{ "phase": "download", "filesDone": 12, "filesTotal": 100, "currentFile": "..." }` | 同 task 合并 |
| `LOG` | `{ "level": "info", "target": "...", "message": "..." }` | 可丢 DEBUG/TRACE |
| `DOMAIN` | `{ "event": "instance.changed", "data": { } }` | 按 key 合并（可选） |
| `MODULE` | `{ "module": "archoera_dl", "state": "degraded", "message": "..." }` | 不合并 |

- 进度事件同时填固定字段 `done/total/bytes_per_sec`，避免 Dart 为进度解析 JSON。
- `json` 指针只在本次 `poll` 到下一次 `poll` 之间有效；Dart 必须立即复制字符串（`Utf8` 转 Dart String）。
- `seq` 用于热重启重放；事件队列溢出时以 `snapshot` 为准。

### 6.6 attach/detach 语义

| 调用 | 线程 | 行为 |
|---|---|---|
| `set_wakeup` | UI | 保存回调；旧回调在 detach 后失效 |
| `attach(last_seq)` | UI | 返回当前 seq；尝试重放 `last_seq` 之后仍保留的事件；不自动 poll |
| `poll` | UI | 取最多 `max` 条；返回 0 表示暂时无事件 |
| `detach` | UI | 停止向该 UI 投递；任务继续；不释放结果 |

- `attach` 必须与 `set_wakeup` 配对；新 UI 先 `set_wakeup` 再 `attach`。
- `attach` 可重复调用；以最后一次为准。
- `detach` 后 `poll` 返回 0；不报错。

### 6.7 内存与生命周期

| 数据 | 分配方 | 释放方 | 有效期 |
|---|---|---|---|
| 入参 `method/params_json` | 调用方 | 调用方 | `call` 返回后即可释放 |
| `archoera_error.message/detail` | core | core | 本线程下一次 ABI 调用前 |
| 事件 `json` | core | core | 本次 poll 到下一次 poll |
| 结果缓冲 | 调用方 | 调用方 | 调用方管理；core 只写 |
| `build_info` | core | core | 进程生命周期 |
| 模块回调结果 | 模块 | 模块 | 回调返回前；C++ 必须复制 |

**铁律**：谁分配谁释放；跨边界字符串必须带长度；不允许“我 new 你 delete”。

### 6.8 版本兼容

- `archoera_abi_version()` 主版本不同 → Dart 拒绝加载并提示升级。
- 次版本只允许“新增可选字段/新增 method”，不得改变既有字段语义。
- 未知 method → `E_UNSUPPORTED`；未知事件类型 → UI 忽略并记录。
- 模块 ABI 与对外 ABI 独立版本；两者都要握手。
- 发布时 `build-info.json` 写入应用版本、ABI 版本、模块版本、构建时间、git commit。

### 6.9 C harness（必须存在）

```c
/* tests/abi/harness.c */
int main(void) {
  archoera_core* core = NULL;
  archoera_error err = {0};
  assert(archoera_abi_version() == ARCHOERA_ABI_VERSION);
  assert(archoera_init("{\"safeMode\":true}", &core, &err) == ARCHOERA_OK);
  uint64_t task = 0;
  assert(archoera_call(core, "system.ping", "{}", &task, &err) == ARCHOERA_OK);
  /* poll 到 Completed；两次 result；release；shutdown */
  archoera_shutdown(core);
  return 0;
}
```

覆盖：空指针、超长字符串、非法 UTF-8、重复 cancel、shutdown 后调用、结果缓冲不足、attach/detach 交错。

---

## 7. 模块协议（内部 ABI）

> 对外 ABI 是“Dart ↔ C++”；模块协议是“C++ ↔ C#/Rust”。两者都走 C，但版本与函数表独立。

### 7.1 模块函数表

```c
#pragma once
#include <stdint.h>
#include <stddef.h>
#include "archoera_abi.h"   /* 复用 archoera_status / archoera_error */

#ifdef _WIN32
  #define ARCHOERA_EXPORT __declspec(dllexport)
#else
  #define ARCHOERA_EXPORT __attribute__((visibility("default")))
#endif

typedef void (*archoera_module_done_fn)(void* user,
                                        int32_t status,
                                        const char* result_json,
                                        const char* error_json);

typedef struct archoera_module_host_v1 {
  uint32_t abi_version;
  void*    ctx;

  /* 线程安全；message 只在该调用期间有效 */
  void (*log)(void* ctx, int32_t level, const char* target, const char* message);

  /* 线程安全；event_json = { "type": "...", "data": { } }；taskId 由 core 填 */
  void (*emit)(void* ctx, uint64_t task_id, const char* event_json);

  /* 单调毫秒，用于超时/进度 */
  uint64_t (*now_ms)(void* ctx);
} archoera_module_host_v1;

typedef struct archoera_module_v1 {
  uint32_t    abi_version;
  const char* name;        /* "archoera_cs" / "archoera_dl" */
  const char* version;     /* SemVer */
  uint32_t    capabilities;/* 位图；见 7.4 */

  int32_t (*init)(const archoera_module_host_v1* host,
                  const char* config_json,
                  void** out_self,
                  archoera_error* err);

  /* 立即返回；异步结果经 done 恰好一次。done 可在 call 返回前同步调用。 */
  int32_t (*call)(void* self,
                  uint64_t task_id,
                  const char* method,
                  const char* params_json,
                  archoera_module_done_fn done,
                  void* user);

  /* 幂等；cancel 后 done 仍会以 CANCELLED 到达 */
  int32_t (*cancel)(void* self, uint64_t task_id);

  /* 阻塞直到所有回调结束；之后不得再回调 */
  void (*shutdown)(void* self);

  /* 活跃任务快照 JSON；两段式；可为 NULL */
  int32_t (*snapshot)(void* self, char* buf, size_t buf_len, size_t* out_len);
} archoera_module_v1;

ARCHOERA_EXPORT const archoera_module_v1* archoera_module_entry(void);
```

### 7.2 调用与回调规则

1. `call` 返回非 `OK` 表示**未受理**，不会再有 `done`。
2. `call` 返回 `OK` 表示**已受理**，`done` 必须恰好调用一次。
3. `done` 可来自任意线程；C++ 只复制字符串并入队，不在回调里做长逻辑。
4. `result_json` / `error_json` 只在 `done` 返回前有效；模块可在回调返回后释放。
5. `cancel` 是协作式；模块应尽快停止并回调 `CANCELLED`。
6. `shutdown` 返回后，模块不得再调用 `done`/`emit`/`log`。
7. 模块不得阻塞 Hub 线程；所有 I/O 在自己的线程池/异步运行时。
8. 模块不得调用其他模块；需要协作时返回效果（§5.7）。

### 7.3 模块初始化配置

```json
{
  "schemaVersion": 1,
  "appVersion": "0.1.0",
  "dataDir": "/home/u/.local/share/archoera",
  "cacheDir": "/home/u/.cache/archoera",
  "logDir": "/home/u/.cache/archoera/logs",
  "channel": "official-global",
  "locale": "zh-Hans",
  "safeMode": false,
  "proxy": { "mode": "system", "url": null, "noProxy": [] },
  "capabilityKey": "<base64 32 bytes；进程级随机，不落盘/不记日志>",
  "limits": { "maxLogBytes": 8388608, "maxJsonBytes": 67108864 }
}
```

- 路径由 C++ 解析为绝对路径后传入；模块不得自行猜测。
- 代理由 C# 解析后写入运行期配置，再传给 Rust；Rust 不重复读系统代理（避免两套行为）。
- `safeMode` 下不加载 Rust、不启动网络任务，用于排障。

### 7.4 能力位

| 位 | 名称 | 说明 |
|---|---|---|
| 1 | `CAP_INSTANCE` | 实例 CRUD/导入导出 |
| 2 | `CAP_JAVA` | Java 探测/运行时 |
| 3 | `CAP_VERSION` | 版本清单/合并/安装 |
| 4 | `CAP_ACCOUNT` | 登录/账户 |
| 5 | `CAP_RESOURCE` | 搜索/详情/更新 |
| 6 | `CAP_MOD` | Mod 扫描/启停 |
| 7 | `CAP_MODPACK` | 整合包 |
| 8 | `CAP_LAUNCH` | 启动/日志 |
| 9 | `CAP_DOWNLOAD` | 下载 |
| 10 | `CAP_COMPLIANCE` | 合规门控 |
| 11 | `CAP_CRASH` | 崩溃分析 |

- 握手时校验：关键能力缺失 → 启动失败；可选能力缺失 → 降级。
- 方法路由按能力过滤；未声明能力的方法返回 `E_UNSUPPORTED`。

### 7.5 模块清单

```json
{
  "name": "archoera_cs",
  "version": "0.1.0",
  "abiVersion": 1,
  "capabilities": ["instance", "java", "version", "account", "resource", "mod", "modpack", "launch", "compliance", "crash"],
  "methods": ["instance.list", "java.scan", "version.install", "launch.start"],
  "build": { "commit": "abc1234", "date": "2026-10-20T12:00:00Z" }
}
```

- 清单可编译进模块（`module.info` 返回），也可作为同目录 `.json` 供诊断；两者不一致时以模块返回为准并告警。
- 当前实现（v1 切片）先冻结 **方法注册表**：C++ `src/method_registry.cpp` 与 C# `BusinessModuleRegistry` 各自显式登记方法；能力位/清单文件在接入 Rust 模块时补齐。

### 7.6 C# 导出层约束

- 只有 `Archoera.Exports` 发布程序集里的 `[UnmanagedCallersOnly]` 方法会被导出。
- 只能 `static`；不能泛型/引用类型参数；字符串用 UTF-8 指针 + 长度。
- 所有异常在边界 catch，转成 `archoera_status` + JSON error；AOT 下未捕获异常 = 进程终止。
- 调用约定统一 `CallConvCdecl`；Windows 头文件与导出必须一致。
- 业务逻辑放 `Archoera.Domain/Application`，导出层只做“解参数 → 调服务 → 装结果”。
- `System.Text.Json` 源生成；禁止运行时反射/动态程序集。

### 7.7 Rust 导出层约束

- `crate-type = ["cdylib"]`；`#[unsafe(no_mangle)] pub unsafe extern "C" fn`。
- 每个导出函数 `catch_unwind`；panic → `E_INTERNAL`，不让展开跨边界。
- 不跨边界传 `&str/String/Vec`；用指针 + 长度。
- 句柄 `Box::into_raw/from_raw`；明确所有权。
- 回调只投递到 C++ 队列；不直接回调 Dart。
- tokio runtime 只建一次；取消用 `CancellationToken`。
- 模块 `shutdown` 等待 JoinSet，超时 abort，但 `.part.meta` 必须先写完。

---

## 8. C# 业务核心（archoera_cs）

### 8.1 分层与项目

```
app/modules/business/
├─ Archoera.Domain/          # 纯模型/规则：无 I/O、无 HttpClient、无 SQLite
├─ Archoera.Application/     # 用例/服务接口/编排：可单测
├─ Archoera.Infrastructure/  # HTTP、SQLite、文件、注册表、进程、凭据库
├─ Archoera.Exports/         # NativeAOT 导出层（唯一发布程序集）
└─ Archoera.DevHost/         # 普通 .NET 控制台，JIT 跑同一套逻辑
```

依赖方向：

```
Exports ──▶ Application ──▶ Domain
   │              ▲
   └──▶ Infrastructure ──┘
```

- `Domain` 不引用 `System.Net.Http`、`Microsoft.Data.Sqlite`、`Microsoft.Win32.*`。
- `Application` 只依赖接口（`IHttpClient`、`IInstanceStore`、`IJavaLocator` 等）。
- `Infrastructure` 实现接口；组合根在 `Exports`/`DevHost`。
- `DevHost` 用于 JIT 调试、CLI、CI；AOT 只发布 `Exports`。

### 8.2 服务清单

| 服务 | 职责 | 写入 |
|---|---|---|
| `InstanceService` | CRUD、复制、导入导出、对账 | `instance.json` + SQLite |
| `JavaService` | 探测、版本解析、运行时安装/校验 | `java_runtimes` + 文件 |
| `VersionService` | 清单缓存、版本 JSON、合并、有效版本 | `version_meta`/`effective_versions` |
| `InstallService` | 原版/加载器安装、计划、校验、事务 | 实例目录 + 共享库 |
| `LaunchService` | 启动计划、JVM spawn、日志转发、退出 | 进程 + 日志 |
| `AccountService` | 微软/离线/第三方、token 生命周期 | `accounts` + 凭据库 |
| `ResourceService` | 搜索/详情/版本/更新检查 | `resource_*` |
| `ModService` | 扫描、元数据、启停、重复检测 | `local_mods` + 文件 |
| `ModpackService` | 格式探测、解析、导入/导出 | 实例 + SQLite |
| `LoaderService` | Fabric/Quilt/Forge/NeoForge 安装 | 共享库/版本 |
| `CrashService` | 收集日志/崩溃报告、规则匹配 | `crash_reports` |
| `ComplianceService` | 门控求值、同意、审计、capability | `state/consent`/`audit`；`compliance.json` 只读（仅降权写） |
| `StorageService` | SQLite 连接/迁移/事务/备份 | `archoera.db` |
| `HttpService` | 每 provider 的 HttpClient、代理、UA、重试 | 无 |
| `SettingsService` | 配置读写、schema 校验 | `config.json`/`settings` |
| `DiagnosticsService` | 诊断包、模块信息、日志导出 | 无（只读） |

### 8.3 领域模型

```csharp
public sealed record Instance(
    string Id, string Name, string GameVersion, LoaderRef Loader,
    JavaSelection Java, MemorySettings Memory, WindowSettings Window,
    IReadOnlyList<string> JvmArgs, IReadOnlyList<string> GameArgs,
    string? IconPath, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record LoaderRef(string Kind, string? Version); // vanilla|fabric|quilt|forge|neoforge|optifine
public sealed record JavaSelection(string Mode, int? Major, string? Path); // auto|managed|manual
public sealed record MemorySettings(int MinMb, int MaxMb);
public sealed record WindowSettings(int Width, int Height, bool Fullscreen);

public sealed record VersionLayer(string Id, string Role, string ContentSha256, JsonElement Json);
public sealed record EffectiveVersion(
    string Id, IReadOnlyList<string> Chain, string MainClass,
    IReadOnlyList<Library> Libraries, IReadOnlyList<Arg> JvmArgs,
    IReadOnlyList<Arg> GameArgs, AssetIndex? AssetIndex, JavaVersion? JavaVersion,
    LoggingConfig? Logging, IReadOnlyList<MergeWarning> Warnings);

public sealed record DownloadPlan(
    int SchemaVersion, string PlanId, string Root, int Concurrency,
    long TotalBytes, IReadOnlyList<PlanFile> Files, IReadOnlyList<PostAction> Post);

public sealed record PlanFile(
    string Url, IReadOnlyList<string> Mirrors, HashSpec Hash, long Size,
    string Dest, string Mode, int Priority, bool Executable);

public sealed record LaunchPlan(
    string InstanceId, string JavaPath, string WorkingDir, string MainClass,
    IReadOnlyList<string> Classpath, IReadOnlyList<string> JvmArgs,
    IReadOnlyList<string> GameArgs, IReadOnlyDictionary<string,string> Env,
    string NativesDir, string? LoggingConfig, int ExpectedExitCode);
```

### 8.4 版本合并与有效版本

**唯一生产者**：C#。任何其他模块都不得再实现一套合并。

规则摘要（详见 `08/01-version-merge.md`）：

1. 链：`root → ... → leaf`；`inheritsFrom` 最多 8 层；检测环。
2. 标量：叶子覆盖；`minimumLauncherVersion`/`complianceLevel` 取链上最大值。
3. libraries 键：`group:artifact:classifier:extension`；版本不参与键；叶子版本胜出、**位置不变**。
4. `arguments.jvm/game`：父→子拼接；`rules` 与 `${}` 原样保留，**启动时**求值。
5. 无 `downloads` 的库：按 Maven 坐标拼 URL；hash 可缺失，标记 `unverified`。
6. `url` 为空的库：本地 processor 产物，**不下载**，启动前检查存在。
7. 缓存键：`mergeSchemaVersion + 每层 id/contentHash + 合并选项`；Fabric `releaseTime` 不参与。
8. 有效版本缓存放 `<cache>/effective/`，不放 `versions/`，避免被“修复版本”误删。

**启动时求值**：`rules`（os/arch/features）与 `${}` 占位符在构建 `LaunchPlan` 时解析；合并阶段不因当前 OS 过滤库。

### 8.5 安装流水线（原版/加载器）

```
1. Resolve   版本清单/版本 JSON/加载器 profile
2. Merge     EffectiveVersion（C# 唯一）+ LoaderArtifactManifest
3. Plan      DownloadPlan + C# 后处理（post）+ 目标实例
4. Effect    C++ 把 plan 交给 Rust；进度/取消
5. Verify    逐文件 hash/size；失败按策略换源/重试
6. Post      C# 解压 natives、chmod、必要复制
7. Loader    运行 processors；逐 output 校验；写 install receipt
8. Commit    staging → 实例目录；shared 库原子落位
9. Register  SQLite upsert + instance.json 原子写
```

**事务边界**：

- 新实例：下载/解压到 `instances/.staging/<txid>/`；完成后 `rename` 到 `instances/<id>`；目标必须不存在。
- 合并/修复：逐文件 journal（backup → replace → fsync）；失败逆序回滚。
- 共享库可先落位（内容寻址、幂等）；失败不影响活动实例，残留由 GC 清理。
- 加载器 processors：输出写 staging；每个 output 按 `install_profile.outputs` 校验；失败保留 store、删 staging。
- 加载器生成物：不能只信 `processors[].outputs`。Forge 1.20.1 / NeoForge 21.1 的 `PATCHED` 既不在 `version.json.libraries`，也不在 `outputs`；必须由 `LoaderArtifactManifest` 从 `data.PATCHED` 显式记录，并用 `producerIndex` 绑定。详见 [08/09-forge-generated-artifacts.md](08-technical-research/09-forge-generated-artifacts.md)。
- 修复：按 manifest 的 artifact 集合验证；从最早缺失 artifact 的 `producerIndex` 开始重跑 processor，跳过已有效输出；重跑后逐项校验 hash/size，再提交 receipt。
- 注册失败：实例目录已提交但 SQLite 写失败 → 启动扫描 `instances/*/instance.json` 收养孤儿。

### 8.6 启动流水线

```
1. 读取实例 + 有效版本（缓存/重建）
2. 解析 Java：用户指定 → 托管 runtime → Mojang runtime → 系统 PATH/注册表
3. 构建 LaunchPlan：
   - classpath（按 rules 过滤、去重、顺序稳定）
   - natives 目录（按版本/架构解压，校验）
   - JVM/game 参数（rules + ${} 展开）
   - 账户占位符（token 只在 C# 内拼进 argv）
   - logging.client 配置（Log4Shell）：必须使用版本 JSON 提供的配置，不自行拼 `-Dlog4j.configurationFile`
   - authlib-injector（可选）
4. 预检：Java 版本、生成物 manifest（含未列出的 `PATCHED`）、文件存在/hash、磁盘、内存、路径长度
   - 生成物缺失/损坏 → 先自动 `loader.repair`（从最早缺失 processor 重跑）；未通过前不 spawn JVM
5. spawn JVM：argv 数组、无 shell、清空 JAVA_TOOL_OPTIONS/_JAVA_OPTIONS/CLASSPATH
6. 转发 stdout/stderr（编码探测、行缓冲、环形缓冲、事件节流）
7. 等待退出：记录 exit code；收集 crash-report/hs_err；触发崩溃分析
```

- `launch.start` 在进程成功 spawn 后返回 `{pid, startedAt}`，不等待游戏退出。
- `launch.stop`：先请求优雅退出（如支持），5s 后杀进程树（Windows Job Object / Unix 进程组）。
- 命令行包含 token，属于官方启动器同样存在的限制；**绝不**把完整 argv 写日志/显示在 UI。

### 8.7 Java 探测与运行时

```
JavaLocator
  ├─ 1. 用户指定 JRE（校验 major）
  ├─ 2. 托管 runtime：<data>/runtime/java-<major>/
  ├─ 3. Mojang java-runtime manifest（按 version.json javaVersion.component）
  └─ 4. 系统 PATH / 注册表 / /usr/libexec/java_home（兜底）
```

- 探测：`java -XshowSettings:properties -version`，解析 `java.version`/`java.home`/`java.vendor`/`os.arch`；超时 5s。
- 候选：`JAVA_HOME`、PATH、`/usr/lib/jvm`、`/opt/java`（含 `vendor/jdk` 嵌套）、`/opt`、Windows 注册表、macOS `java_home`；详见 `08/08-java-scan.md`。
- 缓存：按路径 + mtime + size 缓存版本；PATH 变化时失效。
- 结果：`java.scan` 经 C++ ModuleRegistry 调 C# `archoera_cs`；按 `java.home` 归并 symlink/JRE 别名，按主版本排序；UI 默认显示前 5 个，支持“查看全部”。
- 手动路径：`java.scan {roots, executables, includeDefaults}`；默认扫描为空时 UI 提示“选择目录搜索”，仍为空再提示“选择 JDK 目录 / java 可执行文件”；用户选择后用 `includeDefaults:false` 验证，路径持久化并随扫描带上。
- 安装：Mojang runtime 优先；Adoptium 作为可选源；校验 SHA-256/签名；不打包 Oracle JDK。
- 选择：`majorVersion` 必须匹配；Forge/NeoForge processors 用同一 JRE；不匹配时预检失败，不进入 processor。

### 8.8 账户与认证

| 类型 | 流程 | 存储 |
|---|---|---|
| 微软 | device code → XBL → XSTS → Minecraft Services → profile；可回退 auth code + loopback | refresh token 进 OS 凭据库；元数据进 SQLite |
| 离线 | 名称 + 可选 UUID；仅离线/局域网提示 | SQLite；不请求网络 |
| 第三方 | authlib-injector + Yggdrasil；`-javaagent` | 元数据 + 凭据库 |

- 公共客户端，不内嵌 client secret；自有 Entra 应用。
- OAuth 重定向：默认 **Microsoft Device Code Flow（无 redirect URI）**；浏览器备选用 **loopback + auth code + PKCE**（注册 `http://localhost/callback`，运行时 `http://localhost:<ephemeral>/callback`，仅绑定 `127.0.0.1`，校验 state/Host，短超时）。自定义协议不作为微软登录主方案。
- Entra 注册之外还需向 Minecraft 提交自有 client_id 审核（`aka.ms/mce-reviewappid`）；未获准时 `login_with_xbox` 会返回 `403 Invalid app registration`。
- token 刷新单飞（single-flight）；多账户切换取消旧刷新。
- 日志/崩溃/诊断包脱敏：Bearer、token、邮箱、UUID、路径、IP。
- 失败给“人话”：无 Xbox 账户、儿童账户未同意、地区限制、需要重新登录。

### 8.9 资源搜索与更新

- `IResourceProvider`：Modrinth / CurseForge；统一 DTO，UI 不感知来源。
- 兼容性按 **version 级** `loaders/game_versions` 精确过滤；项目级字段只用于 facets/展示。
- 缓存：搜索 60s、详情 10min、版本 5min、标签 24h；ETag/If-None-Match；single-flight。
- 离线：最近 50 个搜索 key×首页 + 500 个详情，LRU 20MB；返回 `stale/cachedAt`。
- 更新检查：Modrinth 批量 hash；CurseForge fingerprint + `latestFilesIndexes`；默认不自动更新。
- 依赖解析：深度 ≤ 8、去环、锁定解析时 fileId；可选默认不装。

### 8.10 Mod 扫描与启停

- 扫描 `mods/*.jar`、`.jar.disabled`；读 `fabric.mod.json`/`quilt.mod.json`/`META-INF/mods.toml`/`neoforge.mods.toml`。
- 校验 zip 条目路径；不执行 jar；解析失败只记录，不影响启动。
- 重复 modId、缺失依赖、版本冲突只提示，不自动解决。
- 启停用 `.disabled` 后缀；文件锁/占用时提示关闭游戏。
- 本地 hash 缓存 `(path,size,mtime) → sha1`；更新检查按 hash 反查。

### 8.11 整合包

- 格式探测：CurseForge zip、`.mrpack`、MultiMC/Prism、ATLauncher、HMCL server；嵌套最多 2 层。
- 解析为 `ModpackDescriptor`；路径规范化；overrides 层序；`env`/optional/disabled 处理。
- 下载：Rust；解压/后处理/事务：C#（见 `08/06-modpack-install.md`）。
- 安全：Zip Slip、保留名、ADS、大小写冲突、symlink、zip bomb 全部显式拒绝。
- 事务：staging → journal → 原子提交；失败回滚；删除的旧文件进 `.archoera/removed` 保留 30 天。
- 导出：`.mrpack`/CF zip/MultiMC；默认排除日志、崩溃、缓存、账户文件。

### 8.12 崩溃分析

- 数据源：exit code、stdout/stderr、`crash-reports/`、`hs_err_pid*.log`、启动器日志。
- 阶段模型：jvm → bootstrap → mixin → mod_init → graphics → game → world。
- 规则引擎：YAML 编译为 JSON；上下文门控 + 评分；输出人话、证据行、建议。
- 退出码不可靠；以 crash-report/`#@!@# Game crashed!`/`hs_err` 为准。
- 诊断包：脱敏日志尾部 + 规则结果 + 模块版本 + 任务快照；上传需 opt-in。

### 8.13 合规门控

- 唯一求值点：`ComplianceService.Evaluate()`；生成下载计划前必须通过。
- L0/L0-Z/L1/L2；构建期通道 + 机器策略 + 用户配置 + 同意；只能降不能升。
- capability：`HMAC-SHA256(进程随机 key, planId|level|exp|nonce)`；Rust 验签后才落盘。
- 审计 JSONL 哈希链；同意记录；撤回 `gateEpoch++`。
- 详细规则见 `08/07-compliance-gate.md` 与 `09`。

### 8.14 SQLite 与存储

- 唯一写入方；连接串 `Data Source=...;Mode=ReadWriteCreate;Pooling=True`；WAL；不使用 `Cache=Shared`（避免共享缓存锁竞争）。
- 写操作经 `SqliteWriteQueue` 串行化；读可用独立连接。
- 迁移：版本表 + 事务 + 迁移前备份；失败回滚，不删用户数据。
- 完整性：启动 `PRAGMA quick_check`；异常时备份并提示“重建索引”，从 `instance.json`/磁盘恢复。
- Schema 见 §11.4。

### 8.15 配置与密钥

- `config.json`：应用设置（主题、语言、下载限速、代理、Java 路径、日志级别）。
- `settings` 表：内部/可查询设置；与 `config.json` 的权威关系：用户可编辑项以 `config.json` 为准，运行态以 DB 为准。
- 凭据：Windows Credential Manager/DPAPI、macOS Keychain、Linux libsecret；无可用后端时 Windows 用 DPAPI 加密文件、Unix 用 0600 文件，并明确警告。
- CurseForge key：用户自备或独立网关；不进 UI/日志/仓库；不内置。

### 8.16 AOT 约束清单

| 限制 | 对策 |
|---|---|
| 无动态程序集加载 | 不做插件；模块编译期确定 |
| 反射受限 | `System.Text.Json` 源生成；避免 `Activator`/`Type.GetType` |
| 无托管调试器 | 业务纯类库 + DevHost + 单测 + 日志 |
| 体积 | 裁剪、`InvariantGlobalization` 评估（注意中文排序/日期） |
| 启动 | init 预热 JSON/HTTP/SQLite |
| SQLite native | `SQLitePCLRaw` bundle 随包；AOT 链接与目标 RID 实测 |
| 异常 | 边界 catch；禁止异常跨 ABI |

### 8.17 领域错误码

| 码 | 场景 | ABI 映射 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数/schema | `E_INVALID` |
| `INSTANCE_NOT_FOUND` | 实例不存在 | `E_NOT_FOUND` |
| `INSTANCE_BUSY` | 安装/启动中 | `E_BUSY` |
| `VERSION_NOT_FOUND` | 版本/加载器不存在 | `E_NOT_FOUND` |
| `JAVA_NOT_FOUND` / `JAVA_VERSION_MISMATCH` | Java 缺失/版本错 | `E_NOT_FOUND` / `E_UNSUPPORTED` |
| `AUTH_REQUIRED` / `AUTH_EXPIRED` | 未登录/失效 | `E_AUTH` |
| `PROVIDER_AUTH` / `RATE_LIMITED` / `PROVIDER_UNAVAILABLE` | API | `E_AUTH` / `E_NETWORK` |
| `PAGINATION_LIMIT` | CF 深分页 | `E_UNSUPPORTED` |
| `DISTRIBUTION_DENIED` | 作者禁转 | `E_POLICY` |
| `HASH_MISMATCH` / `DISK_FULL` / `FILE_LOCKED` | 下载/磁盘 | `E_DISK` |
| `DOWNLOAD_DISABLED` / `CONSENT_REQUIRED` / `CONSENT_STALE` | 合规 | `E_POLICY` |
| `CONFIG_INVALID` / `SCHEMA_UNSUPPORTED` | 配置 | `E_INVALID` / `E_UNSUPPORTED` |
| `INTERNAL` | 未分类 | `E_INTERNAL` |

---

## 9. Rust 下载器（archoera_dl）

### 9.1 职责与非职责

**做**：把 `DownloadPlan` 变成文件；分片、并发、限速、重试、镜像回退、hash 校验、断点续传、原子落盘、去重、进度聚合。

**不做**：生成 URL、理解 Mojang/Modrinth/加载器、**解压任何归档（含 natives/整合包）**、写 SQLite、决定“装什么”。

### 9.2 DownloadPlan 契约

```json
{
  "schemaVersion": 1,
  "planId": "8f2c9a...",
  "root": "C:/Users/x/AppData/Roaming/.archoera/instances/abc",
  "concurrency": 16,
  "priority": 0,
  "bandwidthLimit": 0,
  "totalBytes": 123456789,
  "options": {
    "dedup": true,
    "verifyExisting": "size",
    "durability": "normal",
    "rangeThreshold": 8388608,
    "chunkSize": 4194304,
    "progressIntervalMs": 150
  },
  "files": [
    {
      "url": "https://piston-data.mojang.com/.../client.jar",
      "mirrors": ["https://bmclapi2.bangbang93.com/..."],
      "hash": { "algo": "sha1", "hex": "0d1c..." },
      "size": 25165824,
      "dest": "versions/1.21.4/1.21.4.jar",
      "mode": "ensure",
      "priority": 0,
      "executable": false,
      "headers": {}
    }
  ],
  "post": []
}
```

> `post` 只是兼容字段；需要解压的 `extract_natives` 由 C# 在下载完成后的 continuation 中执行，避免第二套 Zip 安全实现。
>
> 当前 `archoera_dl` MVP 已实现单流 GET、hash/size 校验、`.part` + 原子 rename、进度/取消；Range 分片、全局配额、代理显式下发和 `post` 执行排在接入 C++ ModuleRegistry 之后。生成物（`PATCHED` 等）**永远不进** `files[]`。

| 字段 | 规则 |
|---|---|
| `root` | 绝对路径；canonicalize；所有 `dest` 必须落在其内。跨根需求拆成多个计划/效果 |
| `dest` | 相对路径；拒绝绝对路径、`..`、空段、NUL、Windows 保留名/ADS/尾随点空格 |
| `hash` | 必填；`sha1/sha256/sha512`；归一化旧字段 `sha1/sha256/sha512` |
| `size` | 必填（0 允许空文件）；用于预检/跳过/进度 |
| `url/mirrors` | 仅 HTTPS（测试允许 loopback）；拒绝 userinfo；mirror 去重 |
| `mode` | `ensure`/`replace`/`link`/`symlink`/`skip` |
| `headers` | 仅允许白名单（如 `Authorization` 由 C# 生成，短期有效） |
| `post` | 兼容字段；v1 Rust **不执行解压**。`extract_natives` 由 C# 在 continuation 中执行；Rust 仅可执行 `chmod/remove/copy`，未知类型报 `UNSUPPORTED_POST` |

**上限**：200k 文件、计划 JSON 64 MiB、单文件 1 TiB；解压总量/压缩比预算由 C# 约束，Rust 只校验下载大小。

### 9.3 校验与路径安全

1. 计划 JSON 解析后先做全量校验，失败即 `INVALID_PLAN`，不启动下载。
2. `root` canonicalize；`dest` 规范化后必须 `starts_with(root)`。
3. 逐级检查父目录：拒绝 symlink/reparse point/junction 逃逸。
4. Windows/macOS 大小写不敏感冲突检测；NFC 归一化。
5. 拒绝 dest 指向内容库内部、`.part/.meta/.lock` 自身、重复 dest。
6. 若未来增加解压型 `post`，必须同样检查绝对路径、`..`、symlink entry、超长路径、总量/压缩比；v1 不提供。
7. 威胁模型是“本地误操作 + 恶意整合包”，不是提权；但 TOCTOU 仍要尽量收紧（Unix `O_NOFOLLOW`、Windows `FILE_FLAG_OPEN_REPARSE_POINT` 可作为增强）。

### 9.4 调度与配额

| 层 | 默认 | 说明 |
|---|---|---|
| 全局 streams | 16（1–64） | 所有 plan 共享 |
| 每 host streams | 6（CDN）/4（源站） | host = 小写 hostname |
| 每文件 streams | 1 或 2–8 | 仅大文件且服务端支持 Range |

- 用单一 `Mutex<SchedState> + Notify` 的 permit broker，避免嵌套 Semaphore 持锁等待。
- plan 间按 `priority` + 轮转；文件 `priority` 只在 plan 内排序。
- 小块文件保留 1/4 全局许可，让 UI 快速出现“已完成 N 个”。
- 抢占只停止派发新 chunk，不 abort 在途大 chunk。
- 带宽限速令牌桶：每 64 KiB 取令牌，再读 socket，形成背压；低速时降到 16 KiB。

### 9.5 HTTP / TLS / 代理

| 项 | 策略 |
|---|---|
| 协议 | HTTPS；测试允许 `http://127.0.0.1` |
| HTTP 版本 | 默认 ALPN 协商 h2；小文件靠多路复用，大文件按阈值分片 |
| Range | `GET Range: bytes=0-0` 探测；仅 `206 + Content-Range` 认为支持 |
| 压缩 | 固定 `Accept-Encoding: identity`；禁用 gzip/brotli/zstd；响应仍带编码则 `ENCODING_UNSUPPORTED` |
| 重定向 | 最多 5 跳；逐跳校验 scheme；拒绝降级到 http |
| 超时 | connect 10s、TTFB 20s、读空闲 30s、单 chunk 5min、整 attempt 30min |
| 重试 | 指数退避 + jitter；429/503 尊重 `Retry-After`；TLS 证书错误不重试 |
| 代理 | **由 C# 解析后显式传入**；Rust 不重复读系统代理；支持 `noProxy`、代理认证 |
| TLS | Windows/macOS 用系统证书（native-tls）；Linux 用 rustls + 系统根；企业根需实测 |
| UA | `ArchoeraCX/<ver> (+https://...; contact)`；Rust 与 C# 同一格式 |

> 两套 HTTP 栈的差异是已接受的成本；代理/证书/UA/超时由 C# 统一下发，避免“C# 能搜、Rust 装不上”。
> **[待验证]** 系统代理（PAC/WPAD）、企业根证书、代理认证在三平台的实际行为，P1 必须用真实环境测试。

### 9.6 落盘、续传与原子提交

```
<dest.parent>/client.jar.part          # 数据，必须与 dest 同卷
<dest.parent>/client.jar.part.meta     # hash/size/etag/bitmap/validator
<dest.parent>/client.jar.part.lock     # 排他锁
```

- `.part` 不能放系统临时目录：跨卷 rename 会 `EXDEV`，失去原子性。
- 写入：`set_len(size)` + `write_at`；每 8–16 MiB 或 2s checkpoint（`sync_data` → bitmap → meta.tmp → fsync → rename）。
- 提交：`sync_all` → 顺序 hash → `rename(.part, dest)` → 父目录 fsync（Unix）→ 删 lock/meta。
- 断点：强 ETag 优先；弱 ETag 不能用于 `If-Range`；无 ETag 用 `Last-Modified`；validator 变化则丢弃重下。
- 分片：未完成 chunk 整块重下；响应必须 206 且 `Content-Range` 精确；任一 200 则退回单流。
- Windows：`MoveFileExW`/`FileRenameInfoEx`；占用时重试 5 次；仍失败 `FILE_LOCKED`，保留 `.part`。
- macOS：真正落盘需 `F_FULLFSYNC`（可选增强）。

### 9.7 去重与内容库

```
<data>/downloads/objects/<algo>/<hex[0:2]>/<hex>
```

- 只在 hash 校验通过后写入对象库。
- `mode:"link"`：优先硬链接；跨卷/不支持/权限失败 → 复制。
- 只对不可变 artifact（jar/assets/libraries/native jar）去重；配置/日志/可编辑 Mod 一律复制。
- 符号链接默认禁用（Windows 权限、Java/反作弊兼容性）。
- GC：`nlink<=1` 且 30 天未用；GC 与提交用读写锁；删除失败跳过。
- SHA-1 碰撞：对象键包含算法；用户 Mod 优先 SHA-256，避免“碰撞复用可信对象”。

### 9.8 事件与结果

- 进度固定结构体 + 150ms 聚合；只发 `current_file` + 聚合值，完整列表用 `download.snapshot` 分页。
- 结果：

```json
{
  "planId": "8f2c...",
  "status": "completed",
  "ok": 1180,
  "skipped": 12,
  "failed": [
    { "dest": "mods/a.jar", "code": "HASH_MISMATCH", "source": "...", "attempts": 4,
      "retryable": false, "message": "sha1 expected ..., got ..." }
  ],
  "bytes": 123456789,
  "elapsedMs": 43210,
  "peakBytesPerSec": 52428800,
  "retries": 17,
  "post": []
}
```

- `status ∈ completed | partial | failed | cancelled | paused`。
- 错误码：1xxx 计划、2xxx 网络、3xxx 完整性、4xxx 磁盘、5xxx 取消、6xxx post、9xxx 内部。
- C++ 映射：1xxx→`E_INVALID`、2xxx→`E_NETWORK`、3xxx/4xxx→`E_DISK`、5xxx→`E_CANCELLED`、6xxx→`E_INVALID`/`E_DISK`、其余→`E_INTERNAL`。

### 9.9 关闭与恢复

- `shutdown`：取消 token → 等待 JoinSet 2s → 超时 abort；abort 前必须写完 `.part.meta`。
- 不调用 `std::process::exit`；让 C++ 统一收尾。
- 退出后保留 `.part` 7 天；`purge=true` 才删。
- 恢复时 `(dest, hash, size)` 匹配即可复用，不要求同一 planId。

### 9.10 内存与性能

- 峰值 RSS 目标 < 256 MiB（不含计划解析）；计划解析是主要占用，200k 文件约 40–80 MiB。
- `streams × (chunk_buffer + inflight)` ≈ 16 × 128 KiB ≈ 2 MiB。
- 不在 chunk 内做 JSON/`Instant::now()`/`tracing::info!`。
- 写盘若 blocking 任务过多，改“每文件一个 writer 线程 + 有界 mpsc”。
- 压测项：本地假 HTTP 100/1000 文件、断点、校验失败、429、磁盘满、跨卷、Windows 占用。

---

## 10. Flutter UI（archoera_cx）

### 10.1 分层

```
lib/
├─ app/             # bootstrap / app / router / shell
├─ pages/           # 页面；只读 state、发 intent
├─ services/        # CoreClient、事件泵、平台服务；无 UI
├─ stores/          # Riverpod providers / prefs
├─ theme/           # tokens / app_theme
├─ widgets/         # common / layout
├─ utils/           # 无状态工具
└─ l10n/            # ARB + generated/
```

- Widget 不直接 `DynamicLibrary`/`File`/`Process`/`HttpClient`。
- Controller 不持有 Widget；不 `setState` 跨页面。
- Model 由契约生成或手写 `freezed`；与 JSON schema 对齐。

### 10.2 CoreClient

```dart
final class CoreClient {
  final DynamicLibrary _lib;
  late final _init = _lib.lookupFunction<...>('archoera_init');
  late final _call = _lib.lookupFunction<...>('archoera_call');
  late final _poll = _lib.lookupFunction<...>('archoera_poll');
  late final _result = _lib.lookupFunction<...>('archoera_result');
  late final _release = _lib.lookupFunction<...>('archoera_release');
  late final _attach = _lib.lookupFunction<...>('archoera_attach');
  late final _setWakeup = _lib.lookupFunction<...>('archoera_set_wakeup');

  Future<TaskHandle> call(String method, Map<String, Object?> params);
  Stream<CoreEvent> events; // 由 EventPump 提供
}
```

- 加载路径：`Platform.resolvedExecutable` 同目录 + `native/`；**不要**依赖 `PATH`。
- Windows 用应用目录绝对路径，防 DLL 劫持；macOS 正确 `@rpath`。
- 所有 `NativeCallable` 保存引用；退出 `close()`；热重启重建。
- `call` 只做投递；结果由 `TaskHandle` 的 completer/状态流返回。

### 10.3 事件泵

```dart
void _onWake() => scheduleMicrotask(_drain);

void _drain() {
  if (_draining) return;
  _draining = true;
  try {
    final batch = _pollBatch(64);
    for (final e in batch) _reduce(e);
    _flushUiOncePerFrame();
  } finally {
    _draining = false;
  }
}
```

- 进度按 taskId 合并，只保留最新；每帧最多一次通知。
- 状态事件立即更新；`Completed` 后再拉 `result`。
- 事件字符串立即复制，不能持有原生指针。
- UI 退到后台/窗口最小化时降低刷新频率，但任务继续。

### 10.4 状态管理

- **Riverpod**（`Notifier`/`AsyncNotifier`）；按页面/领域分 provider。
- 全局：`coreClientProvider`、`eventPumpProvider`、`settingsProvider`、`complianceProvider`。
- 页面：`instancesProvider`、`instanceDetailProvider(id)`、`downloadsProvider`、`accountsProvider`。
- 规则：
  - provider 不直接读文件/网络；
  - 控制器只调 `CoreClient`；
  - 不在 `build` 里发请求；
  - 取消：页面销毁时 cancel 对应 task（或标记为后台任务）。

### 10.5 页面地图

| 区域 | 页面 | 关键能力 |
|---|---|---|
| 首页 | 实例列表/最近游玩/快速启动 | 列表、搜索、分组、创建 |
| 实例 | 详情/版本/Mod/资源包/光影/日志/设置 | 启停、加载器安装、Mod 启停/删除、打开目录、修复 |
| 下载 | 版本列表/全局 Mod/整合包/资源搜索 | 过滤、分页、安装队列 |
| 账户 | 账户列表/登录/皮肤 | device code、离线、第三方 |
| 下载中心 | 队列/速度/限速/镜像 | 暂停/继续/取消/重试 |
| 设置 | 通用/Java/网络/外观/合规/关于 | 配置、诊断、导出 |
| 诊断 | 崩溃报告/日志/模块信息 | 复制/导出/上传（opt-in） |

- 单窗口 + 内嵌面板；日志窗口是页面/抽屉，不新建窗口。
- 路由：`go_router`；深链只接受白名单动作。

### 10.6 主题与设计令牌

- 统一 token：颜色（surface/onSurface/accent/danger）、字号阶梯、间距、圆角、动效曲线/时长。
- 明暗主题 + 强调色；背景：纯色/图片/Mica/Acrylic + 模糊缓存。
- 不混搭 `fluent_ui`/`macos_ui`；自研组件层。
- 150% 缩放、中文长文本、RTL 预留；对比度 4.5:1。
- 减少动效设置；骨架屏/空态/错误态统一组件。

### 10.7 图片

- 这是唯一允许 Dart 直接出网的场景。
- 优先本地缓存（C# 后台预热），失败再 `Image.network`；必须有占位图/重试。
- 代理需要认证时图片可能失败；UI 不能白屏。
- 图片解码在 isolate/引擎内；大图限制尺寸，避免内存暴涨。

### 10.8 3D 皮肤

- 默认 **C# 预渲染**：SkiaSharp/ImageSharp 渲染 2D 展开图 + 变换，或离屏转盘帧序列；UI 只播放。
- 回退：2D 正/背/侧视图；再回退静态头像。
- 不在 Flutter 内做实时 3D；不依赖长期缺维护的 `three_dart`/`flutter_gl`。
- 可选 WebView + `skinview3d`（MIT）作为高级模式，需评估内存/离线。

### 10.9 插件白名单

| 插件 | 用途 | 备注 |
|---|---|---|
| `window_manager` | 窗口尺寸/位置/无边框 | 平台差异集中 |
| `file_selector` | 文件/目录选择 | 官方 |
| `desktop_drop` | 拖拽导入 | `.mrpack`/zip |
| `url_launcher` | 外部链接兜底 | 业务统一走 C++ `system.openUrl`（https 白名单）；插件仅兜底 |
| `webview_*`（按需） | Mod 描述/OAuth 回调 | 仅展示/回调；域名白名单、独立 Cookie；不承载业务 API |
| `shared_preferences`（可选） | 轻量 UI 偏好 | 业务配置走核心 |

- 托盘/单实例/文件关联/更新不放 Dart 插件，放 C++。
- 插件升级与 Flutter 大版本绑定；升级前跑 PoC 清单（`01` §6）。

### 10.10 i18n 与无障碍

- ARB + `intl`；`zh-Hans`/`en` 起步；不拼句子。
- UI 文案统一使用 `AppLocalizations.of(context)`；模型不持有文案，枚举标签由 `lib/l10n/l10n_labels.dart` 映射。
- 语言偏好保存在 `shared_preferences` 的 `ui.locale`（`en` / `zh`；缺省跟随系统），设置页外观可切换。
- 日期/数字/文件大小用 locale；审计用 UTC ISO-8601。
- 键盘导航、焦点陷阱、读屏标签；锁图标不能是唯一信息载体。
- 伪语言 `en_XA` 检查截断；德语长词/阿语 RTL 单独验收。

### 10.11 错误/加载/空态

- 每个异步页面三态：loading（骨架）、empty（引导）、error（原因 + 重试 + 日志/诊断）。
- 搜索失败给“重试 + 查看离线缓存”，不只 toast。
- 下载失败按文件展示错误码/源/重试；支持“只重试失败项”。
- 合规拒绝给说明面板，不把按钮做成不可点的死路。

### 10.12 性能规则

- 大列表 `ListView.builder`/`SliverList`；不一次性建 1000 个 Widget。
- 日志用环形缓冲 + 虚拟滚动；不把全部日志放一个 `Text`。
- 进度事件合并；每帧最多一次 `notifyListeners`。
- 图片 `cacheWidth/cacheHeight`；避免超大图。
- 页面销毁取消订阅；后台任务保留在核心。

---

## 11. 数据与存储

### 11.1 根目录

| 用途 | Windows | Linux | macOS |
|---|---|---|---|
| 配置 | `%APPDATA%\ArchoeraCX` | `~/.config/archoeracx` | `~/Library/Application Support/ArchoeraCX` |
| 缓存/日志/崩溃 | `%LOCALAPPDATA%\ArchoeraCX` | `~/.cache/archoeracx` | `~/Library/Caches/ArchoeraCX` |
| 游戏数据 | 用户可选，默认 `%APPDATA%\.archoera` | `~/.local/share/archoera` | `~/Library/Application Support/archoera` |

```
<config>/
├─ config.json
├─ compliance.json              # 用户手写
├─ state/consent.json           # 应用写
└─ audit/compliance-YYYYMM.jsonl

<data>/
├─ archoera.db
├─ instances/
│  ├─ <id>/
│  │  ├─ instance.json
│  │  ├─ icon.png
│  │  ├─ .minecraft/
│  │  └─ .archoera/{managed.json,tx,conflicts,removed}
│  └─ .staging/<txid>/
├─ shared/{libraries,assets,versions}/
├─ downloads/objects/<algo>/<xx>/<hash>
├─ runtime/java-<major>/
├─ meta/{manifest,versions,loaders,resources}/
├─ backups/
└─ trash/

<cache>/
├─ logs/
├─ crashes/
└─ images/
```

- 默认不写用户已有的 `.minecraft`；提供“导入/接管”两种模式。
- 便携模式：所有数据在可执行文件旁 `data/`；`--portable` 或标记文件；需可写。
- 路径比较：Windows/macOS 大小写不敏感；Linux 也建议按大小写不敏感拒绝冲突，保证可移植。

### 11.2 实例目录与 `instance.json`

```json
{
  "schemaVersion": 1,
  "id": "01J8Z9K3Q7",
  "name": "1.21.4 Fabric",
  "createdAt": "2026-10-20T12:00:00Z",
  "updatedAt": "2026-10-20T12:00:00Z",
  "gameVersion": "1.21.4",
  "loader": { "kind": "fabric", "version": "0.16.10" },
  "components": [],
  "java": { "mode": "auto", "major": 21, "path": null },
  "memory": { "minMb": 512, "maxMb": 4096 },
  "window": { "width": 854, "height": 480, "fullscreen": false },
  "jvmArgs": [],
  "gameArgs": [],
  "env": {},
  "icon": "icon.png",
  "notes": "",
  "playTimeSec": 0,
  "lastPlayedAt": null
}
```

- **权威源**：用户可见配置以 `instance.json` 为准；SQLite 是索引/运行态缓存。
- 写入：`.tmp` → fsync → rename；保留 `.bak`（最近 3 份）。
- 不存绝对共享路径；由数据根派生。
- 启动对账：
  - 目录有、DB 无 → 收养；
  - DB 有、目录无 → 标 `missing`，不自动删；
  - 两边字段冲突 → 以 `instance.json` 为准，DB 更新；`playTimeSec` 取较大值。
- 实例 ID 用 ULID/时间有序 ID；不可变。

### 11.3 共享存储与去重

| 内容 | 位置 | 说明 |
|---|---|---|
| libraries | `shared/libraries/` | 所有实例共享；Forge processor 产物按版本落位 |
| assets | `shared/assets/` | `objects/xx/hash` + `indexes/`；legacy/virtual 支持 |
| versions | `shared/versions/<id>/` | 版本 JSON/jar；加载器 profile 合并结果不放这里 |
| 内容库 | `downloads/objects/<algo>/<xx>/<hash>` | hash 校验后写入；硬链接/复制到实例 |
| 实例 gameDir | `instances/<id>/.minecraft/` | Mod/配置/存档；不共享 |

- 默认不对整个 `.minecraft` 做 symlink；用启动参数指向共享 assets/libraries，避免 Windows 权限与反作弊问题。
- 可选“磁盘高效模式”：对不可变 jar 使用硬链接；失败降级复制。
- 删除实例只删实例目录与 DB 行；共享库由 GC 按引用/时间清理。

### 11.4 SQLite schema（v1）

```sql
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
PRAGMA synchronous = NORMAL;

CREATE TABLE schema_migrations (
  version    INTEGER PRIMARY KEY,
  applied_at TEXT NOT NULL
) STRICT;

CREATE TABLE instances (
  id              TEXT PRIMARY KEY,
  name            TEXT NOT NULL,
  game_version    TEXT NOT NULL,
  loader_kind     TEXT NOT NULL DEFAULT 'vanilla',
  loader_version  TEXT,
  game_dir        TEXT NOT NULL,              -- 相对 <data>
  icon_path       TEXT,
  group_name      TEXT,
  pinned          INTEGER NOT NULL DEFAULT 0,
  status          TEXT NOT NULL DEFAULT 'ready', -- ready|installing|broken|missing
  play_time_sec   INTEGER NOT NULL DEFAULT 0,
  last_played_at  TEXT,
  created_at      TEXT NOT NULL,
  updated_at      TEXT NOT NULL
) STRICT;

CREATE TABLE instance_components (
  instance_id  TEXT NOT NULL,
  kind         TEXT NOT NULL,                 -- loader|optifine|...
  component_id TEXT NOT NULL,
  version      TEXT NOT NULL,
  installed_at TEXT NOT NULL,
  PRIMARY KEY (instance_id, kind, component_id),
  FOREIGN KEY (instance_id) REFERENCES instances(id) ON DELETE CASCADE
) STRICT;

CREATE TABLE accounts (
  id               TEXT PRIMARY KEY,
  kind             TEXT NOT NULL,             -- msa|offline|authlib
  display_name     TEXT NOT NULL,
  uuid             TEXT,
  authlib_url      TEXT,
  skin_url         TEXT,
  is_default       INTEGER NOT NULL DEFAULT 0,
  token_ref        TEXT,                      -- 指向 OS 凭据库，不存 token
  token_expires_at TEXT,
  created_at       TEXT NOT NULL,
  last_used_at     TEXT
) STRICT;

CREATE TABLE settings (
  key        TEXT PRIMARY KEY,
  value_json TEXT NOT NULL,
  updated_at TEXT NOT NULL
) STRICT;

CREATE TABLE java_runtimes (
  id           TEXT PRIMARY KEY,
  path         TEXT NOT NULL,
  major        INTEGER NOT NULL,
  vendor       TEXT,
  arch         TEXT,
  source       TEXT NOT NULL,                 -- mojang|adoptium|manual
  sha256       TEXT,
  installed_at TEXT NOT NULL
) STRICT;

CREATE TABLE version_meta (
  id           TEXT PRIMARY KEY,
  type         TEXT,
  url          TEXT,
  sha1         TEXT,
  release_time TEXT,
  fetched_at   TEXT NOT NULL,
  raw_json     BLOB NOT NULL
) STRICT;

CREATE TABLE effective_versions (
  cache_key    TEXT PRIMARY KEY,
  instance_id  TEXT NOT NULL,
  merge_schema INTEGER NOT NULL,
  inputs_json  TEXT NOT NULL,
  effective_json BLOB NOT NULL,
  created_at   TEXT NOT NULL,
  FOREIGN KEY (instance_id) REFERENCES instances(id) ON DELETE CASCADE
) STRICT;

CREATE TABLE file_objects (
  algo         TEXT NOT NULL,                 -- sha1|sha256|sha512
  hex          TEXT NOT NULL,
  size         INTEGER NOT NULL,
  rel_path     TEXT NOT NULL,                 -- 相对 <data>/downloads/objects
  last_used_at TEXT NOT NULL,
  PRIMARY KEY (algo, hex)
) STRICT;

CREATE TABLE resource_projects (
  provider    TEXT NOT NULL,
  project_id  TEXT NOT NULL,
  slug        TEXT,
  kind        TEXT NOT NULL,
  name        TEXT NOT NULL,
  summary     TEXT,
  icon_url    TEXT,
  author      TEXT,
  downloads   INTEGER NOT NULL DEFAULT 0,
  follows     INTEGER,
  updated_at  TEXT,
  raw_json    BLOB NOT NULL,
  fetched_at  TEXT NOT NULL,
  PRIMARY KEY (provider, project_id)
) STRICT;

CREATE TABLE resource_versions (
  provider          TEXT NOT NULL,
  version_id        TEXT NOT NULL,
  project_id        TEXT NOT NULL,
  name              TEXT,
  version_number    TEXT,
  channel           TEXT,
  game_versions_json TEXT NOT NULL,
  loaders_json      TEXT NOT NULL,
  published_at      TEXT,
  files_json        TEXT NOT NULL,
  dependencies_json TEXT,
  raw_json          BLOB NOT NULL,
  fetched_at        TEXT NOT NULL,
  PRIMARY KEY (provider, version_id)
) STRICT;

CREATE TABLE resource_cache (
  cache_key     TEXT PRIMARY KEY,
  provider      TEXT NOT NULL,
  etag          TEXT,
  last_modified TEXT,
  fetched_at    TEXT NOT NULL,
  expires_at    TEXT NOT NULL,
  stale         INTEGER NOT NULL DEFAULT 0,
  payload       BLOB NOT NULL
) STRICT;

CREATE TABLE local_mods (
  instance_id  TEXT NOT NULL,
  rel_path     TEXT NOT NULL,
  sha1         TEXT,
  size         INTEGER NOT NULL,
  mtime        INTEGER NOT NULL,
  mod_id       TEXT,
  version      TEXT,
  loader       TEXT,
  enabled      INTEGER NOT NULL DEFAULT 1,
  metadata_json TEXT,
  PRIMARY KEY (instance_id, rel_path),
  FOREIGN KEY (instance_id) REFERENCES instances(id) ON DELETE CASCADE
) STRICT;

CREATE TABLE download_jobs (
  id          TEXT PRIMARY KEY,               -- taskId
  plan_id     TEXT NOT NULL,
  instance_id TEXT,
  state       TEXT NOT NULL,
  total_bytes INTEGER NOT NULL DEFAULT 0,
  done_bytes  INTEGER NOT NULL DEFAULT 0,
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  result_json TEXT
) STRICT;

CREATE TABLE crash_reports (
  id           TEXT PRIMARY KEY,
  instance_id  TEXT,
  occurred_at  TEXT NOT NULL,
  kind         TEXT NOT NULL,                 -- exit|crash_report|hs_err|launcher
  summary      TEXT,
  report_path  TEXT,
  analysis_json TEXT,
  uploaded     INTEGER NOT NULL DEFAULT 0
) STRICT;

CREATE INDEX idx_instances_last_played ON instances(last_played_at DESC);
CREATE INDEX idx_instance_components_instance ON instance_components(instance_id);
CREATE INDEX idx_effective_instance ON effective_versions(instance_id);
CREATE INDEX idx_local_mods_instance ON local_mods(instance_id);
CREATE INDEX idx_file_objects_last_used ON file_objects(last_used_at);
CREATE INDEX idx_resource_cache_expires ON resource_cache(expires_at);
CREATE INDEX idx_crash_instance_time ON crash_reports(instance_id, occurred_at DESC);
```

- 所有时间用 UTC ISO-8601 文本；布尔用 0/1。
- 迁移只前进；每个迁移在事务中执行；失败回滚并保留备份。
- `STRICT` 需要 SQLite 3.37+；目标包 `SQLitePCLRaw` 版本需实测。
- FTS5 可选：中文用 `tokenize='trigram'`，但 2 字查询需回退 LIKE/内存索引；不要假设编译选项，启动检查 `PRAGMA compile_options`。

### 11.5 数据库所有权与并发

- C# 是唯一写入方；Rust/Flutter 不打开数据库。
- WAL + `busy_timeout`；写队列串行化；读连接可并发。
- 长事务禁止；安装事务的 journal 在文件系统，不在 DB 里跨步骤持有。
- 备份：迁移前、重大操作前；保留最近 N 份；导出诊断时可附带 DB 副本（脱敏）。

### 11.6 缓存

| 缓存 | 位置 | TTL/策略 |
|---|---|---|
| 版本清单/JSON | `meta/` + `version_meta` | 启动异步刷新；sha1/ETag |
| 有效版本 | `meta/effective/` + `effective_versions` | 输入 hash 变化即失效 |
| 搜索/详情 | `resource_cache` | 60s/10min/5min/24h；LRU 20MB |
| 图片 | Flutter 图片缓存 + 本地预热 | 失败占位；不阻塞列表 |
| 本地 Mod hash | `local_mods` | `(size,mtime)` 变化才重算 |
| Java 探测 | 内存 + `java_runtimes` | 路径/mtime 变化失效 |

### 11.7 日志、崩溃与诊断

```
logs/
├─ launcher-YYYYMMDD.log       # JSON Lines；轮转
├─ module-cs-YYYYMMDD.log
├─ module-dl-YYYYMMDD.log
└─ game/<instance>/<timestamp>.log
crashes/
├─ <timestamp>.dmp
└─ <timestamp>.json            # 模块版本、任务快照、最近事件
```

- 日志字段：`ts, level, target, taskId, module, message, fields`；统一脱敏。
- 环形缓冲 + 文件轮转；默认保留 7 天/每文件 16 MiB。
- 诊断包：`diagnostics-<timestamp>.zip`，含脱敏日志、模块信息、任务快照、崩溃报告；不含 token/账户文件。
- 上传默认关闭；opt-in 前展示内容清单。

### 11.8 回收站与 GC

- 删除实例：移到 `trash/<timestamp>/`，保留 7 天；可恢复。
- 整合包删除的旧文件：`.archoera/removed/<txid>/`，保留 30 天。
- 内容库 GC：30 天未用且 `nlink<=1`；与下载提交用读写锁。
- 备份保留：最近 3 份 `instance.json`、最近 5 份 DB 迁移备份。

---

## 12. 关键流程

### 12.1 冷启动

```
Flutter main
  └─ CoreClient.load() → archoera_init(config)
      ├─ 日志/崩溃/配置
      ├─ C# init：SQLite 迁移、设置、合规、HTTP
      ├─ Rust init：tokio、代理、内容库
      └─ 返回 core
  └─ set_wakeup + attach(0)
  └─ system.snapshot / instance.list（并行）
  └─ 主界面可交互
  └─ 后台：清单刷新、崩溃报告检查、更新检查（门控）
```

**失败**：C# 失败 → 诊断页；Rust 失败 → 降级；DB 损坏 → 修复/重建索引。

### 12.2 安装原版

```
UI → core: version.install { version:"1.21.4", instance:{name,...} }
core → C#: call
C#: manifest → version JSON → merge → DownloadPlan + continuation
C# → core: effect(download)
core → Rust: download.submit(plan)
Rust → core: progress/state
Rust → core: completed(result)
core → C#: task.continue { continuation, downloadResult }
C#: verify → post(natives) → staging commit → instance.json → SQLite
C# → core: completed
core → UI: state completed + result
```

**失败点**：清单/JSON 网络失败（重试/换源）；hash 不匹配（换源）；磁盘满（预检）；processors 失败（staging 回滚）；注册失败（收养扫描）。
**根目录选择**：实例私有文件以 `instances/.staging/<txid>` 为 plan root；共享库以 `<data>` 为 root 或 `mode:"link"` 到内容库；跨根拆成多个计划。

### 12.3 启动游戏

```
UI → launch.start { instanceId }
C#: 读取实例 → 有效版本 → Java → LaunchPlan → 预检
C#: spawn JVM（argv 数组）
C# → UI: { pid, startedAt }（立即）
JVM stdout/stderr → C# → core → 日志事件/UI
JVM exit → C#: 退出码 + crash-report/hs_err → 崩溃分析
C# → UI: launch.exited { code, diagnosis }
```

**失败点**：Java 缺失/版本错；文件缺失（修复）；token 失效（重新登录）；端口/内存；Mod 崩溃（分析）。

### 12.4 微软登录

```
UI → account.login.microsoft.start
C# → 微软 devicecode → { userCode, verificationUri, expiresIn }
UI: 展示代码/链接（可复制）
UI → account.login.microsoft.poll（按 interval）
C#: token → XBL → XSTS → MC login → profile
C#: refresh token 写 OS 凭据库；账户写 SQLite
C# → UI: Account
```

**失败**：`authorization_pending`/`slow_down`/`expired_token`/XSTS 错误 → 人话提示；取消 → 停止轮询。

### 12.5 搜索与安装 Mod

```
UI: 输入防抖 300ms → cancel 旧 task → resource.search
C#: 缓存/single-flight → provider → DTO
UI: 列表/分页/详情
UI → resource.install { instanceId, provider, projectId, versionId }
C#: 兼容性/依赖解析 → DownloadPlan
C# → core: effect(download)
Rust: 下载到内容库/staging
C#: 校验 → 落 mods/ → 更新 local_mods → 事件
```

**失败**：429/403（倒计时/配置 key）；依赖冲突（提示）；作者禁转（去官网）；文件被占用。

### 12.6 加载器安装

```
loader.install { instanceId, kind, version }
C#: Fabric/Quilt → meta profile → merge → 下载库
C#: Forge/NeoForge → installer → install_profile → 下载库/processor
C#: 有 Java → 跑 processors；无 Java → PendingProcessors
C#: 合并 version.json → 校验 outputs → 写实例组件
```

- Fabric/Quilt 不需要 Java；Forge 1.13+ / NeoForge 需要。
- OptiFine 默认手动导入 jar（站点条款限制），不作为一键安装路径。
- processors 输出写 staging；`outputs` 已存在且 sha1 匹配则跳过（幂等）。
- 失败保留 store、删 staging；首次启动前可重试 PendingProcessors。

### 12.7 整合包导入

```
UI: 拖入 .mrpack/zip → modpack.probe
C#: magic → 格式 → descriptor → 预览（名称/版本/加载器/文件数/大小）
UI: 选择安装模式/可选 Mod
C#: resolve → DownloadPlan + overrides
Rust: 下载
C#: 解压 overrides（安全校验）→ 安装加载器 → staging
C#: 事务提交 → 注册 → 事件
```

**失败**：未知格式（提示解压）；恶意 zip（拒绝并报告条目）；磁盘不足（预检）；加载器失败（staging 回滚）。

### 12.8 崩溃诊断

```
JVM exit
C#: 收集 exit code/stdout/stderr/crash-report/hs_err
C#: 阶段定位 → 规则匹配 → 评分 → 诊断
UI: 崩溃页（摘要/证据/建议/复制/导出/上传 opt-in）
C#: 写 crash_reports
```

- 退出码 0 也可能是崩溃；以证据为准。
- 规则误报要可关闭/反馈；诊断不自动上传。

### 12.9 启动器更新

```
UI/后台 → update.check（签名 manifest）
updater.exe：下载 → 验签 → 关闭主程序 → 替换 → 失败回滚 → 重启
主程序：启动时检查上次更新结果
```

- 更新器独立进程；不占用自身文件。
- 签名：Ed25519 manifest + Windows Authenticode/macOS notarization。
- 不更新正在运行的游戏；提示先退出。

### 12.10 合规门控

```
UI → compliance.state
C#: 读 compliance.json/state/consent → 机器策略 → 有效等级
下载请求 → C#: 生成计划前求值
  ├─ 拒绝 → DOWNLOAD_DISABLED/CONSENT_REQUIRED
  └─ 通过 → capability(HMAC) + DownloadPlan
core → Rust: submit(plan, capability)
Rust: 验签 → 下载
撤回 → gateEpoch++ → 取消在途任务
```

- L0 = 不下载内容，但允许只读元数据；L0-Z = 零出站严格构建（回环除外）。
- 开发者模式：双文件 + 三步同意 + 审计；应用永不写 `developerMode: true`。
- capability 只防 Dart/脚本绕过 C#，不是抗逆向。

---

## 13. 网络与 API 设计

### 13.1 所有权

| 流量 | Owner | 说明 |
|---|---|---|
| 业务 API（清单/搜索/详情/认证/更新检查） | C# | 唯一 API owner；统一代理/证书/限流/缓存/错误 |
| 文件下载（游戏/库/资源/Mod/整合包/Java） | Rust | 只执行 `DownloadPlan`；不解析业务 |
| 图片 | Flutter | 唯一例外；本地缓存优先，失败占位 |
| 启动器自更新 | 独立 updater | 签名 manifest；不经过业务 API |
| 游戏自身联网 | JVM | 启动器不代理、不注入（authlib 除外） |

### 13.2 两套 HTTP 栈

| 项 | C# `HttpClient` | Rust `reqwest` |
|---|---|---|
| 用途 | 小请求、JSON、认证 | 大文件、分片、并发 |
| 代理 | 系统代理/手动；解析后下发 | 使用 C# 下发的代理配置 |
| 证书 | 系统信任库 | 系统根（native-tls/rustls native roots） |
| HTTP/2 | 默认 | ALPN 协商 |
| 超时 | 连接 10s、总 30s（按方法） | 连接 10s、读空闲 30s |
| 重试 | 幂等请求指数退避 | 按文件/源/错误分类 |
| UA | 统一格式 | 同一格式 |

**规则**

- 代理只解析一次（C#），通过 `init`/`download.submit` 传给 Rust；Rust 不再读系统代理。
- 代理变更通过 `download.configure` 热更新或重建 client；不允许 Rust 自行读系统代理。
- C# 与 Rust 的 UA、`Retry-After`、429 语义保持一致；日志都带 provider/host/taskId。
- 不允许在 C# 里下载大文件；不允许在 Rust 里解析 Mojang/Modrinth JSON。
- 图片绕过两者是已知例外；失败时 UI 不能白屏。

### 13.3 超时、重试、限流

| 场景 | 超时 | 重试 | 限流 |
|---|---|---|---|
| 清单/详情 | 连接 10s，总 30s | 幂等 3 次，指数退避 | 每 provider token bucket |
| 搜索 | 总 20s | 3 次；429 尊重 `Retry-After` | 搜索/详情/标签分桶 |
| 登录轮询 | 单次 15s | 按协议 `interval` | 不并发轮询 |
| 文件下载 | 见 §9.5 | 同源 3–4 次，换源 | 全局/每 host/每文件三层 |
| 更新检查 | 30s | 2 次 | 独立桶，避免挤占搜索 |

### 13.4 Provider 抽象

```csharp
public interface IResourceProvider {
    string Id { get; }                       // modrinth|curseforge
    Task<ResourceSearchResult> SearchAsync(ResourceSearchRequest r, CancellationToken ct);
    Task<ResourceProject> GetProjectAsync(ResourceRef r, CancellationToken ct);
    Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(ResourceRef r, InstanceTarget? t, int page, int size, CancellationToken ct);
    Task<UpdateCheckResult> CheckUpdatesAsync(UpdateCheckRequest r, CancellationToken ct);
}

public interface IAuthProvider {
    Task<DeviceCode> StartDeviceCodeAsync(CancellationToken ct);
    Task<Account> PollAsync(string deviceCode, CancellationToken ct);
    Task<Account> RefreshAsync(Account account, CancellationToken ct);
}

public interface IDownloadProvider {
    // 只生成 URL/镜像/条款标记，不执行下载
    DownloadSource Resolve(ResourceFile file);
}
```

- Provider 注册表由发行通道决定；`official-cn` 在 DI 阶段不注册 L2 provider。
- CurseForge key：用户自备或独立网关；不内置；不进日志。
- Modrinth/CF 的字段差异在 Provider 内映射；UI 只消费统一 DTO。
- 源矩阵、依赖解析、失败码和“官方文件 vs 内容平台 vs 本地生成物”的边界见 [08/10-download-sources.md](08-technical-research/10-download-sources.md)。

### 13.5 缓存、single-flight、离线

| 机制 | 规则 |
|---|---|
| TTL | 搜索 60s、详情 10min、版本 5min、标签 24h |
| ETag | 有则 `If-None-Match`；304 只更新 `fetched_at` |
| single-flight | key = provider + 规范化查询 + facets + sort + page + target；并发合并 |
| 取消 | 所有订阅者取消才取消底层；单个 UI 取消不影响其他页面 |
| 离线 | 最近 50 个搜索 key×首页 + 500 个详情；LRU 20MB；标注 `stale/cachedAt` |
| 失败 | 内联错误 + 重试 + 查看离线缓存；不只 toast |

### 13.6 方法目录（v1）

> 所有方法名稳定；参数/结果 schema 在 `contracts/methods/`。`owner` 指实际实现模块；C++ 只路由/编排。

| 方法 | Owner | 说明 |
|---|---|---|
| `system.ping` | C++ | 健康检查 |
| `system.info` | C++ | 应用/ABI/模块版本 |
| `system.modules` | C++ | 已加载模块名称/版本/状态 |
| `system.snapshot` | C++ | 活跃任务 + 事件 seq |
| `system.log.tail` | C++ | 环形缓冲分页 |
| `system.log.export` | C++ | 导出诊断日志 |
| `system.memory` | C# | 上报物理总内存/可用内存 |
| `system.diagnostics` | C++/C# | 生成诊断包 |
| `system.openPath` | C++ | 打开本地路径（校验） |
| `system.openUrl` | C++ | 打开 https 白名单 |
| `settings.get` | C# | 应用设置 |
| `settings.set` | C# | 校验 + 原子写 |
| `settings.reset` | C# | 恢复默认 |
| `instance.list` | C# | 实例列表/分组/搜索 |
| `instance.get` | C# | 详情 |
| `instance.create` | C# | 创建空实例/从版本创建 |
| `instance.update` | C# | 名称/内存/Java/参数 |
| `instance.delete` | C# | 移入 trash |
| `instance.sync` | C# | 扫描配置的实例根，采纳/刷新/标记缺失（不删文件） |
| `instance.mods.list` | C# | 列出实例 `mods/` 中的 jar 与 `.disabled` 文件 |
| `instance.mods.setEnabled` | C# | 通过 `.disabled` 后缀启停 Mod |
| `instance.mods.remove` | C# | 删除实例中的单个 Mod 文件 |
| `instance.duplicate` | C# | 复制（可选链接） |
| `instance.import` | C# | 导入现有目录/压缩包 |
| `instance.export` | C# | 导出整合包/目录 |
| `instance.repair` | C# | 校验/重建有效版本/补文件 |
| `instance.openFolder` | C#/C++ | 打开实例目录 |
| `java.scan` | C# | 扫描本机 Java |
| `java.list` | C# | 已知/托管 runtime |
| `java.validate` | C# | 校验路径/版本/架构 |
| `java.install` | C# | 下载/安装 runtime |
| `java.remove` | C# | 删除托管 runtime |
| `version.manifest` | C# | 版本清单（缓存） |
| `version.refresh` | C# | 强制刷新 |
| `version.list` | C# | Mojang 版本清单；正式版/快照过滤（已接） |
| `version.plan` | C# | 原版 client/libraries/assets 计划；`versions/` 下实例自动使用共享根 |
| `version.commit` | C# | 写回原版版本 JSON |
| `version.get` | C# | 单个版本 JSON |
| `version.effective` | C# | 合并后的有效版本 |
| `version.install` | C# | 安装原版/创建实例 |
| `version.repair` | C# | 修复文件/重建 |
| `loader.catalog` | C# | 加载器版本/兼容矩阵 |
| `loader.install` | C# | 安装/切换加载器 |
| `loader.verify` | C# | `quick`/`full` 两种深度校验生成物与空 URL 库（不写文件） |
| `loader.versions` | C# | 按游戏版本列出 Fabric/Quilt/Forge/NeoForge 加载器版本 |
| `loader.check` | C# | 从实例安装目录定位 installer 后执行 quick/full 校验 |
| `loader.plan` | C# | 从本地 installer 生成原版 + 加载器依赖 + installer 组合计划 |
| `loader.commit` | C# | 原子写入合并后的 `versions/<id>/<id>.json` |
| `loader.repair` | C# | 从最早缺失 processor 重跑；写回校验结果（processors 需要 JVM） |
| `account.list` | C# | 账户列表 |
| `account.current` | C# | 当前账户（无则 null） |
| `account.select` | C# | 设为默认 |
| `account.login.microsoft.start` | C# | device code |
| `account.login.microsoft.poll` | C# | 轮询完成 |
| `account.login.microsoft.cancel` | C# | 取消轮询 |
| `account.login.offline` | C# | 离线账户 |
| `account.login.authlib` | C# | 第三方认证 |
| `account.refresh` | C# | 刷新 token |
| `account.logout` | C# | 清 token/保留元数据可选 |
| `account.remove` | C# | 删除账户 |
| `launch.prepare` | C# | 构建/预检 LaunchPlan（不启动） |
| `launch.start` | C# | spawn JVM |
| `launch.stop` | C# | 优雅停止/杀进程树 |
| `launch.status` | C# | 运行状态/pid |
| `launch.log.tail` | C# | 游戏日志分页 |
| `resource.search` | C# | 统一搜索（当前 Modrinth；支持排序/分页） |
| `resource.project` | C# | 项目详情与画廊（当前 Modrinth） |
| `resource.detail` | C# | 项目详情 |
| `resource.versions` | C# | 版本列表（当前 Modrinth） |
| `resource.plan` | C# | 生成并校验单文件 `DownloadPlan`（当前 Modrinth） |
| `resource.install` | C# | 安装 Mod/资源包/光影（`resource.plan` + `download.execute`） |
| `resource.checkUpdates` | C# | 批量更新检查 |
| `resource.resolveDependencies` | C# | 依赖解析 |
| `mod.list` | C# | 本地 Mod |
| `mod.scan` | C# | 重新扫描 |
| `mod.enable` / `mod.disable` | C# | `.disabled` |
| `mod.delete` | C# | 移入 trash |
| `mod.update` | C# | 更新单个 |
| `modpack.probe` | C# | 格式探测/预览 |
| `modpack.plan` | C# | 解析/计划 |
| `modpack.import` | C# | 安装/合并 |
| `modpack.export` | C# | 导出 |
| `download.configure` | Rust | 更新代理/限速/并发（不重下） |
| `download.submit` | Rust | 提交计划（正式路径待接） |
| `download.execute` | C#/Rust | 临时代理：C# 写计划并调用 `archoera-dl`，Rust 执行 |
| `download.pause` | Rust | 暂停 |
| `download.resume` | Rust | 继续 |
| `download.cancel` | Rust | 取消 |
| `download.snapshot` | Rust | 分页快照 |
| `compliance.state` | C# | 当前等级/原因 |
| `compliance.evaluate` | C# | 求值（不改变状态） |
| `compliance.accept` | C# | 三步同意 |
| `compliance.revoke` | C# | 撤回 |
| `compliance.openConfigDir` | C# | 打开配置目录 |
| `cache.stats` | C# | 缓存占用 |
| `cache.clear` | C# | 按类型清理 |
| `update.check` | C# | 检查启动器更新（签名 manifest） |
| `update.download` | updater | 独立进程下载/验签，不复用主进程 HTTP |
| `update.apply` | updater | 退出后替换/回滚 |
| `crash.list` | C# | 崩溃报告列表 |
| `crash.analyze` | C# | 重新分析 |
| `crash.exportDiagnostics` | C# | 诊断包 |
| `platform.singleInstance` | C++ | 单实例状态/唤醒 |
| `platform.tray` | C++ | 托盘菜单 |
| `platform.window` | C++ | 窗口状态 |
| `platform.revealPath` | C++ | 文件管理器定位 |

### 13.7 Envelope 与错误

**请求**

```json
{ "schemaVersion": 1, "requestId": "01J...", "params": { } }
```

> `archoera_call` 的 `params_json` 就是上面的请求 envelope；`method` 单独传。C++ 只校验 envelope（版本/大小/JSON 结构），把 `params` 原样交给模块。

**成功**

```json
{ "ok": true, "data": { }, "meta": { "schemaVersion": 1, "requestId": "01J..." } }
```

**失败**

```json
{
  "ok": false,
  "error": {
    "code": "RATE_LIMITED",
    "message": "请求过于频繁，请稍后重试",
    "retryable": true,
    "retryAfterMs": 8000,
    "details": { "provider": "curseforge" }
  },
  "meta": { "schemaVersion": 1, "requestId": "01J..." }
}
```

- `code` 稳定、可本地化；`message` 可展示；`details` 供诊断，不含 token。
- `retryable` 只表示“同参数重试可能成功”；UI 决定是否自动重试。
- 错误码表见 `contracts/errors/codes.json`，与 §8.17/§9.8 对齐。

### 13.8 关键方法示例

**`version.install`**

```json
{
  "schemaVersion": 1,
  "params": {
    "version": "1.21.4",
    "instance": { "name": "1.21.4", "java": { "mode": "auto" } },
    "loader": null
  }
}
```

**`launch.start`**

```json
{
  "schemaVersion": 1,
  "params": {
    "instanceId": "01J8Z9K3Q7",
    "accountId": "01J8Z9K3Q8",
    "dryRun": false
  }
}
```

结果（立即）：

```json
{ "pid": 12345, "startedAt": "2026-10-20T12:34:56Z", "logTaskId": 42 }
```

**`download.submit`**

```json
{
  "schemaVersion": 1,
  "params": {
    "plan": { "schemaVersion": 1, "planId": "8f2c...", "files": [] },
    "capability": "base64-hmac",
    "pauseOnMetered": false
  }
}
```

**`resource.search`**

```json
{
  "schemaVersion": 1,
  "params": {
    "provider": "modrinth",
    "query": "sodium",
    "kind": "mod",
    "target": { "gameVersion": "1.21.1", "loader": "fabric", "side": "client" },
    "sort": "downloads",
    "page": 0,
    "pageSize": 20
  }
}
```

### 13.9 外部端点

| 域 | 端点 | 备注 |
|---|---|---|
| Mojang 清单 | `https://piston-meta.mojang.com/mc/game/version_manifest_v2.json` | 缓存 + sha1 |
| Mojang 资源 | `https://resources.download.minecraft.net/<xx>/<hash>` | Rust 下载 |
| Mojang 库 | `https://libraries.minecraft.net/` | 允许镜像回退 |
| Mojang Java | java-runtime manifest | 以 `javaVersion.component` 为准 |
| 微软 device code | `https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode` | 公共客户端 |
| Xbox Live | `https://user.auth.xboxlive.com/user/authenticate` | `RpsTicket=d=<token>` |
| XSTS | `https://xsts.auth.xboxlive.com/xsts/authorize` | 返回 UHS/XSTS |
| Minecraft Services | `https://api.minecraftservices.com/` | login/profile/entitlements |
| Fabric | `https://meta.fabricmc.net/v2/versions/loader/{game}/{loader}/profile/json` | 直接可合并 |
| Quilt | `https://meta.quiltmc.org/v3/versions/loader/{game}/{loader}/profile/json` | 注意 Maven 差异 |
| Forge | `https://maven.minecraftforge.net/` | installer/库 |
| NeoForge | `https://maven.neoforged.net/releases/` | 版本号规则不同 |
| Modrinth | `https://api.modrinth.com/v2/` | UA + 限流 |
| CurseForge | `https://api.curseforge.com/v1/` | `x-api-key`；用户 key/网关 |
| BMCLAPI | `https://bmclapi2.bangbang93.com/` | 白名单镜像；失败回退官方 |
| Adoptium | `https://api.adoptium.net/v3/` | 可选 Java 源 |

- 端点/字段可能变化；以 `06` 与官方文档为准；集成测试用固定快照。
- 镜像必须带官方 hash 校验；不自建游戏文件镜像。

### 13.10 Schema 与兼容

- 每个 method 一个 `contracts/methods/<domain>.<method>.schema.json`；请求/响应/事件分开。
- CI 校验：Dart 发参、C#/Rust 返回、事件 payload、旧 schema 兼容。
- 新增字段必须可选；删除/改语义 = 主版本升级。
- 事件 `type` 字符串稳定；未知事件 UI 忽略。

---

## 14. 安全设计

### 14.1 威胁模型

| 威胁 | 入口 | 控制 | 残余风险 |
|---|---|---|---|
| 路径逃逸（Zip Slip） | 整合包/资源包/加载器解压 | 规范化 + 前缀校验 + 拒绝绝对路径/`..` | 解析器 0-day |
| 符号链接/junction 逃逸 | 解压/下载落盘 | 拒绝 reparse point；逐级检查；`CreateNew` | TOCTOU（本地攻击者） |
| Zip bomb | 整合包 | 条目数/总量/压缩比/单条目预算 | 新型压缩算法 |
| 大小写/NFC 冲突 | Windows/macOS | 统一 key + 冲突拒绝 | 文件系统差异 |
| 恶意 Mod | 用户安装的 jar | 明确“Mod 是代码”警告；不自动执行；不内置未知 Mod | 无法沙箱 |
| 恶意 loader/processor | Forge/NeoForge installer | 官方 Maven + hash；argv 数组；无 shell | installer 自身漏洞 |
| 命令注入 | Java/processors | `ArgumentList`；清空危险环境变量；无 shell | 子进程自身行为 |
| DLL 劫持 | Windows 加载 | 应用目录绝对路径；`LoadLibraryEx` 安全搜索 | 本地写权限 |
| Token 泄漏 | 日志/崩溃/UI | OS 凭据库；脱敏；不跨 ABI 传 token | 内存转储 |
| 更新劫持 | 自更新 | Ed25519 manifest + 平台签名；分阶段替换 | 签名私钥失窃 |
| 远程策略滥用 | kill switch | 只降不升；签名；字段白名单；可离线 | 服务端失陷 |
| 恶意镜像 | 下载 | HTTPS + hash + 白名单；失败回退官方 | 镜像拒绝服务 |
| Log4Shell | 1.7–1.18 启动 | 使用版本 JSON 的 `logging.client`；不自行拼配置 | 版本 JSON 缺失 |
| SQL 注入 | 本地 DB | 参数化查询；不拼接 SQL | — |
| JSON DoS | API/配置/计划 | 深度/大小/条目上限 | — |
| 供应链 | 依赖 | 锁文件、SBOM、许可证白名单、CI 审计 | 上游投毒 |

### 14.2 边界控制

| 边界 | 必须做 | 禁止 |
|---|---|---|
| Dart ↔ C++ | 长度/空指针/UTF-8 校验；两段式缓冲 | 信任 Dart 传入路径/JSON |
| C++ ↔ C# | 异常 catch；JSON schema；taskId 生命周期 | 异常跨 ABI；回调后使用 self |
| C++ ↔ Rust | `catch_unwind`；`done` 恰好一次；取消 | panic 展开；跨边界 `String/Vec` |
| C# ↔ 文件系统 | 规范化；原子写；权限/锁处理 | 直接拼接用户路径 |
| Rust ↔ 文件系统 | `dest` 在 root 内；`.part` 同卷；hash | 跨卷 rename；跟随 symlink |
| 子进程 | argv 数组；工作目录；超时；进程树 kill | shell 拼接；继承敏感环境 |
| 网络 | HTTPS；证书校验；代理；UA | 关闭证书校验；http 降级 |
| 日志 | 脱敏；轮转；访问权限 | token/邮箱/完整 argv |

### 14.3 密钥与凭据

- 微软 refresh token、CurseForge key、第三方认证 token：`archoera-vault` 凭据保险库；DB/元数据只存引用。
- 保险库优先使用 OS 安全存储（DPAPI / Keychain / libsecret）；无桌面钥匙串时降级到 0600 `secret.key` 的 encrypted-file 模式，不写明文。
- 旧 `accounts.credentials.json` 首次访问时迁移到保险库并删除；不把密钥写进 `config.json` 或日志。
- 日志脱敏顺序：Bearer/Authorization → token 字段 → 邮箱 → UUID → 路径 → IP。
- 崩溃转储默认本地；上传前提示可能含内存内容；不自动上传。

### 14.4 更新与供应链

- 更新 manifest 用 Ed25519 签名；公钥编译进二进制；签名失败整份丢弃。
- Windows Authenticode/Trusted Signing；macOS Developer ID + notarization；Linux SHA-256 + 包签名。
- 分阶段更新：下载 → 验签 → 关闭主程序 → 替换 → 失败回滚。
- SBOM + `THIRD-PARTY-NOTICES`；CI 许可证白名单；不引入 GPL/AGPL 代码（除独立进程的 authlib-injector）。

### 14.5 本地数据

- 配置/日志/DB 权限：用户可读写；不要求管理员。
- 便携模式不写注册表/系统目录（除用户显式选择）。
- 删除账户时清 token；删除实例移入 trash，不立即物理删除。
- 诊断包导出前展示内容清单，默认不含账户/存档。

---

## 15. 合规与发行架构

### 15.1 发行通道

| 能力 | `official-cn`（L0） | `official-global`（L1/L2） | `dev`（L2） |
|---|---|---|---|
| 版本清单/搜索（只读元数据） | ✓ | ✓ | ✓ |
| 原版游戏文件 | ✗（引导官网） | ✓ | ✓ |
| 加载器/processors | ✗（引导手动） | ✓ | ✓ |
| Mod/整合包/资源包/光影 | ✗ | ✓（L2 需确认） | ✓ |
| Java runtime | ✗（提示手动） | ✓ | ✓ |
| 启动器自更新 | 仅提示 + 官网 | ✓（签名通道） | ✓ |
| 遥测/日志上传 | ✗ | opt-in，默认 ✗ | ✗ |

- **默认构建为 `official-cn`（L0）**；`official-global` 作为单独构建保留。
- 通道是**编译期常量**；`official-cn` consumer 构建在 DI 阶段不注册 L2 provider，`official-global` 保留 L2 注册但默认 L1/需确认。
- CI 断言：`official-cn` 产物不含 L2 端点字符串/注册符号；`official-global` 的 L2 受 ComplianceGate 控制。
- `official-cn` 默认 L0（允许只读元数据），需要绝对零出站时使用 `L0-Z` 严格构建；`official-global` 默认 L1（官方必需内容），L2 需确认。
- 不通过 IP/时区探测地区；地区由发行渠道决定。

### 15.2 Provider + Policy + Plan

```
C# ComplianceGate.Evaluate()
  → defaultLevel = channel 默认（cn disabled / global official / dev all）
  → requested = userConfig.downloadLevel ?? defaultLevel
  → 升到 L2 时检查 consent；cn 升到 L1 时检查 developerMode/ACK/consent
  → effectiveLevel = min(requested, channelMax, machinePolicy, remotePolicy)
  → 通过后生成 DownloadPlan + capability
  → Rust 验 capability 后下载
```

- `disabled < official < all`；机器/远程策略只能降低，用户请求受 `channelMax` 限制。
- C# HTTP 层按用途分类 `metadata/content/telemetry`：`content` 必须过门控，`telemetry` 默认禁止；不按 URL 白名单（镜像换域名不应绕过策略）。
- Plan 带 `provider/source/termsUrl`，UI 能展示来源与条款。
- Rust 不重新解释策略；只执行带能力的计划。

### 15.3 L0/L0-Z/L1/L2 定义

| 等级 | 含义 | 出站 |
|---|---|---|
| L0 | 不下载内容；只读元数据允许 | 版本清单/搜索/详情；不下载游戏/加载器/Mod/Java；远程图片用本地占位 |
| L0-Z | 严格零出站（可选构建） | 进程不发起任何出站连接（回环除外） |
| L1 | 仅官方必需 | Mojang/微软/加载器官方源/Java runtime/自更新 |
| L2 | 全部 | Mod/整合包/资源包/光影/第三方 API |

- 默认：`official-cn` L0；`official-global` L1；`dev` L2（仅开发构建）。
- 通道上限：`official-cn` 最高 L1（official，不提供 L2）；`official-global` 最高 L2；`dev` 最高 L2。
- 任何消费构建的 `L1→L2` 都需应用内第二次确认；`official-cn` 从 L0 升到 L1 需开发者模式 + 声明。
- 若发行地区/法务要求，构建 `L0-Z`；CI 用 network namespace/防火墙断言零出站，不信日志。
- 不受门控：启动已有实例、离线账户、本地扫描/启停、日志、崩溃本地解析、导入本地整合包。
- 门控不能把启动器变成砖头。

### 15.4 同意、审计、capability

- 双文件：`official-cn` 使用 `compliance.json`（用户手写）+ `DEVELOPER_ACK.txt`（安装目录，用户手写）；`official-global` 默认 L1，不需要 ACK。
- 应用永不创建 ACK，永不写 `developerMode: true`；只有降权写接口。
- 三步同意：阅读页 → 勾选 → 输入短语；`official-cn` 升 L1、所有消费构建升 L2 都走此流程；拒绝路径同等视觉权重。
- 审计 JSONL 哈希链；记录 `noticeVersion/noticeSha256/acceptedAt/method/channel/appVersion`。
- capability = `base64url(payload || HMAC-SHA256(进程随机 key, payload))`，payload 含 `planId/level/exp/nonce`；Rust 解析并验签；只防进程内绕过。
- 进程 key 由 C++ 启动时生成，随模块 init 配置传给 C#/Rust；不落盘、不进日志；C++ 不参与策略判断。
- 撤回：`gateEpoch++`；取消在途任务；保留 `.part`。

### 15.5 构建期断言

| 断言 | 方式 |
|---|---|
| consumer 不含 L2 provider | DI 注册表为空/链接裁剪；符号扫描 |
| consumer 不含 L2 端点字符串 | `strings`/二进制扫描 |
| 不含 CurseForge key | secret 扫描 + 构建日志 |
| 不含 debug 升权符号 | `#if DEBUG`；Release 符号检查 |
| 许可证白名单 | `dotnet list package --include-transitive` + `cargo deny` + `flutter pub deps` |
| SBOM | CycloneDX/syft |

### 15.6 AGPL 义务

- 分发二进制时提供完整对应源码（含构建脚本）；网络服务按 **AGPL §13** 向用户提供源码获取方式。
- 根目录 `LICENSE`、`THIRD-PARTY-NOTICES.md`、`NOTICE`（Apache 依赖）。
- `authlib-injector` 作为独立 jar/`-javaagent` 分发时，附其 AGPL 声明与源码链接。
- 不接受外部代码贡献；未来开放 PR 前必须上 CLA。
- 商标/名称/Logo 单独政策；fork 必须改名。

### 15.7 发布物检查

- 安装包签名 + SHA-256 公布。
- 关于页/下载页展示免责声明与源码获取方式。
- 不含游戏本体/assets/libraries、B/C 档代码或资产。
- 隐私政策（若收集任何数据）、遥测 opt-in。
- 第三方许可页面可离线查看。

---

## 16. 性能与容量

### 16.1 预算

| 指标 | 目标 | 测量 |
|---|---|---|
| 冷启动到可交互 | < 1.5s（SSD） | 启动打点 |
| 核心 init（含模块加载） | < 400ms | `system.info` 时间戳 |
| FFI 空调用往返 | < 50µs | 微基准 |
| 事件吞吐 | 10k/s 不丢；UI 合并后 60fps | 压测 |
| 安装 1.21.4 | 带宽跑满；CPU 不成为瓶颈 | 100Mbps/1Gbps 基准 |
| 1000 文件进度 | UI 60fps | 事件压测 |
| 搜索 p95 | 缓存 < 50ms；网络 < 1.5s | 基准 |
| 实例列表 1000 | < 100ms | 基准 |
| 空闲内存 | UI + 核心 < 400MB | RSS |
| 安装峰值内存 | 核心 < 300MB；含 UI < 700MB | RSS（分开记录） |
| 安装包 | < 120MB | 产物 |
| 下载并发 | 全局 16–32；每 host 4–8 | 配置 |

### 16.2 容量与磁盘

- 共享 libraries/assets 只存一份；内容库按 hash 去重。
- 预检：`downloadBytes + uncompressedBytes*1.1 + 64MiB`（同卷）。
- 大文件分片阈值 8 MiB；chunk 4–8 MiB；小文件靠 H2 多路复用。
- 日志/崩溃/缓存单独配额；超限提示清理，不静默删用户数据。

### 16.3 测量方法

- 固定参考机（Windows 11 x64 / Ubuntu 24.04 / macOS arm64）；记录 CPU/磁盘/网络。
- 每次发布跑启动、安装、搜索、UI 滚动、内存基线；回归 >20% 阻断。
- 性能数据写入 `build-info.json` 的基准段，便于对比。

---

## 17. 可观测性与诊断

### 17.1 日志

```json
{
  "ts": "2026-10-20T12:34:56.789Z",
  "level": "info",
  "target": "install",
  "module": "archoera_cs",
  "taskId": 42,
  "message": "download plan created",
  "fields": { "version": "1.21.4", "files": 1180 }
}
```

- 统一 JSON Lines；控制台/文件/诊断包同源。
- `taskId` 贯穿 UI → C++ → C#/Rust；跨模块排障按 taskId 关联。
- 脱敏在写入前完成；日志级别运行期可调，但 Release 默认 `info`。

### 17.2 指标（本地）

| 指标 | 用途 |
|---|---|
| 启动阶段耗时 | 性能回归 |
| 任务成功/失败/取消计数 | 稳定性 |
| 下载速度/重试/换源 | 网络诊断 |
| 搜索延迟/429 次数 | Provider 健康 |
| 崩溃/模块降级 | 发布质量 |
| DB 迁移耗时/大小 | 数据健康 |

- 默认只本地聚合；不上传。诊断包可包含聚合值。

### 17.3 诊断包

```
diagnostics-<timestamp>.zip
├─ build-info.json
├─ modules.json
├─ logs/（脱敏，最近 N 天）
├─ crashes/（如有，需用户确认）
├─ tasks.json（活跃/最近任务）
├─ system.json（OS/架构/内存/磁盘）
└─ README.txt（如何提交）
```

- 导出前展示清单；默认不含账户/存档/完整 argv。
- 崩溃上传 opt-in；上传前可预览。

### 17.4 关于页

- 应用版本/构建号/commit；ABI/模块版本；发行通道；许可证/源码获取方式；更新检查；诊断导出；免责声明。

---

## 18. 测试与质量

### 18.1 测试金字塔

| 层 | 工具 | 覆盖 |
|---|---|---|
| 单元（C#） | xUnit + FsCheck | rules、合并、Java 解析、路径、依赖、合规 |
| 单元（Rust） | `cargo test` + `proptest` | 计划校验、路径、hash、断点、调度 |
| 单元（C++） | Catch2 | ABI 校验、任务状态、事件合并 |
| 契约 | JSON Schema + 黄金文件 | 每个 method/事件；旧 schema 兼容 |
| ABI | C harness | 加载、调用、错误、边界 fuzz |
| 集成 | 假 HTTP + 假 JVM | 安装/续传/取消/启动 argv |
| 安全 | 恶意样本库 | Zip Slip/bomb/保留名/symlink/长路径 |
| UI | `flutter_test` + 截图 | 页面状态、主题、i18n |
| E2E | 三平台 | 装原版 → 启动 → 退出 |
| 性能 | 基准脚本 | 启动/安装/搜索/内存 |

### 18.2 必备用例

> 版本说明：文中的 `1.20.1` / `1.21.4` 是**固定测试基线**，用于快照与回归；实现不得硬编码版本。截至 2026-10-20，Mojang 清单 `latest.release` 为 `26.3`，发布前需另测当时的最新版。

| # | 用例 | 期望 |
|---|---|---|
| 1 | vanilla 1.20.1 合并 | 88 库；mainClass/assetIndex 正确 |
| 2 | Fabric 1.21.1 | 无 hash 库可解析；mainClass=KnotClient |
| 3 | Forge 1.20.1 processors | 输出 sha1 匹配；失败可重试 |
| 4 | NeoForge 21.1 | 坐标/版本规则正确 |
| 5 | 下载中断/续传 | 只补缺失；hash 通过 |
| 6 | 脏镜像 | 换源；全失败报 `HASH_MISMATCH` |
| 7 | 恶意 `.mrpack` | 拒绝路径逃逸/炸弹，无文件写出 |
| 8 | 取消/退出 | 2s 内停止；`.part.meta` 完整 |
| 9 | 崩溃注入 | minidump + 下次启动可见 |
| 10 | 热重启 | 任务继续；UI 快照正确 |
| 11 | 合规 L0-Z | 零出站（防火墙断言） |
| 12 | 账户 token | 日志/诊断无明文 |

### 18.3 CI 矩阵

| 平台 | 架构 | 必跑 |
|---|---|---|
| Windows 11 | x64 | ABI/集成/E2E/签名冒烟 |
| Ubuntu 24.04 | x64 | ABI/集成/E2E/ASan |
| macOS 14/15 | arm64 | ABI/集成/E2E/签名冒烟 |
| Linux | arm64（可选） | 构建/单测 |

- 每次 PR：lint、单测、契约、许可证扫描。
- 每次 tag：三平台完整构建、E2E、SBOM、安装包、校验和。

### 18.4 发布门禁

- P1/P2 验收表全绿；
- 无 P0/P1 缺陷；
- 崩溃率/启动失败率基线达标；
- 许可证/SBOM/免责声明齐全；
- 安装包签名验证通过；
- 回滚演练成功。

---

## 19. 构建、打包与发布

### 19.1 工具链

| 工具 | 版本策略 |
|---|---|
| .NET SDK | `global.json` 锁定 10.x |
| Rust | `rust-toolchain.toml` 锁定 stable |
| CMake/Ninja | `CMakePresets.json` 锁定最低版本 |
| Flutter/Dart | `app/.fvmrc` 锁定 stable |
| C++ | C++20；MSVC/Clang/GCC 三平台 |

### 19.2 构建图

```
CMake (top-level)
├─ app/core/                  # C++ archoera_core
├─ ExternalProject: cargo    # archoera_dl + archoera_updater
├─ ExternalProject: dotnet   # archoera_cs (PublishAot/NativeLib=Shared)
├─ app/                      # flutter build <platform>
└─ package                   # 复制产物 + 签名 + 安装包
```

命令示例：

```bash
cmake --preset release
cmake --build --preset release
cargo build --release -p archoera_dl
dotnet publish app/modules/business/Archoera.Exports -c Release -r win-x64 \
  -p:PublishAot=true -p:NativeLib=Shared
flutter build windows --release
```

> macOS 使用 `-r osx-arm64` 与 `flutter build macos --release`；不做 x64/universal。
> 具体 flag 以实际 AOT/链接器验证为准；CI 必须锁版本，不使用“最新”。

### 19.3 产物布局

```
ArchoeraCX/
├─ ArchoeraCX(.exe)
├─ data/flutter_assets/...
├─ native/
│  ├─ archoera_core.(dll|so|dylib)
│  ├─ archoera_cs.(dll|so|dylib)
│  └─ archoera_dl.(dll|so|dylib)
├─ modules/*.json             # 诊断清单
├─ updater(.exe)
└─ THIRD-PARTY-NOTICES.md
```

### 19.4 平台注意

| 平台 | 注意 |
|---|---|
| Windows | 统一 CRT；`/DELAYLOAD` 可选；导出 `.def`；`LoadLibraryEx` 安全搜索；签名 |
| Linux | `-fvisibility=hidden` + version script 只导出 `archoera_*`；`$ORIGIN` RPATH；AppImage/Flatpak |
| macOS | 仅 arm64；`@rpath`；嵌套 dylib 逐个签名后再签 App；notarization |
| 便携 | 数据目录可写；不写系统目录；卸载不删用户数据（可选） |

### 19.5 更新器

- 独立可执行文件（Rust，复用 `archoera_dl` 的 HTTP/校验代码）；下载 → 验签 → 等待主程序退出 → 替换 → 失败回滚 → 重启。
- 不依赖主进程的 HTTP/C++ 枢纽；主进程只负责检查/启动更新器。
- 不占用自身文件；更新期间不启动游戏。
- 更新日志与回滚结果写入 `logs/updater.log`；下次启动检查。

### 19.6 发布流程

1. 冻结代码/契约；更新版本号与 CHANGELOG。
2. CI 三平台构建 + 测试 + 安全扫描。
3. 生成 SBOM/第三方许可；签名；计算 SHA-256。
4. 冒烟安装/启动/卸载；回滚演练。
5. 发布源码归档、安装包、校验和、免责声明。
6. 更新通道灰度（可用签名 manifest 控制）。

---

## 20. 实施路线与验收

### 20.1 阶段映射

| 阶段 | 主题 | 本文对应 | 验收 |
|---|---|---|---|
| P0 | 选型与文档 | 全部 | 文档/ADR/参考清单完成 |
| P1 | ABI + 枢纽骨架 | §5–§9 | 三平台 Dart→C++→C#→Rust 垂直切片；装原版+启动 |
| P2 | FFI + UI 壳 | §10 | UI 安装/启动；事件泵；热重启 |
| P3 | 账户 | §8.8 | 正版登录/离线/第三方；token 安全 |
| P4 | 加载器 | §8.5/§12.6 | Fabric/Quilt/Forge/NeoForge |
| P5 | 实例与资源 | §8.9–§8.11/§11 | 多实例/Mod/整合包 |
| P6 | 体验与打磨 | §10 | 主题/动效/3D/托盘/i18n |
| P7 | 发布 | §15/§19 | 更新/签名/安装包/合规 |

### 20.2 垂直切片（P1 的前 2–3 周；P1 总计 4–5 周）

1. `archoera_core` + `archoera_abi.h`：`ping` 返回字符串；三平台加载。
2. 事件泵：异步任务产生 `TASK_PROGRESS`；Dart 由 `wake` 驱动 drain，合并刷新一个进度条。
3. C# 模块：`java.scan` 返回真实 Java 列表（纯类库 + AOT 导出）。
4. Rust 模块：本地假 HTTP 100 文件下载 + 校验 + 进度。
5. 接通“扫描 Java → 安装 1.21.4 → 启动（假 JVM/真实 JVM）”。
6. 压测：取消/断网/磁盘满/崩溃注入。

> ### 实例目录识别

`instance.sync` 不要求实例必须带 `.archoera/instance.json`。它会识别：

- ArchoeraCX 自有布局：`<instance>/.archoera/instance.json`
- LauncherX / SJMCL：`versions/<name>/sjmclcfg.json` + `lx_profiles.json`
- MultiMC / Prism：`mmc-pack.json`、`instance.cfg`
- Modrinth 整合包：`modrinth.index.json`
- 通用游戏目录：`versions/<name>/` 下的版本 JSON、`mods/`、`saves/`、`options.txt`、`config/`

导入采用**只读发现 + SQLite 索引**：不会为导入实例写 `.archoera`，ID 由规范化路径的 SHA-256 稳定生成；`instance.list` 返回 `instanceRoot` / `custom` / `missing`。添加根目录后会自动同步，实例页也提供手动“扫描实例”。

### 跨平台路径约束

- 原生库、模块和 `archoera-dl` 的解析必须以**可执行文件目录**为基准（Windows runner、Linux bundle `lib/`、macOS `Contents/Frameworks`），不能依赖进程 CWD。
- 进程 CWD 可能因为安装目录被替换/删除而失效；所有 `Directory.current`/`Environment.CurrentDirectory` 访问必须容错。
- Java 探测等子进程必须显式设置有效 `WorkingDirectory`，避免子进程继承已删除的 CWD。
- 数据目录仍优先使用 `ARCHOERACX_DATA_DIR`，否则按 Windows `%APPDATA%`、macOS `Application Support`、Linux XDG/`~/.local/share` 解析。

**当前进度（2026-10-21）**：C++ `system.ping` / `system.pingAsync` + wake 驱动 `EventPump`、`ModuleRegistry` / `MethodRegistry` / `system.modules`、C# `BusinessModuleRegistry`、NativeAOT `java.scan`、`version.list`、`loader.check`/`plan`/`verify`/`repair`、`launch.start` 均已在 Linux x64 跑通。`launch.start` 当前支持离线账户：自动选择匹配游戏版本的 Java（也可在设置中手动指定并写入 `settings.json`），内存、GC 预设、JVM 参数和附加游戏参数持久化，构建 classpath/参数、提取 natives 并启动 JVM；进程状态/停止/日志流尚未接线。Modrinth 模组安装会递归安装 required 依赖（深度 8、环检测）。Java 扫描按 `java.home` 去重并扩展 SDKMAN/asdf/mise/.jdks 等用户目录。设置支持多个实例根（`instancesRoots`，首项为默认）并提供 `instance.sync` 采纳已存在的实例目录。Flutter 实例 UI 支持创建/选择/删除/搜索、多根目录与自定义目录选择；页面切换：首页切换用旧页快照覆盖，其他页面之间上浮淡入，切回首页收回；快照等比绘制避免底栏高度变化导致形变；实例“更多”支持 Forge/NeoForge 加载器安装、Mod 启停/删除与 quick/full 依赖检查；下载页提供版本列表、全局 Mod 搜索、项目详情画廊与排序/分页；Mod/资源包/光影结果带图标，详情通过 `resource.project` 获取 Modrinth 画廊。实例创建与 Forge/NeoForge 安装弹窗通过 `loader.versions` 列出全部加载器版本，可选择旧版。Forge/NeoForge 安装前通过 `java.select` 解析 Java，下载后调用 `loader.repair` 执行 processors 并校验输出。Microsoft Device Code 登录链、重排后的账户页、离线账户、刷新和凭据文件已接入；账户页采用当前账户 Hero + 单账户列表，设置页采用分区导航 + 平铺设置组，内联编辑内存/GC/JVM/游戏参数，并展示系统总内存、可用内存和启动后预计占用；下载任务通过模块 ABI v2 上报进度，右上角任务中心可查看/取消；实例创建支持 `versions/` 版本隔离，原版下载优先官方源、失败回退 BMCLAPI，跨类型依赖按项目类型补全；真实 Entra client_id 与 Minecraft Services 审核仍待完成。269 个 C# 测试、36 个 Flutter 测试通过；Fabric/Quilt 无 Java 安装、正式 C++ DownloadManager/Rust module、完整 LaunchPlan/进程管理、凭据保险库已接入（crypto/file），CurseForge 尚待接入。

**P1 完成定义**

- ABI 契约测试全绿；三平台 CI 通过。
- 安装 1.21.4 哈希全匹配；启动进入主菜单（或假 JVM argv 断言）。
- 断点续传、取消、shutdown 可复现。
- 崩溃生成 minidump；诊断包可导出。
- CLI 能无 UI 跑同一流程。

### 20.3 每阶段 DoD

- 代码 + 单测 + 契约测试；
- 文档/ADR 更新；
- 性能基线记录；
- 安全/合规检查；
- 可回滚；
- 演示脚本/验收记录。

---

## 21. 风险与未决问题

### 21.1 风险登记

| 风险 | 影响 | 概率 | 缓解 |
|---|---|---|---|
| 4 门语言拖慢迭代 | 高 | 高 | 垂直切片；容器化构建；契约先行；按阶段评估是否保留 Rust |
| 单进程崩溃不隔离 | 高 | 中 | Crashpad/minidump；关键任务可降级子进程；发布前崩溃注入 |
| NativeAOT 调试困难 | 高 | 高 | 业务纯类库 + DevHost + 单测 + 日志 |
| ABI 漂移 | 高 | 中 | 版本握手；同包发布；契约测试；schema CI |
| 跨语言内存错误 | 高 | 中 | 所有权矩阵；ASan/UBSan；边界 fuzz |
| C#/Rust 代理/TLS 行为不一致 | 中 | 高 | C# 解析代理并下发；同 UA/超时；集成测试 |
| Forge/NeoForge processors 碎片化 | 高 | 高 | 每加载器适配层 + 真实样本 + artifact manifest；不依赖 `outputs` 是否完整，按 producerIndex 校验/重跑 |
| 整合包恶意路径 | 高 | 中 | 自写解压器 + 恶意样本库 + 预算 |
| 磁盘/文件锁 | 中 | 高 | 原子写、重试、明确错误码、预检 |
| 微软认证变更 | 高 | 低 | `IAuthProvider` 抽象；错误可诊断 |
| 许可证返工 | 高 | 低 | AGPL 已定；CI 白名单；不复制 B/C 档代码 |
| 包体/内存超标 | 中 | 高 | AOT 裁剪、按需加载、基线回归 |
| 热重启事件丢失 | 中 | 中 | seq + snapshot + 全量刷新兜底 |
| 合规门控被误解为 DRM | 中 | 中 | 文档/UI 明说；不宣称技术强制 |

### 21.2 已定补充决策（2026-10-20）

| # | 决策 | 结论 |
|---|---|---|
| D1 | 默认发行通道 | **`official-cn`（L0）**；`official-global` 作为单独构建保留（L1/L2） |
| D2 | macOS 架构 | **仅 Apple Silicon arm64**；不做 x64/universal，下载页明确标注 |
| D3 | 微软 OAuth 重定向 | **默认 Device Code Flow（无 redirect URI）**；浏览器备选用 **loopback + auth code + PKCE**（`http://localhost:<ephemeral>/callback`）；自定义协议不作为主方案 |
| D4 | SQLite 归属 | **只由 C# 持有**；Flutter 不引入数据库插件/不直接读 SQLite；C++ 只路由。Linux/macOS 用系统 SQLite，Windows 随 C# 模块分发 `e_sqlite3.dll` |
| D5 | 账户凭据 | device code/access/refresh token **不跨 ABI、不写日志**；C# `VaultCredentialStore` 经 `archoera-vault` 加密存储，旧 `accounts.credentials.json` 首次迁移后删除 |

### 21.3 待决策

| # | 问题 | 影响 | 建议 |
|---|---|---|---|
| D3 | Windows/Linux ARM64 | 构建矩阵 | 先 x64；有需求再加 |
| D4 | CurseForge key 策略（用户自备/自建网关） | 合规/体验 | 先用户自备；网关另立条款 |
| D5 | 遥测/崩溃上传 | 隐私/排障 | 默认关闭；opt-in；可预览 |
| D6 | 自动更新默认开关 | 安全/体验 | 默认提示更新，不静默 |
| D7 | Riverpod 版本升级/代码生成策略 | UI 维护 | 锁定大版本；PoC 通过后再启用代码生成 |
| D8 | 3D 皮肤方案 | 体验/包体 | 预渲染优先；WebView 备选 |
| D9 | OptiFine 支持 | 条款/需求 | 手动导入优先 |
| D10 | 是否保留 Rust（下载压测后） | 维护成本 | P1 后按数据决定；ABI 不变 |
| D11 | ABI v1 冻结时间 | 兼容性 | P1 结束前冻结；之后只加可选字段 |
| D12 | 多窗口/独立日志窗 | UI 复杂度 | 单窗口 + 内嵌面板 |

---

## 22. 附录

### 22.1 术语

| 术语 | 含义 |
|---|---|
| 有效版本（EffectiveVersion） | 合并 `inheritsFrom` 链、rules/args 保留后的版本模型 |
| 下载计划（DownloadPlan） | C# 生成的只读文件清单；Rust 只执行 |
| 效果（Effect） | C# 返回给 C++ 的“请执行下载”指令；完成后回调 C# |
| capability | C# 签发的短期下载许可；Rust 验签 |
| L0/L0-Z/L1/L2 | 内容下载等级；L0 允许只读元数据，L0-Z 零出站 |
| 共享库 | `shared/{libraries,assets,versions}` |
| 内容库 | `downloads/objects`，按 hash 去重 |
| 实例 | 一个游戏目录 + `instance.json` + 加载器/账户/设置 |
| 托管 runtime | 启动器下载/管理的 Java |
| PendingProcessors | 无 Java 时延迟到首次启动前执行的 Forge/NeoForge processors |

### 22.2 契约文件清单

```
contracts/
├─ abi/archoera_abi.h
├─ module/archoera_module_abi.h
├─ methods/<domain>.<method>.schema.json
├─ events/<type>.schema.json
├─ errors/codes.json
└─ build-info.schema.json
```

### 22.3 参考文档

- `docs/08-technical-research/01-version-merge.md` — 版本合并
- `docs/08-technical-research/02-loader-install.md` — 加载器/processors
- `docs/08-technical-research/03-mod-search.md` — 搜索/更新
- `docs/08-technical-research/04-crash-analysis.md` — 崩溃分析
- `docs/08-technical-research/05-download-engine.md` — 下载引擎
- `docs/08-technical-research/06-modpack-install.md` — 整合包/解压安全
- `docs/08-technical-research/07-compliance-gate.md` — 合规开关
- `docs/09-distribution-and-download-policy.md` — 发行/下载策略
- `docs/06-ecosystem-apis.md` — 外部接口
- `docs/05-roadmap.md` — 阶段/验收

### 22.4 本次检查修正（2026-10-20）

| # | 原问题 | 处理 |
|---|---|---|
| 1 | `07` 顶部仍写“C 管网络传输” | 改为“Rust 管文件下载；C 网络层已取消” |
| 2 | `07` ABI 草案写“task_id=0 表示同步完成” | 统一为“task_id 恒 > 0；同步完成也产生 Completed 事件” |
| 3 | `04` 检查表写“AGPL 源码获取（当前不适用）” | 改为“当前必须做” |
| 4 | `05` P0 产出只写 `docs/01–07` | 更新为 `docs/01–10` |
| 5 | `02` 写“失败回退 C# 下载器”“ModuleHost 卸载模块” | 改为“禁用下载，不做第二套下载器”“加载后不卸载” |
| 6 | `03` 把 authlib-injector 说成“独立进程” | 改为“独立 jar / `-javaagent`” |
| 7 | `09`/`08-07` 的 L0 与“零出站”定义冲突 | 引入 `L0-Z` 严格构建；L0 允许只读元数据 |
| 8 | `07`/`08-05` 的 `post.extract_natives` 与“C# 解压”冲突 | Rust v1 不解压；`extract_natives` 由 C# 执行 |
| 9 | 更新器下载归 C++，与“C++ 不实现 HTTP”冲突 | 更新器改为独立 Rust 可执行文件 |
| 10 | 示例版本 1.21.4 未说明是否最新 | 标注为固定测试基线；记录 2026-10-20 清单最新 release 为 26.3 |
| 11 | `README` 文档索引缺少 10、顺序不自然 | 增加 10 并调整顺序 |
| 12 | `07` 把 C# 模块写成可加载 C#/Rust/C | 去掉已取消的 C |
| 13 | 默认发行通道未定 | 定为 `official-cn`（L0）；`official-global` 单独构建（L1/L2） |
| 14 | macOS 架构未定 | 仅支持 Apple Silicon arm64 |

### 22.5 变更记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-20 | Draft v1 | 初版：总体架构、ABI v1、模块协议、C#/Rust/Flutter 分层、数据模型、关键流程、安全/测试/构建；同时修正跨文档一致性问题（见 22.4） |

---

> **一句话**：Flutter 只画界面，C++ 只做枢纽，C# 拥有业务与 API，Rust 只搬字节；
> 所有跨边界的东西都有 schema、owner、生命周期和失败路径。
