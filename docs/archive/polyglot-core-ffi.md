# 07 · 多语言核心与 FFI 契约

> **归档文档**（原 `docs/07-polyglot-core.md`）。FFI 契约早期稿，现行 ABI 与模块协议已重写。
> 现行规范见 [`../10-architecture-design.md`](../10-architecture-design.md) §6–§7；认证见 [`../authentication.md`](../authentication.md)；归档说明见 [`README.md`](README.md)。

> 目标架构（已按你的方向定稿）：
> **Flutter 只做 UI，C++ 做唯一对外枢纽，C# 管实例/Java/业务，Rust 管下载，C 管网络传输。**
> 本文回答三件事：**怎么连、谁拥有什么、哪里一定会出事。**

---

## 0. 先回答："Flutter 能直接原生链接 C# 吗？"

**能，但不是"Flutter ↔ C#"直连，而是：**

```
Dart (dart:ffi)
  └─ C ABI（纯 C 函数 / 指针 / 整数）
      └─ NativeAOT 编译出的 .dll/.so/.dylib
          └─ C# 代码
```

技术要点（已核实）：

| 项 | 结论 |
|---|---|
| .NET 侧 | `PublishAot=true` + `NativeLib=Shared`，导出 `[UnmanagedCallersOnly(EntryPoint="x")]` 的 **static** 方法 |
| 参数限制 | 不能有 `string`、引用类型、泛型；只能指针/整数/结构体指针；返回值走 out 参数 |
| 导出范围 | **只有被发布程序集本身**里标记的方法会导出（引用的类库不会） |
| 调试 | NativeAOT **没有托管调试器**，只能原生调试 + 日志 |
| 大小 | 一个最小 AOT 共享库约 10–30 MB |
| 调用开销 | 原生调用本身是纳秒级，真正的成本在序列化与线程切换 |

**但建议不要让 Dart 直接 `dlopen` C# 库。** 原因：

1. 你已经有 4 种语言，如果 Flutter 同时连 C++ 和 C#，边界会从 1 个变成 N 个，版本/加载/崩溃责任都分散。
2. C# 的 AOT 库一旦加载，进程内就是"又一个运行时"；统一由 C++ 加载，才能控制初始化顺序与生命周期。
3. 未来换 UI（或加 CLI）时，只需替换最外层。

> 结论：**Dart 只认识一个库：`archoera_core`（C++）。** C++ 再加载 C#/Rust/C 模块。

---

## 1. 组件与职责（唯一 owner 原则）

| 关注点 | 唯一 owner | 语言 | 说明 |
|---|---|---|---|
| 渲染、交互、动画、主题 | `archoera_ui` | Dart / Flutter | 不碰文件、网络、进程 |
| 对外 ABI、生命周期、任务编排、事件泵 | `archoera_core` | C++ | **唯一被 Dart 加载的库**，不含业务逻辑 |
| 实例管理、Java 探测、版本元数据、账户、Mod 扫描 | `archoera_cs` | C# (NativeAOT) | 注册表、文件系统、SQLite |
| **API（搜索/详情/清单/认证）** | `archoera_cs` | C# (NativeAOT) | 唯一 API owner；缓存、限流、代理 |
| 下载调度、分片、校验、断点、去重 | `archoera_dl` | Rust | tokio + 分片 + hash |
| 图片 | Flutter | Dart | 唯一例外，走图片缓存 |
| 持久化 | `archoera_cs` | C# + SQLite | 只有它有写库权 |

### 关键冲突：HTTP 到底谁负责？（已决策）

原方案里 Rust 下载器和 C 网络栈都会做 HTTP。如果不指定唯一 owner，会出现：

- 两套 TLS/证书/代理/超时/重试逻辑，行为不一致；
- 同一个站点在"下载"和"API"两条路径上表现不同；
- 排障时要同时看两套日志。

三种可行划分（加上 Dart 方案共四种，完整对比见 `02-architecture.md` §3.1）：

**方案 A（严格分层）**

```
C (libcurl)      = 传输层：连接、TLS、代理、HTTP/2、流式读取
Rust             = 调度层：分片、并发、重试策略、hash 校验、落盘
C#               = 语义层：Mojang/微软/Modrinth/CurseForge 的 API 与数据模型
```

- 所有出网都经过 `archoera_net`，Rust 与 C# 都通过 C ABI 调它。
- 优点：一套 TLS 行为；缺点：C# 调 HTTP 要跨一层 ABI，异步流式要设计好。

**方案 B（务实简化，MVP 推荐）**

```
C#  = 所有 API（HttpClient，成熟）
Rust = 仅批量文件下载（reqwest）
删除 C 网络栈
```

- 优点：少一门语言、少一层 FFI、C# 的 HTTP 生态最省心。
- 缺点：两套 HTTP 栈（但用途清晰：小请求 vs 大文件）。

**方案 C（单栈，最少争议）**

```
Rust = 所有 HTTP（API + 下载），C# 不直接出网
```

- 优点：一套栈；缺点：C# 的认证/JSON 逻辑要隔着 ABI 调 Rust，写起来别扭。

**方案 D（Dart API 网关）**

```
Dart = API 接入 + 生成 DownloadPlan
Rust = 消费计划并下载
C    = 删除
```

- 优点：API 用 Dart 写最顺手；C# 可退化为实例/Java 工具；UI 直接持有模型。
- 缺点：**下载计划不是 API 响应**——rules/继承/资源索引展开都是核心逻辑；放进 Dart 等于把 UI 层变成核心层，且 headless 依赖 Dart 运行时。

> **已定：方案 B。** API 全部归 C#；Rust 只做文件下载；C 网络层取消。
> 若未来确有传输层需求（例如需要 HTTP/2 多路复用、统一 TLS），再按方案 A 替换 C# 的 HTTP，不影响 ABI 与上层契约。

---

## 2. 分层图

```
┌────────────────────────────────────────────────────────────┐
│ Flutter (Dart)                                             │
│   CoreClient ── dart:ffi ── NativeCallable.listener        │
└───────────────────────────┬────────────────────────────────┘
                            │ 只加载 1 个库：libarchoera_core
┌───────────────────────────▼────────────────────────────────┐
│ archoera_core (C++20)                                      │
│  ├─ Abi          : 参数校验、版本握手、错误封装              │
│  ├─ Dispatcher   : method → 模块路由                        │
│  ├─ TaskRegistry : taskId、状态机、取消                     │
│  ├─ EventPump    : MPSC 队列 → 批量投递 Dart                │
│  └─ ModuleHost   : dlopen/LoadLibrary + 函数表              │
└───────┬───────────────────────────────┬────────────────────┘
        │ C ABI                         │ C ABI
┌───────▼──────────────────────┐ ┌──────▼─────────────────────┐
│ archoera_cs                  │ │ archoera_dl                │
│ C# NativeAOT                 │ │ Rust cdylib                │
│ 实例/Java/元数据/API/计划     │ │ 下载/校验/分片              │
└──────────────────────────────┘ └────────────────────────────┘
```

> C++ 层**不要**写业务逻辑。它的价值是：稳定 ABI、生命周期、线程/事件、错误边界。
> 一旦 C++ 里出现"Mojang 版本解析"，就会变成第五套业务实现。

---

## 2.5 下载计划契约（DownloadPlan）

无论最终由谁生成，**Rust 只消费这一份数据**，不理解 Mojang/Modrinth/CurseForge 语义。

```json
{
  "planId": "8f2c...",
  "root": "C:/Users/x/AppData/Roaming/.archoera/instances/abc",
  "concurrency": 16,
  "totalBytes": 123456789,
  "files": [
    {
      "url": "https://piston-data.mojang.com/.../client.jar",
      "mirrors": ["https://bmclapi2.bangbang93.com/..."],
      "sha1": "0d1c...",
      "size": 25165824,
      "dest": "versions/1.21.4/1.21.4.jar",
      "mode": "create",
      "priority": 0
    }
  ],
  "post": [
    { "type": "extract_natives", "src": "libraries/...", "dest": "natives" }
  ]
}
```

**规则**

| 规则 | 说明 |
|---|---|
| `dest` | 必须是相对路径，规范化后仍在 `root` 内；拒绝绝对路径、`..`、符号链接逃逸 |
| 协议 | 仅 https（测试允许 http://127.0.0.1） |
| 校验 | hash 必填；不匹配时换 mirror 重试，全部失败才算该文件失败 |
| 语义 | Rust 不知道"这是 Mod 还是库"，只负责字节 → 文件 |
| 幂等 | 同一 `planId` 重复提交应复用结果；续传按 `(dest, sha1)` |
| 不可变 | 计划是只读输入；Rust 只回传事件与结果，不回写计划 |
| 归属 | 生产者负责正确性（路径、hash、依赖顺序）；消费者负责执行与校验 |

**结果**

```json
{ "planId": "8f2c...", "ok": 1180, "failed": [{"dest":"...","code":"HASH_MISMATCH"}], "bytes": 123456789, "elapsedMs": 43210 }
```

**生产者建议**

| 生产者 | 评价 |
|---|---|
| C#（推荐） | 元数据/rules/加载器合并都在 C#，天然能生成计划；headless 可用 |
| Dart | 只有在"Dart 已成为核心层"时成立；否则会把 rules/继承/索引逻辑塞进 UI |
| C++ | 不推荐，会把业务规则吸进枢纽 |

> 若 Dart 只负责拉取原始 JSON，仍应由 C# 生成计划——否则计划会缺少 rules 求值与资源索引展开。

---

## 3. C ABI 设计

### 3.1 原则

1. **只暴露 C**：不导出 C++ 类、STL 容器、异常、`std::string`。
2. **不透明句柄**：`typedef struct archoera_core archoera_core;`
3. **调用方分配、提供长度**：小字符串用"查询长度 → 填充"两段式。
4. **谁分配谁释放**：跨模块字符串必须由分配方提供 `*_free`。
5. **错误不抛异常**：返回 `int32` 状态码 + 错误结构。
6. **版本可查询**：`archoera_abi_version()` + 每个模块自己的 `module_version()`。
7. **错误缓冲按线程隔离**：每个线程一份，避免并发调用互相覆盖。

### 3.2 头文件草案（`archoera_abi.h`）

```c
#pragma once
#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

#define ARCHOERA_ABI_VERSION 1u

typedef struct archoera_core archoera_core;

typedef enum {
  ARCHOERA_OK            = 0,
  ARCHOERA_E_INVALID     = 1,   /* 参数/状态非法 */
  ARCHOERA_E_NOT_FOUND   = 2,
  ARCHOERA_E_AUTH        = 3,
  ARCHOERA_E_NETWORK     = 4,
  ARCHOERA_E_DISK        = 5,
  ARCHOERA_E_CANCELLED   = 6,
  ARCHOERA_E_UNSUPPORTED = 7,
  ARCHOERA_E_INTERNAL    = 8
} archoera_status;

typedef struct {
  int32_t     code;        /* archoera_status */
  const char* message;     /* UTF-8，属于 core，直到下一次调用 */
  const char* detail;      /* 可为 NULL */
} archoera_error;

typedef enum {
  ARCHOERA_EV_TASK_PROGRESS = 1,
  ARCHOERA_EV_TASK_STATE    = 2,
  ARCHOERA_EV_LOG           = 3,
  ARCHOERA_EV_ACCOUNT       = 4,
  ARCHOERA_EV_INSTANCE      = 5
} archoera_event_type;

typedef struct {
  int32_t  type;
  uint64_t task_id;
  int32_t  code;           /* 子状态，含义随 type */
  int64_t  done;
  int64_t  total;
  int64_t  bytes_per_sec;
  const char* json;        /* 事件负载（UTF-8），NULL 表示无 */
} archoera_event;

/* 生命周期 */
uint32_t archoera_abi_version(void);
archoera_status archoera_init(const char* config_json, archoera_core** out, archoera_error* err);
void            archoera_shutdown(archoera_core* core);

/* 事件：wake 由任意线程调用；poll 只在调用方线程 */
typedef void (*archoera_wake_fn)(void* user);
archoera_status archoera_set_wakeup(archoera_core* core, archoera_wake_fn fn, void* user);
int32_t         archoera_poll(archoera_core* core, archoera_event* out, int32_t max);

/* 调用：method + JSON，返回 task_id（0 表示同步完成） */
archoera_status archoera_call(archoera_core* core,
                              const char* method,
                              const char* params_json,
                              uint64_t* out_task_id,
                              archoera_error* err);

archoera_status archoera_cancel(archoera_core* core, uint64_t task_id);

/* 结果读取：两段式（buf=NULL 查长度；buf 不足返回 E_INVALID 并回填 out_len） */
archoera_status archoera_result(archoera_core* core, uint64_t task_id,
                                char* buf, size_t buf_len, size_t* out_len,
                                archoera_error* err);

/* 任务结束后由 UI 显式释放结果缓存 */
void archoera_release(archoera_core* core, uint64_t task_id);

/* 模块信息（诊断页用） */
archoera_status archoera_module_info(archoera_core* core, int32_t index,
                                     char* buf, size_t buf_len, archoera_error* err);

#ifdef __cplusplus
}
#endif
```

### 3.3 为什么"method + JSON"而不是每个功能一个函数

| | method + JSON | 每功能一个 C 函数 |
|---|---|---|
| 迭代速度 | 快（加方法不用改 ABI） | 慢（改头文件 + 重编译全部） |
| 类型安全 | 弱（靠 schema） | 强 |
| 性能 | 有序列化成本 | 最低 |
| 版本兼容 | 容易（未知方法返回 `E_UNSUPPORTED`） | 困难 |

**混合策略（推荐）**：

- **控制面**（低频：创建实例、登录、安装）用 `method + JSON`。
- **数据面**（高频：进度、日志、文件列表）用**固定结构体**，避免每 100ms 序列化一次大 JSON。
- 二进制数据（图标、皮肤预览图）只传路径或共享内存句柄，**不要**走 stdio/JSON。

### 3.4 JSON schema 放哪

- 契约文件：`contracts/*.schema.json`（或 `.proto` 仅用于生成文档）。
- 每个 method 一个 schema，CI 校验：
  - UI 发出的参数符合 schema；
  - 核心返回的事件符合 schema；
  - 旧 schema 不被破坏（兼容性测试）。

---

## 4. Dart 侧细节（最容易踩坑）

### 4.1 加载

```dart
final lib = DynamicLibrary.open(_resolve('archoera_core'));
```

- **不要**依赖 `PATH`/系统库搜索路径；用 `Platform.resolvedExecutable` 的目录拼绝对路径。
- Windows 用应用目录，防止 DLL 劫持。
- macOS 需正确 `@rpath`，且所有嵌套 dylib 都要签名。

### 4.2 线程

- Dart FFI 的**同步调用发生在调用它的 isolate 线程**；在 UI isolate 里调用阻塞函数 = 界面卡死。
- 所有可能阻塞的调用（安装、扫描、启动）必须是 **"投递任务 + 事件回调"** 模式，ABI 函数本身立即返回 `task_id`。
- 原生线程回调 Dart 只能用 `NativeCallable.listener`（Dart 3.1+，可从任意线程调用）或 `isolateGroupBound`。
- **必须保存 `NativeCallable` 引用**，否则会被 GC 回收导致悬空函数指针；退出时 `close()`。

```dart
late final NativeCallable<Void Function()> _wake;
_wake = NativeCallable<Void Function()>.listener(_onWake);
_native.setWakeup(_wake.nativeFunction, nullptr);
// 任意线程 → listener → UI isolate 的 microtask
void _onWake() => scheduleMicrotask(_drainEvents);
```

### 4.3 事件泵

```
原生线程 → wake() → Dart microtask → poll() 批量取事件（如一次 64 条）
        → 合并进度（同一 task 只保留最新）→ 更新状态 → 每帧最多一次 setState
```

- 进度事件在 C++ 侧就按 **100–200ms** 合并，别让 Dart 处理每秒几千次回调。
- UI 侧再做一层节流，保证 60fps。

### 4.4 生命周期与热重启

- Flutter 热重启不会卸载原生库；必须支持 `attach/detach`：
  - `detach`：停止向旧 UI 投递事件，但任务继续跑。
  - `attach`：重放当前任务快照。
- 应用退出：`shutdown` → 等待 2s → 强杀；确保下载线程、tokio、.NET GC 都能收尾。

---

## 5. C# 模块（NativeAOT）

### 5.1 结构

```
archoera_cs/
├─ Archoera.Core/            # 纯类库：业务逻辑，可单测，可 JIT 调试
├─ Archoera.Exports/         # 只放 [UnmanagedCallersOnly] 适配层
└─ Archoera.DevHost/         # 开发期用普通 .NET 控制台跑同一套逻辑
```

**关键**：业务逻辑放在纯类库里，导出层只做三件事——解参数、调业务、装结果。
这样即使 AOT 无法托管调试，90% 的 bug 仍可在单测/JIT 下定位。

### 5.2 导出规则

```csharp
[UnmanagedCallersOnly(EntryPoint = "archoera_cs_scan_java", CallConvs = new[]{ typeof(CallConvCdecl) })]
public static int ScanJava(IntPtr requestUtf8, IntPtr outJson, int outLen) { ... }
```

- 只能 `static`，不能泛型，不能引用类型参数。
- 所有异常必须在边界 `catch`，转成错误码（AOT 下未捕获异常 = 进程终止）。
- 字符串：UTF-8 指针 + 长度；不要 `Marshal.PtrToStringAuto`。
- Windows 导出注意调用约定（`CallConvStdcall` vs `Cdecl`），保持与头文件一致。
- 只导出**发布程序集自身**的方法——适配层必须是最终发布项目。

### 5.3 AOT 限制清单

| 限制 | 影响 | 对策 |
|---|---|---|
| 无动态程序集加载 | 插件化困难 | 不做运行时插件 |
| 反射受限 | 序列化/DI 需配置 | `System.Text.Json` 源生成 |
| 无托管调试器 | 排障慢 | 纯类库 + 单测 + 日志 + DevHost |
| 体积 | +10–30MB | 裁剪、`InvariantGlobalization` 评估 |
| 启动 | 首次调用初始化 | 在 `init` 阶段预热 |

### 5.4 依赖

- SQLite：`Microsoft.Data.Sqlite`（注意 native 库随包分发与 AOT 兼容）。
- 注册表：`Microsoft.Win32.Registry`（Windows）。
- JSON：`System.Text.Json` 源生成。
- HTTP：若走 C#，用 `HttpClient` + `SocketsHttpHandler`（连接池、代理、HTTP/2 都成熟）。

---

## 6. Rust 模块（下载器）

### 6.1 形态

- `crate-type = ["cdylib"]`，C ABI 导出。
- `#[unsafe(no_mangle)] pub unsafe extern "C" fn ...`（Rust 2024 edition 语法）。
- 用 `cbindgen` 生成头文件，但**以手写稳定头文件为准**（生成物容易随版本漂移）。

### 6.2 边界规则

| 规则 | 原因 |
|---|---|
| `catch_unwind` 包住每个导出函数 | `extern "C"` 中 panic 展开是 UB（新版直接 abort） |
| 不把 `&str`/`String`/`Vec` 跨边界 | ABI 不稳定；用指针 + 长度 |
| 句柄用 `Box::into_raw` / `from_raw` | 明确所有权 |
| 回调只投递到 C++ 队列 | 不要直接回调 Dart |
| tokio runtime 只建一次 | 避免每任务一个 runtime |
| 任务取消用 `CancellationToken` | 与 C++ 的取消语义对齐 |

### 6.3 下载器要点

- 分片（Range）+ 并发 + 全局限速 + 域名级限流。
- 校验：sha1/sha256/sha512；失败自动换源。
- 原子落盘：`.part` → fsync → rename。
- 断点续传：记录 ETag/Last-Modified + 已完成区间。
- 去重：以 hash 为键；硬链接失败降级复制。
- 进度：聚合后按 100–200ms 上报，不在每个 chunk 回调。

---

## 7. C 网络层（已取消，存档）

> API 已归 C#，C 网络层不再进入构建。以下内容仅在你未来决定"统一传输层"时参考。

**底线：用 libcurl，不要手写 TCP/TLS/HTTP。**

| 选择 | 许可证 | 说明 |
|---|---|---|
| libcurl | MIT-like | HTTP/1.1、HTTP/2、代理、重定向、证书校验齐全 |
| OpenSSL / Schannel / SecureTransport | Apache-2.0 / 系统 | 由 libcurl 后端选择，跟随系统证书库最佳 |
| c-ares | MIT | 异步 DNS |
| yyjson / jansson | MIT | JSON（但建议 JSON 语义放 C#/Rust） |

必须实现的语义：

- 证书与主机名校验**默认开启**，不提供"关闭校验"的正式开关。
- 超时：连接/总时长/低速；取消用 `curl_multi_wakeup` + progress callback。
- 代理：`CURLOPT_PROXY` + `NO_PROXY`；PAC 需额外处理。
- 重试：指数退避 + `Retry-After`。
- 流式：写回调直接喂给 Rust 的分片器，避免整包进内存。
- 线程：`curl_global_init` 只一次；每个线程一个 `CURLM` 或统一事件循环。

**风险**：C 层解析任何不可信数据（HTTP 头、JSON、压缩包）都要 fuzz；一次越界就是整个启动器崩溃。

---

## 8. 线程与事件模型

```
UI isolate (Dart)                 C++ hub thread              Workers
     │  call(method)  ───────────▶ │  enqueue task
     │                              │  dispatch ──────────────▶ C# / Rust / C
     │                              │                            │
     │                              │  ◀── progress/log ─────────┘
     │  ◀── wake() ──────────────── │  event queue (MPSC)
     │  poll(batch) ───────────────▶│
```

规则：

1. **Dart 只在 UI isolate 调 `poll`**。
2. C++ 的 `call` 可以来自任意线程，但内部统一入队到 hub 线程。
3. 每个 worker 只向 hub 投事件，**不能**直接调 Dart、不能直接改共享状态。
4. 取消是协作式的：`task_id` → `CancellationToken` → 各模块自行检查。
5. 任何跨线程共享数据用不可变消息，不用共享内存（除非未来做零拷贝）。

---

## 9. 内存所有权矩阵

| 数据 | 分配方 | 释放方 | 传递方式 |
|---|---|---|---|
| 配置/参数 JSON | Dart | Dart（`call` 内同步消费） | UTF-8 指针 + 长度 |
| 事件 JSON | C++ | C++（`poll` 后下一次调用前有效） | 指针，仅在本次 poll 有效 |
| 大结果（文件列表） | 模块 → C++ | C++ | 句柄 + 分页读取 |
| 错误信息 | C++ | C++ | 指针，直到下一次调用 |
| 二进制（图标） | 模块 | 模块 | 文件路径或共享内存 |

**铁律**：谁分配谁释放；跨模块传字符串必须带长度；不允许"我 new 你 delete"。

---

## 10. 构建与打包

| 平台 | 产物 | 注意 |
|---|---|---|
| Windows | `archoera_core.dll` + `archoera_cs.dll` + `archoera_dl.dll` | 统一 `/MT` 或 `/MD`；导出用 `.def`；`LoadLibraryEx` 安全搜索 |
| Linux | `libarchoera_core.so` 等 | `-fvisibility=hidden` + version script 只导出 `archoera_*`；`$ORIGIN` RPATH |
| macOS | `libarchoera_core.dylib` 等 | universal（arm64+x86_64）；`@rpath`；逐个签名后再签 App |

构建编排：

```
CMake (top-level)
├─ add_subdirectory(core)         # C++
├─ ExternalProject: cargo build   # Rust
├─ ExternalProject: dotnet publish# C# AOT
└─ 复制产物到 flutter 的 bundle 目录
```

- CI 矩阵：Windows / Ubuntu / macOS，三平台都要跑 ABI 冒烟测试。
- 产物版本写入 `build-info.json`，UI "关于"页展示，便于排障。
- **不要**把 4 套工具链塞进一个脚本还不锁版本；用容器/固定版本。

---

## 11. 调试与可观测性

| 问题 | 手段 |
|---|---|
| 跨语言崩溃 | Crashpad/Breakpad minidump + 符号服务器；统一 `task_id` 关联日志 |
| C# AOT 难调试 | 业务放纯类库；DevHost 用 JIT；导出层保持极薄 |
| FFI 参数错 | "ABI trace" 模式：记录每次 method/参数长度/耗时 |
| 内存问题 | C/C++/Rust 开 ASan/UBSan；CI 跑一遍 |
| 死锁 | 固定线程模型 + 禁止跨模块回调；TSan |
| 版本错配 | 启动时打印所有模块版本，不匹配直接拒绝启动 |

---

## 12. 测试

1. **ABI 契约测试**（C 写的 harness）：加载库、调用每个方法、断言错误码。
2. **黄金事件序列**：同一输入 → 同一事件顺序（忽略时间戳）。
3. **边界 fuzz**：空指针、超长字符串、非法 UTF-8、重复 cancel、shutdown 后调用。
4. **跨语言集成**：CI 里跑"安装 1.20.1 + 假 JVM 启动"。
5. **崩溃注入**：让某模块故意 abort，验证崩溃处理器与 UI 提示。

---

## 13. 风险登记

| 风险 | 影响 | 概率 | 缓解 |
|---|---|---|---|
| 多语言工具链拖慢迭代 | 高 | 高 | MVP 先砍到 4 语言（去 C）；容器化构建 |
| 任一模块崩溃 = 全应用崩溃 | 高 | 中 | 崩溃处理器 + minidump；下载器可选子进程 |
| NativeAOT 调试困难 | 高 | 高 | 业务纯类库 + DevHost + 单测 |
| ABI 漂移 | 高 | 中 | 版本握手；UI 与核心同包发布；契约测试 |
| 内存所有权错误 | 高 | 中 | 所有权矩阵 + ASan + 代码评审清单 |
| 两套 HTTP 行为不一致 | 中 | 高 | 指定唯一 owner（见 §1） |
| 下载器路径/解压安全 | 高 | 中 | dest 规范化 + hash 校验 + fuzz |
| 包体膨胀 | 中 | 高 | AOT 裁剪、按需加载、体积基线 |
| 启动变慢 | 中 | 中 | 并行初始化；延迟加载非关键模块 |

---

## 14. 建议的落地顺序（垂直切片，2–3 周）

1. **只做一条链路**：Dart → C++ `ping` → 返回字符串。验证三平台加载/签名/打包。
2. 加**事件泵**：C++ 定时发 `TASK_PROGRESS`，Dart 合并后刷新一个进度条。
3. 加 **C# 模块**：`java.scan` 返回本机 Java 列表（真实业务，纯类库 + AOT 导出）。
4. 加 **Rust 模块**：对本地假 HTTP 服务器做 100 文件下载 + 校验 + 进度。
5. 把 3、4 接到真实 UI 页面，跑通"扫描 Java → 安装 → 启动"。
6. 此时再评估：Rust 是否值得保留（下载压测对比 C# 实现），以及 C++ 枢纽是否真的需要。

> 每加一门语言，都应该能回答："它解决了另外三門语言解决不了的问题吗？"
> 如果答案只是"我想用"，那就留到 v2。
