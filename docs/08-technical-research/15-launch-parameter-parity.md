# 15 · 启动参数与 Prism 的差距（只读研究）

> 目的：对照 Prism Launcher 的启动/游戏参数设置，判断 `ArchoeraCX` 还缺什么、哪些值得补、哪些不该照搬。
> 方法：只读 Prism 源码（`PrismLauncher/PrismLauncher@d835fc7`，2026-10-24，GPL-3.0）与 UI 文件，记录字段与行为；**不复制代码/文案**。
> 标记：**[事实]** 源码可核对；**[推断]** 基于实现的判断；**[未验证]** 需要实测。
> 结论：Prism 的“多”主要不在 game args，而在**包装命令、环境变量、自动加入、pre/post 命令、控制台/窗口行为、性能开关**；我们已有 game args（Prism 反而没有），但上述几项是真缺口。

> **实现状态（2026-10-25）**
>
> - **[已实现]** 引号感知 tokenizer（JVM/game/wrapper/命令）；全局与每实例 `wrapperCommand`、`environment`（合并顺序：自动优化 → 性能预设 → 全局 → 实例；实例 `{}` 清空）；每实例 `joinServerOnLaunch` / `joinServerAddress` / `joinWorldName`（版本支持时用 Quick Play，否则 `--server/--port`）；pre-load / pre-launch / post-exit 命令（无 shell、超时、`INST_*` 环境变量、pre 失败中止、post 失败仅记日志、`INST_JAVA_ARGS` 脱敏、命令日志 0600 并纳入 `launch-*.log` 轮转）；窗口三态（最大化用主显示器尺寸近似，fullscreen 优先）；Linux GameMode / MangoHud / Zink 预设（wrapper/env，用户值覆盖预设）；原生 GLFW/OpenAL/SDL 覆盖仅在独立的 `developerMode` + Linux + 文件存在时生效，与下载合规的开发者模式无关；settings schema 15；`settings.setRuntimeOptions` 已登记 C++/C#。pre-load 在自动选择 Java 之前运行，`INST_JAVA` 只有在实例显式固定 Java 时才有值。
> - **[未实现]** 独立控制台窗口（我们使用日志页/任务中心）、“启动后隐藏启动器”/“游戏退出后退出启动器”（需要进程退出事件，另开一批）、系统级 GLFW/OpenAL/SDL 只保留开发者选项，不做图形化自动探测。

---

## 1. Prism 的设置面

### 1.1 Java（全局 + 每实例可覆盖）

| 设置 | 行为 | 我们的对应 |
|---|---|---|
| `JavaPath` / 自动检测 | 手动路径或扫描选择 | 有（`JavaPath`/`JavaAuto` + runtime 扫描） |
| `AutoDownloadJava` | 按 Mojang 推荐自动下载 | 无（走 Java runtime 计划，不自动选） |
| `SkipCompatibilityCheck` / 跳过启动向导 | 关闭版本兼容检查/首次提示 | 无 |
| `MinMemAlloc` / `MaxMemAlloc` | `-Xms`/`-Xmx` | 有（全局 + 实例） |
| `PermGen` | 旧版 `-XX:PermSize` | 无（现代 JVM 不需要，**不建议补**） |
| `LowMemWarning` | 内存低于系统容量时提示 | 有低内存提示（实例对话框） |
| `JvmArgs` / `OverrideJavaArgs` | 原始 JVM 参数，实例可覆盖 | 有（`JvmArgs` + GC 预设） |
| Game args | **Prism 没有自定义 game args 字段** | 有（`GameArgs`，是我们的优势） |

### 1.2 游戏窗口 / 控制台 / 杂项（每实例）

| 设置 | 行为 | 我们的对应 |
|---|---|---|
| `MinecraftWinWidth/Height` | `--width`/`--height` | 有 |
| `LaunchMaximized` | 近似最大化（部分版本不支持） | 无；只有 `--fullscreen` |
| `CloseAfterLaunch` / `QuitAfterGameStop` | 启动后隐藏/退出启动器 | 无（UI 行为） |
| `ShowConsole` / `ShowConsoleError` / `AutoCloseConsole` | 控制台窗口 | 无（我们用日志页） |
| `JoinServerOnLaunch` + address / `JoinWorldOnLaunch` | 自动进服务器/单人世界；新版本用 Quick Play，旧版用 `--server/--port` | **无** |
| `UseNativeGLFW/OpenAL/SDL` + 自定义路径 | Linux 系统库注入 `-Dorg.lwjgl.*.libname` | 无（低优先，平台特定） |
| `OnlineFixes` | 旧在线服务模拟 | 无（不相关） |
| 游戏时长统计 | 记录/显示游玩时间 | 无（产品功能） |

### 1.3 自定义命令与环境（每实例可覆盖）

| 设置 | 行为 | 我们的对应 |
|---|---|---|
| `WrapperCommand` | 在 Java 前加包装程序（gamemoderun、mangohud、prime-run…） | **无** |
| `PreLoadCommand` / `PreLaunchCommand` / `PostExitCommand` | 元数据加载前 / 启动前 / 退出后执行；提供 `$INST_NAME/ID/DIR/MC_DIR/JAVA/JAVA_ARGS` | **无** |
| `Env` / `OverrideEnv` | JSON 键值表，注入子进程环境 | **无**（只有优化器内部注入 `DRI_PRIME` 等） |

### 1.4 性能开关（Linux 为主）

| 设置 | 实现 | 我们的对应 |
|---|---|---|
| `EnableFeralGamemode` | 通过包装/环境调用 GameMode | 无（可用 wrapper 覆盖） |
| `EnableMangoHud` | `LD_PRELOAD` 注入 MangoHud | 无（可用 env 覆盖） |
| `UseDiscreteGpu` | PRIME/DRI 环境变量 | 有（优化器按 GPU 检测自动设） |
| `UseZink` | `MESA_LOADER_DRIVER_OVERRIDE=zink` 等 | 无（可用 env 覆盖） |

---

## 2. 我们现在的覆盖

- **全局**（`AppSettings`）：Java 路径/自动、内存 min/max、JVM args、GC 模式、game args、窗口宽高、fullscreen、游戏优化（JVM 调优 + dGPU 环境）。
- **每实例**（`InstanceOverrides`）：内存 min/max、JVM args、GC、game args、Java 路径、窗口宽高、fullscreen。
- **启动计划**（`LaunchPlanBuilder`/`LaunchProcessStarter`）：JVM/game args 用空格 `Split`（**不支持引号**）；环境只来自优化器；没有包装命令/自动加入/pre-post。

---

## 3. 差距与优先级

| 优先级 | 缺口 | 价值 | 跨平台 |
|---|---|---|---|
| P0 | JVM/game args 的引号解析（`-Dfoo="a b"`） | 修正确性，避免参数被拆坏 | 全平台 |
| P0 | `WrapperCommand`（全局 + 实例） | gamemoderun/mangohud/prime-run，无需为每个工具做开关 | 全平台（工具各自平台） |
| P0 | `Env` 键值表（全局 + 实例） | Zink/MangoHud/dGPU/自定义 JVM 环境；比硬编码开关通用 | 全平台 |
| P1 | 自动加入服务器/单人世界（每实例） | 常用启动体验；Prism 用 Quick Play/`--server` 两套 | 全平台 |
| P1 | 启动前/退出后命令 + `$INST_*` | 备份、通知、联动脚本 | 全平台（命令本身平台相关） |
| P2 | 最大化、启动后隐藏/退出、控制台开关 | 体验对齐 | 全平台 |
| P3 | 性能开关（GameMode/MangoHud/Zink） | 用 wrapper/env 预设即可，不必做专门字段 | Linux |
| P3 | 系统 GLFW/OpenAL/SDL | 少数 Linux 用户需要 | Linux |
| 不做 | PermGen、自动下载 Java、在线服务模拟 | 现代 JVM/产品策略不适用 | — |

---

## 4. 建议的 schema 与优先级

### 4.1 新增字段

```text
AppSettings:
  WrapperCommand: string?          // 例: "gamemoderun" / "mangohud --dlsym"
  Environment: map<string,string>  // 例: {"MESA_LOADER_DRIVER_OVERRIDE":"zink"}

InstanceOverrides:
  WrapperCommand: string?          // null = 跟随全局
  Environment: map<string,string>? // null = 跟随全局；{} = 显式清空
  JoinServerOnLaunch: bool?
  JoinServerAddress: string?       // "host" 或 "host:port"
  JoinWorldName: string?           // 单人世界目录名
```

- 环境合并顺序：**自动优化（dGPU）→ 全局 Env → 实例 Env**；后者覆盖前者。实例 `null` 跟随全局，`{}` 表示显式清空（与 Prism 的 override 语义一致）。
- 包装命令与 JVM/game args 共用同一个**引号感知 tokenizer**，不经过 shell；`ProcessStartInfo.ArgumentList` 逐个传参。
- 自动加入：版本 JSON 的 game arguments 含 `--quickPlayMultiplayer` 时用它，否则 `--server <host> --port <port>`；单人世界同理。
- 命令长度上限、地址解析（IPv4/IPv6/域名）、端口 1–65535；非法值在保存前拒绝。

### 4.2 安全边界

- Wrapper/pre/post 是**用户显式配置的本地程序**，等价于用户自己执行；不拼接未转义的 token，不经过 `sh -c`。
- pre/post 命令只拿到 `$INST_*` 环境变量；**不提供 access token / client id**。`$INST_JAVA_ARGS` 里的 `--accessToken`/`--clientId`/`--auth_xuid` 会被替换为 `<redacted>`，命令日志以 0600 写入并按启动日志轮转。
- pre-load/pre-launch 失败中止启动；post-exit 失败只写日志，不影响已退出的游戏。
- 日志里记录阶段与可执行文件，不记录完整命令参数；Env 值不记录（可能含密钥）。
- 实例覆盖只影响该实例；全局修改不影响已有实例的显式覆盖。

---

## 5. 实施顺序建议

1. **P0（一个提交）**：quote-aware tokenizer + `WrapperCommand` + `Environment`（全局/实例、JSON 持久化、启动计划、日志、测试）。
2. **P1**：自动加入服务器/单人世界（版本参数探测 Quick Play）。
3. **P1**：pre-launch/post-exit（`$INST_*`、超时、输出写日志）。
4. **P2/P3**：窗口/控制台行为、性能预设（用 wrapper/env 组合，不新增硬编码字段）。

---

## 6. 来源

- Prism `Application.cpp`（`WrapperCommand`/`PreLoadCommand`/`PreLaunchCommand`/`PostExitCommand`/`Env` 注册，`:733-807, :897`）。
- Prism `launcher/minecraft/MinecraftInstance.cpp`（`createEnvironment`、`processMinecraftArgs`、自动加入、pre/post 调用，`:690, :768-785, :1206-1263`）。
- Prism `launcher/BaseInstance.cpp`（命令 getter、`extraArguments` 即 JvmArgs，`:138-155, :491`）。
- Prism UI：`JavaSettingsWidget.ui`、`MinecraftSettingsWidget.ui`、`CustomCommands.ui`、`EnvironmentVariables.ui`。
- 本仓库：`AppSettings.cs`、`InstanceOverrides.cs`、`LaunchPlanBuilder.cs`、`LaunchOptimizationPlanner.cs`、`LaunchProcessStarter.cs`、`launch_panel.dart`、`instance_settings_dialog.dart`。
