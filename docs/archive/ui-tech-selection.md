# 01 · UI 技术选型

> **归档文档**（原 `docs/01-tech-selection.md`）。选型对比与决策依据，结论已落地为 Flutter + C++ 枢纽。
> 现行规范见 [`../10-architecture-design.md`](../10-architecture-design.md) 与 [`../11-ui-design-system.md`](../11-ui-design-system.md)；归档说明见 [`README.md`](README.md)。

> **2026-10 更新：UI 已定为 Flutter，对外边界定为 C++ 枢纽 + C ABI。**
> 本文保留选型对比作为决策依据；落地形态与 FFI 细节见 [`ffi-architecture.md`](ffi-architecture.md) 与 [`polyglot-core-ffi.md`](polyglot-core-ffi.md)。

> **结论先行**
> 1. 如果"未来可能闭源 / 可能商业化"是认真保留的选项：**Avalonia 是总成本最低的方案**（单语言、单运行时、MIT、NativeAOT 已验证可行）。
> 2. 如果 UI 表现力、动效开发效率、跨端像素级一致性优先：**Flutter 可用**，但要接受"双运行时 + IPC + 桌面插件债"三项长期成本。
> 3. 无论选哪个，**都建议先做两周 PoC**（见 §6），用同一张页面各实现一遍再定稿。

---

## 1. 约束条件

| 约束 | 说明 |
|---|---|
| 核心语言 | C# / .NET 10（LTS），可能 NativeAOT |
| 许可证 | 必须允许**开源分发**（AGPL-3.0-only）；依赖以 MIT/Apache/BSD 为主 |
| 平台 | Windows 10/11 优先；Linux（X11 + Wayland）；macOS 尽力 |
| 团队 | 小团队/单人长期维护，**维护成本 > 初期开发速度** |
| 必备能力 | 自绘标题栏、Mica/Acrylic、托盘、单实例、拖拽导入、3D 皮肤、Markdown、i18n、自动更新 |
| 发行 | 官网 exe/dmg/AppImage/Flatpak；**可能**上 Microsoft Store；**大概率不上** Mac App Store |

---

## 2. 候选对比

| 维度 | Flutter | Avalonia | WinUI 3 / WPF | Tauri 2 | Compose MP | Electron |
|---|---|---|---|---|---|---|
| UI 语言 | Dart | C# / XAML | C# / XAML | TS + CSS | Kotlin | TS + CSS |
| 许可证 | BSD-3（引擎） | MIT | MIT | MIT / Apache-2.0 | Apache-2.0 | MIT |
| 闭源友好 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 与 C# 核心 | **跨进程**（或 FFI） | **同进程直接调用** | 同进程 | sidecar 跨进程 | 跨进程/JVM | 跨进程 |
| 运行时数量 | Dart + .NET | 仅 .NET | 仅 .NET | WebView + Rust + .NET | JVM + .NET | Chromium + .NET |
| 冷启动 | 中 | **快**（NativeAOT 可 <300ms） | 快 | 快 | 慢（JVM） | 慢 |
| 包体（空壳） | ~30–45 MB | ~15–40 MB（AOT） | ~20–60 MB | ~5–15 MB | ~80 MB+ | ~120 MB+ |
| 内存（空壳） | ~150–250 MB | ~80–150 MB | ~100 MB | ~120 MB | ~250 MB | ~250 MB+ |
| UI 表现力 | **最强**（自绘、动效、跨端一致） | 强（Skia，需自己写动效） | 中（系统控件，Win 限定） | 强（CSS 生态） | 强 | 强 |
| 桌面系统集成 | **弱**（靠插件） | 强 | **最强** | 中 | 弱 | 中 |
| 中文 IME | 中（历史问题多，已改善） | 好 | 最好 | 好 | 中 | 好 |
| 3D / 皮肤 | 弱 | 中（可嵌 OpenGL/Silk.NET） | 中 | 中（WebGL 可用） | 中 | 强（WebGL） |
| 生态/招聘 | 中 | 中 | 中（仅 Win） | 中 | 小 | 大 |
| 长期维护风险 | 低（Google） | 中（社区） | 低 | 中（Rust 工具链） | 中 | 低 |

---

## 3. Flutter 的真实成本（按优先级）

> 这些不是"缺点罗列"，而是**必须提前设计规避**的工程约束。

### 3.1 双运行时与跨语言边界（最大成本）

Flutter 不能直接调用 .NET 对象。可选：

| 方式 | 复杂度 | 延迟 | 备注 |
|---|---|---|---|
| **FFI（C ABI + C++ 枢纽 + NativeAOT）** | 高 | 最低 | **已选**；无崩溃隔离，见 `polyglot-core-ffi.md` |
| stdio JSON-RPC | 低 | 低 | 仅作开发/CI 备用通道 |
| 本地 HTTP + SSE | 低 | 低 | 端口/防火墙/安全面 |
| gRPC（TCP loopback） | 中 | 低 | 契约强、流式好；Dart 端 UDS/命名管道支持需实测 |

**代价**：跨语言内存所有权、错误码、进度事件、取消语义；调试需要跨运行时看栈；任一模块崩溃 = 整个应用退出。
**收益**：低延迟、核心可 CLI 化、UI 可热重载、未来可换 UI。

### 3.2 桌面插件生态（第二成本）

高频依赖几乎都是第三方个人维护：

- `window_manager` / `bitsdojo_window` — 无边框、最小尺寸、居中
- `flutter_acrylic` — **仅 Windows** 的 Mica/Acrylic
- `system_tray` / `tray_manager` — 托盘
- `launch_at_startup`、`single_instance`、`screen_retriever`
- `webview_windows` / `inappwebview` — Mod 描述页、OAuth 回调
- `file_selector`（官方）、`desktop_drop`（拖拽）

风险：Flutter 大版本升级时集中失配；Linux Wayland 下行为不一致。
缓解：**把平台能力收敛到 5 个以内的插件**，其余全部走 C# 核心（C# 侧做托盘/单实例/文件关联反而更稳）。

### 3.3 3D 皮肤查看器

Flutter 没有成熟的 3D 引擎。三条路：

1. **C# 侧预渲染**（推荐）：SkiaSharp/ImageSharp 渲染 2D 展开图 + 变换，或离屏渲染转盘帧序列；UI 只做播放。
2. 嵌入原生视图（Windows HWND / GTK Widget）— 复杂、平台分裂。
3. WebView + `skinview3d`（MIT）— 简单，但多一个 WebView 实例与内存开销。

> 别指望 `three_dart` / `flutter_gl` 这类长期缺维护的包。

### 3.4 窗口装饰与系统感

- 自绘标题栏 = 自己实现拖动、双击最大化、Aero Snap、高 DPI 缩放、最大化时不留边距。
- Windows 11 的 Mica 需要 DWM API；Linux 需要 compositor 差异处理；macOS 需要 NSVisualEffectView。
- 结论：**平台差异代码一定会存在**，放 C# 侧比放 Dart 插件侧可控。

### 3.5 输入法、无障碍、多窗口

- 中文输入法在 Flutter Desktop 历史上问题较多（候选框位置、组合输入），**必须在 PoC 中实测**。
- 无障碍（屏幕阅读器）在桌面端基本不可用。
- 多窗口（如独立日志窗）支持弱，建议单窗口 + 内嵌面板。

### 3.6 包体与内存

Flutter 空壳 ~30–45 MB，加上 .NET NativeAOT 核心 20–60 MB，安装包 70–110 MB 属正常。
对比 Avalonia 单栈可控制在 40–70 MB。**对启动器这个品类可接受，但不是优势。**

### 3.7 现实先例（很有参考价值）

| 项目 | 形态 | 说明 |
|---|---|---|
| Kraft Launcher（MIT） | 纯 Dart/Flutter | 用 Dart workspace 组织完整启动器逻辑，证明"核心不必是 C# 这类原生语言"，也说明 Dart 侧的文件/进程管理可行 |
| Foast（MIT） | Flutter | 轻量实现，可读性好 |
| RPMLauncher（GPL-3.0） | Flutter | 中文社区，桌面端页面组织参考 |
| **SJMCL（GPL-3.0+）** | **Tauri 2 + Rust + Next.js**（非 Flutter） | 同期同类项目选择了"原生核心 + Web UI"，而非 Flutter |

> 启示：你的"C# 核心 + Flutter UI"方向与"原生核心 + 独立 UI"的共识一致（SJMCL 用 Rust + Web，你用 C# + Flutter），**这条路是通的**，代价就是本文列出的 IPC 与双运行时成本。
> 若愿意接受纯 Dart 核心，Kraft Launcher 证明可以省掉 IPC——但你会失去 .NET 生态与 NativeAOT 的可选性。

---

## 4. 如果换栈：Avalonia（保留为对照/退路）

> UI 已定为 Flutter；本节仅用于评估退路与两栈差异。

**优势**

- 单语言单运行时，核心逻辑直接 in-process 调用，**没有 IPC**。
- MIT；NativeAOT 已被商业启动器验证（Avalonia + .NET + NativeAOT 的组合已有实际产品）。
- `FluentAvalonia` 提供 Mica/Acrylic、Fluent 控件；窗口/托盘/单实例生态成熟。
- XAML 的样式/模板系统适合做"主题令牌"（参考通用的主题令牌思路）。
- 可直接嵌 OpenGL（Silk.NET / OpenTK）做 3D 皮肤。

**劣势**

- 动效表达力不如 Flutter，复杂转场要手写 `Animation`/合成器。
- 设计器体验弱（Rider 预览有限），UI 迭代靠热重载 + 手写 XAML。
- 社区规模小于 Flutter，遇到诡异渲染问题可参考资料少。
- Linux 下字体渲染与 Wayland 支持仍在完善。

**适合**：以 Windows 为主、想快速做出"原生感"、团队 C# 熟练。

---

## 5. 其他方案为什么不推荐

| 方案 | 不推荐原因 |
|---|---|
| WinUI 3 / WPF | Windows only。若确定永不跨平台，WPF（MIT）其实是性价比最高的选择，但和"跨平台"目标冲突 |
| Tauri 2 | 前端体验好、包体小，但 C# 核心只能做 sidecar → 仍是跨进程，还多一套 Rust 工具链 |
| Compose Multiplatform | JVM + .NET 双运行时，启动与内存最差，桌面生态仍早期 |
| Electron | 已被 XMCL/旧版 Modrinth 验证可行，但内存/包体最差；无必要 |
| Qt | LGPL 动态链接对闭源可行但合规负担高（替换库、提供目标文件），且 C# 绑定（Qml.Net）生态弱 |

---

## 6. 两周 PoC 验收清单（定稿前必做）

同一张"实例详情页"，用 Flutter 实现；若对选型仍有疑虑，再用 Avalonia 做对照。逐项打分。

| # | 验证项 | 通过标准 |
|---|---|---|
| 1 | 无边框窗口 + Mica/Acrylic + 拖动/双击最大化/Aero Snap | 无明显错位，多显示器/150% 缩放下正常 |
| 2 | 10 万行日志实时滚动 | 60fps，不卡 UI，内存不持续增长 |
| 3 | 3D 皮肤旋转 | ≥30fps；或 2D 方案视觉可接受 |
| 4 | 中文输入法搜索框 | 候选框位置正确，无丢字 |
| 5 | 托盘 + 最小化到托盘 + 单实例唤醒 | 三平台行为一致 |
| 6 | 1000+ 文件下载进度刷新 | 批量进度合并后不卡顿 |
| 7 | 拖拽 `.mrpack`/`.zip` 到窗口 | 正确触发导入流程 |
| 8 | 冷启动到可交互 | < 1.5s（SSD） |
| 9 | 安装包体积 / 空闲内存 | 记录基线，作为长期回归指标 |
| 10 | FFI 加载链 | Dart → C++ → C#(AOT) → 返回字符串，三平台成功 |
| 11 | 原生线程回调 | C++ 工作线程 → `NativeCallable.listener` → UI 更新，不崩不卡 |

> 打分维度：开发耗时、代码量、平台兼容问题数、视觉还原度、后续加功能的边际成本。

---

## 7. 已定方案：Flutter + C++ 枢纽（附条件）

**结论**：UI 用 Flutter，Dart 只加载**一个** C ABI 库（C++ 枢纽）；C#（业务 + API，NativeAOT）、Rust（下载）作为内部模块，由 C++ 加载与编排。

### 7.1 必须先接受的三个前提

1. **崩溃不隔离**：任一模块崩溃 = 整个应用退出。必须投入崩溃处理器、minidump、统一日志。
2. **调试复杂**：C# 走 AOT 后没有托管调试器；业务逻辑必须放纯类库并用单测覆盖。
3. **构建复杂**：CMake + cargo + dotnet publish + Flutter 打包，三平台矩阵。

> 任何一条做不到，都应退回"分进程"或"Avalonia 单栈"。

### 7.2 直接连 C# 的可行性（回答你的问题）

技术上可行：NativeAOT 导出 `UnmanagedCallersOnly` 的 C 函数，Dart 用 `dart:ffi` 加载。

但**不建议**让 Dart 同时加载 C++ 和 C#：

- 边界从 1 个变 N 个，版本/生命周期/崩溃责任分散；
- 未来换 UI 或加 CLI 时要改多处；
- C++ 枢纽本来就要加载 C#，让它做唯一入口更干净。

实现细节（导出限制、字符串、线程、调试）见 [`polyglot-core-ffi.md`](polyglot-core-ffi.md) §0、§5。

### 7.3 Flutter 侧必须遵守的约束

1. **UI 零业务逻辑**：所有文件、网络、进程、SQLite 操作在核心。
2. **平台能力下沉**：托盘、单实例、文件关联、更新、协议注册放 C++。
3. **插件白名单**：`window_manager`、`file_selector`、`desktop_drop`、`url_launcher`、`webview_*`（按需）。
4. **主题系统自研**：统一设计令牌，不混搭 `fluent_ui` / `macos_ui`。
5. **3D 皮肤用预渲染**，不在 Flutter 内做实时 3D。
6. **所有 ABI 调用非阻塞**：投递任务 + 事件回调，禁止在 UI isolate 里同步等待。

### 7.4 保留的退路

- 若 FFI 集成把迭代速度拖垮：把 C# 模块改为**独立进程 + 本地 IPC**，C++ 只保留下载/网络。ABI 不变，只换传输层。
- 若 Flutter 桌面插件问题超出预期：C++ 枢纽已把平台能力收走，换 Avalonia 时业务层可复用。
- 若多语言维护不动：先合并 Rust（下载回到 C#），或把 C# 模块改为独立进程 + 本地 IPC，再考虑减语言。
