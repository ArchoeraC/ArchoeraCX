# 02 · 架构设计（FFI 多语言核心）

> **归档文档**（原 `docs/02-architecture.md`）。早期架构稿，已被现行架构设计取代。
> 现行规范见 [`../10-architecture-design.md`](../10-architecture-design.md)；归档说明见 [`README.md`](README.md)。

> 定稿形态：**Flutter(UI) → C ABI → C++ 枢纽 → { C# 业务/API, Rust 下载 }**
> 详细 ABI 与实现约束见 [`polyglot-core-ffi.md`](polyglot-core-ffi.md)。

---

## 1. 决策记录（ADR）

| 编号 | 决策 | 结论 | 理由 |
|---|---|---|---|
| A1 | 分发模式 | **AGPL-3.0-only（不收外部贡献）** | 已确认；依赖以 MIT/Apache/BSD 为主 |
| A2 | UI | Flutter | 已确认；只做渲染，不含业务 |
| A3 | 对外边界 | **单一 C ABI（C++ 枢纽）** | 稳定、可跨编译器、可换 UI |
| A4 | 业务逻辑 | C#（NativeAOT 共享库） | 团队熟悉、生态好、注册表/文件/SQLite 方便 |
| A5 | 下载 | Rust（cdylib） | 并发、校验、内存安全 |
| A6 | API / HTTP | **C#（`HttpClient`）** | 唯一 API owner；系统代理/证书开箱即用；可 headless |
| A7 | 进程模型 | **单进程、多模块** | 延迟最低；代价是崩溃不隔离 |
| A8 | 配置/元数据 | SQLite，仅 C# 有写权限 | 避免多语言并发写 |
| A9 | 图片 | Flutter 直接加载（例外） | 避免图片流量穿过 ABI；失败可回退到本地缓存 |

> 与上一版文档的差异：不再使用 stdio JSON-RPC 作为主通道；它只保留为 **开发期 CLI / 故障诊断** 的备用入口。

---

## 2. 总体形态

```
┌──────────────────────────────────────────────────────────────┐
│ Flutter UI (Dart)                                            │
│  pages · widgets · theme · CoreClient(ffi)                   │
└──────────────────────────┬───────────────────────────────────┘
                           │ C ABI（唯一入口，见 07）
┌──────────────────────────▼───────────────────────────────────┐
│ archoera_core (C++20)                                        │
│  Abi · Dispatcher · TaskRegistry · EventPump · ModuleHost    │
└───────┬──────────────────────────────────────┬───────────────┘
        │                                      │
┌───────▼────────────────────────┐   ┌─────────▼───────────────┐
│ archoera_cs                    │   │ archoera_dl             │
│ C# NativeAOT                   │   │ Rust                    │
│ 实例/Java/账户/API/下载计划     │   │ 下载/校验/分片           │
└───────┬────────────────────────┘   └─────────┬───────────────┘
        │                                      │
        ▼                                      ▼
   SQLite / 文件系统                     HTTP（文件）/ 磁盘
        │
        └── API 请求（Mojang / 微软 / Modrinth / CurseForge）
```

### 2.1 为什么 C++ 做枢纽

| 理由 | 说明 |
|---|---|
| ABI 稳定 | C ABI 跨编译器/跨语言稳定；C++ 类、STL、异常都不能跨 DLL |
| 加载控制 | 由 C++ 决定模块初始化顺序、版本校验、失败降级 |
| 事件汇聚 | 所有模块的事件统一排队，Dart 只拉一次 |
| 可替换 | Rust/C# 任一模块可替换，不影响 UI 契约 |
| 可裁剪 | MVP 可以只带 C#，后续再加 Rust/C |

### 2.2 代价（必须接受）

- **崩溃不隔离**：任何模块 segfault/abort，整个启动器退出。→ 必须有崩溃处理器 + minidump。
- **调试复杂**：跨 4 种运行时，栈回溯需要符号与工具链配合。
- **构建复杂**：4 套工具链、3 个平台。
- **体积**：AOT + Rust + C++ 叠加，安装包显著变大。

---

## 3. 职责划分与 HTTP 归属

| 关注点 | Owner | 备注 |
|---|---|---|
| UI / 交互 / 动画 | Flutter | 不直接读文件、不直连业务 API（图片除外） |
| 生命周期 / 路由 / 任务 / 事件 | C++ | 不含业务规则 |
| 实例 / Java / 版本元数据 / 账户 / Mod 扫描 | C# | 唯一写 SQLite 的模块 |
| **API（搜索/详情/清单/认证）** | **C#** | 唯一 API HTTP owner；统一缓存、限流、代理、UA |
| 下载 / 校验 / 断点 / 去重 | Rust | 只处理"字节 → 文件" |
| 图片 | Flutter | 例外：走 Flutter 图片缓存，不经 ABI |

### 3.1 为什么 API 全归 C#

| 理由 | 说明 |
|---|---|
| 下载计划依赖核心逻辑 | 计划由 rules/继承/索引展开产生，只有 C# 有这些数据 |
| 代理/证书一致 | `HttpClient` 默认走系统代理与系统信任库，避免"Dart 能搜、核心装不上" |
| 缓存与限流集中 | 一个 token bucket、一份 ETag 缓存，跨页面复用 |
| 无 token 扩散 | 搜索虽不需要 token，但详情/更新检查会关联账户；不必把边界切在中间 |
| headless 可用 | CLI/CI 不启动 Flutter 也能跑完整流程 |
| 一套错误语义 | 网络错误码、重试策略、离线降级只有一处实现 |

### 3.2 下载计划 ≠ API 接入（保留的决策依据）

一次安装的链路是：

```
API 拉取原始 JSON
  → 版本继承合并（inheritsFrom）
  → rules 求值（os/arch/features）
  → 加载器 profile 合并
  → 资源索引展开（assets objects）
  → 去重/已有文件检查
  → DownloadPlan（URL + hash + 目标路径）
  → Rust 执行
```

只有第 1 步是"API 接入"，第 2–6 步是核心逻辑。所以计划的生产者必须是拥有这些逻辑的模块（C#），否则要么多一跳，要么把核心塞进 UI。

### 3.3 被否决的方案（存档）

| 方案 | 内容 | 否决原因 |
|---|---|---|
| A | C（libcurl）做唯一传输层 | 5 语言；C# 异步流式要跨 ABI；收益不足以抵消复杂度 |
| C | Rust 管所有 HTTP | C# 认证/JSON 隔着 ABI 调 Rust，开发体验差 |
| D | Dart 做 API 网关 | 计划生成依赖核心逻辑；UI 变厚；headless 依赖 Dart |

### 3.4 Dart 仍然会做的 HTTP：图片

这是唯一的例外，必须正视：

- Flutter 的 `Image.network` 有自己的 HTTP 栈（Dart），不经过 C#。
- **不要**为了"绝对统一"把图片字节穿过 ABI——内存与延迟都不划算。
- 折中：C# 在后台把常用图标缓存到本地，UI 优先读缓存、失败再走网络。
- 若用户配置了需要认证的代理，图片可能加载失败：UI 需要占位图 + 重试，不能白屏。

### 3.5 搜索的边界（详见 `06-ecosystem-apis.md` §5.1）

| 能力 | 归属 | 说明 |
|---|---|---|
| 远程搜索/详情/版本列表 | C# | 统一分页、facet、限流、缓存 |
| 本地搜索（实例/Mod/文件） | C# | SQLite FTS5 或内存索引 |
| 搜索框输入、防抖、取消 | Dart | UI 只做 300ms 防抖 + taskId 取消 |
| 结果渲染、图标、骨架屏 | Dart | 只消费 DTO |

> Kotlin/JS 生态里常见的"前端直连 API"在这里不适用：启动器的 API 与安装强耦合，拆开只会制造两套行为。

---

## 4. 模块边界

### 4.1 C++ 枢纽（`archoera_core`）

| 组件 | 职责 | 禁止 |
|---|---|---|
| `Abi` | 参数校验、UTF-8 处理、错误封装 | 业务判断 |
| `Dispatcher` | `method → handler` 路由 | 直接读数据库 |
| `TaskRegistry` | taskId、状态机、取消、超时 | 阻塞等待 |
| `EventPump` | MPSC 队列、批量投递、合并进度 | 直接调 Dart |
| `ModuleHost` | 加载/卸载模块、版本握手、能力查询 | 假设模块一定存在 |
| `Log` | 统一日志出口、脱敏、轮转 | 记录 token |

### 4.2 C# 模块（`archoera_cs`）

- 实例 CRUD、游戏目录、隔离策略
- Java 探测：`JAVA_HOME`、PATH、注册表、常见路径、版本解析
- 版本元数据缓存与合并（`inheritsFrom`、rules）
- 账户：微软 device code、refresh、离线、authlib-injector
- **API 客户端**：Mojang / 微软 / Modrinth / CurseForge（统一 UA、代理、限流、重试）
- **搜索与浏览**：分页、facet、single-flight、TTL/ETag 缓存、离线快照
- **下载计划生成**：版本继承 + rules + 索引展开 → `DownloadPlan`
- Mod/资源包/光影扫描与元数据解析
- SQLite 唯一写入方

> 业务逻辑放**纯类库**，AOT 导出层只做适配（见 07 §5）。

### 4.3 Rust 模块（`archoera_dl`）

- 消费 `DownloadPlan`：分片下载、并发、限速、重试、镜像回退
- hash 校验（sha1/sha256/sha512）
- 原子落盘、断点续传、去重
- 进度聚合上报

### 4.4 已取消：C 网络层（`archoera_net`）

原计划用 C + libcurl 做统一传输层。因为 API 已归 C#、且 C 会引入第 5 门语言，**本阶段取消**。
若未来需要 HTTP/2 多路复用或统一 TLS 行为，再按 §3.3 方案 A 替换 C# 的 HTTP 实现，ABI 与上层契约不变。

---

## 5. 通信契约

- 控制面：`archoera_call(method, params_json) → task_id`
- 数据面：固定结构体事件（进度/日志/状态）
- 事件：`wake()` → Dart `poll(batch)` → 合并 → 刷新 UI
- 版本：`abi_version` + 每模块 `module_version`，不匹配拒绝启动

完整头文件见 `polyglot-core-ffi.md` §3。

**开发期备用通道**：核心同时提供一个 CLI（`archoera-cli --rpc stdio`），用 JSON-RPC 跑同一套 method。
用途：CI、排障、无 UI 复现。**发布版不暴露**。

---

## 6. 线程模型

| 线程 | 归属 | 职责 |
|---|---|---|
| UI isolate | Flutter | 渲染、`poll`、状态合并 |
| Hub | C++ | 路由、任务状态、事件队列 |
| Worker pool | C++/各模块 | 下载、扫描、安装 |
| tokio runtime | Rust | 异步 IO |
| .NET 线程池 | C# | 业务异步 |
| JVM | 子进程 | 游戏 |

规则：

1. 模块之间**不直接调用**，一律经 C++ 路由。
2. 模块只向 hub 投事件，不直接回调 Dart。
3. 所有跨线程数据是不可变消息（或明确移交所有权）。
4. 取消是协作式的，每个长任务都要检查 token。

---

## 7. 生命周期

```
App 启动
  └─ Flutter 加载 archoera_core（绝对路径）
      └─ archoera_init(config)
          ├─ 初始化日志/崩溃处理器
          ├─ 加载 archoera_cs → 版本握手 → 预热
          ├─ 加载 archoera_dl（可选，失败则回退 C# 下载）
          └─ 打开 SQLite（C#），执行迁移
              └─ UI 进入主界面（可交互）
退出
  └─ shutdown → 停止接受新任务 → 取消/等待 → 关闭模块 → 退出
```

**加载失败策略**：非关键模块（下载器）失败 → 降级为 C# 实现并提示；关键模块（C# 业务）失败 → 诊断页。

**热重启**：Flutter 热重启不卸载原生库，必须实现 `attach/detach` 与任务快照重放（见 07 §4.4）。

---

## 8. 数据目录

| 用途 | Windows | Linux | macOS |
|---|---|---|---|
| 配置 | `%APPDATA%\ArchoeraCX` | `~/.config/archoeracx` | `~/Library/Application Support/ArchoeraCX` |
| 缓存/日志/崩溃 | `%LOCALAPPDATA%\ArchoeraCX` | `~/.cache/archoeracx` | `~/Library/Caches/ArchoeraCX` |
| 游戏数据 | 用户可选，默认 `%APPDATA%\.archoera` | `~/.local/share/archoera` | `~/Library/Application Support/archoera` |

```
<root>/
├─ instances/<id>/         # instance.json + .minecraft/
├─ shared/{libraries,assets,versions}/
├─ meta/                   # 版本清单与 JSON 缓存
├─ logs/                   # 轮转日志
├─ crashes/                # minidump
└─ archoera.db             # SQLite（仅 C# 写）
```

默认不写 `.minecraft`，提供导入。

---

## 9. 安全

| 面 | 措施 |
|---|---|
| Token | OS 凭据库；只在 C# 内可见；不跨 ABI 传到 UI |
| 日志 | 统一脱敏（Bearer、token、邮箱、UUID、路径、IP） |
| 下载 | HTTPS + hash 校验 + 镜像白名单 |
| 解压 | Zip Slip 规范化校验（每个模块各自负责，不能只靠上层） |
| 更新 | 签名校验（Authenticode / notarization），不只校验服务端 hash |
| ABI | 入参长度/空指针校验；不信任任何跨边界数据 |
| 子进程 | 参数数组、无 shell、显式工作目录 |
| 第三方登录 | authlib-injector 独立 agent，不修改游戏 jar |

---

## 10. 平台集成

| 能力 | 实现位置 | 说明 |
|---|---|---|
| 无边框/模糊/窗口 | Flutter 插件 + C#/C++ 平台调用 | 平台差异集中在 C++ |
| 托盘/单实例/文件关联 | C++ | 不依赖 Dart 插件 |
| 深链/开机自启 | C++ | 注册表/.desktop/LaunchAgent |
| 自动更新 | 独立 updater | 替换时不能占用自身 |
| 崩溃上报 | C++ Crashpad | 默认本地保存，上传需 opt-in |

---

## 11. 性能目标

| 指标 | 目标 |
|---|---|
| 冷启动到可交互 | < 1.5s |
| 核心 init（含模块加载） | < 400ms |
| FFI 往返（空调用） | < 50µs |
| 事件吞吐 | 10k/s 不丢，UI 合并后 60fps |
| 下载 | 跑满带宽，并发 16–32 |
| 空闲内存 | UI+核心 < 400MB（4 运行时） |
| 安装包 | < 120MB |

---

## 12. 测试策略

| 层 | 方式 |
|---|---|
| C# 业务 | 纯类库单测（JIT 下跑），覆盖 rules/Java 解析/路径 |
| Rust 下载 | 本地假 HTTP 服务器 + 断点/校验/取消用例 |
| C ABI | C harness + 黄金事件序列 + 边界 fuzz |
| 跨语言 | CI 三平台跑"扫描 Java → 安装 → 假 JVM 启动" |
| 崩溃 | 注入 abort，验证 minidump 与 UI 提示 |
| UI | widget test + 关键页面截图回归 |

---

## 13. 与旧版（JSON-RPC 分进程）的对比

| 维度 | 分进程 RPC | 单进程 FFI |
|---|---|---|
| 延迟 | 中 | 低 |
| 崩溃隔离 | ✅ | ❌ |
| 调试 | 简单（两边独立） | 复杂（跨运行时） |
| 打包 | 简单 | 复杂（多产物签名） |
| 换 UI | 容易 | 容易（C ABI 不变） |
| 性能上限 | 受序列化限制 | 高 |

> 选择单进程 FFI 的核心前提是：**你能接受"一个模块崩了，整个启动器没了"**，并且愿意投入崩溃处理与跨语言调试的基础设施。
> 如果这两点做不到，分进程仍然是更稳的工程选择。
