# 13 · 传输与协议回退（连接不只一种尝试）

> 目的：定义下载器在“连接尝试”上的完整协议栈，以及当某条路径失败时，怎样降级而不破坏完整性、并发与安全边界。
> 结论先行：**启动器场景不需要 FTP/BT，但需要 HTTP 版本与地址族的回退**；本主题的起点是 HTTP/1.1 曾同时写死在编译期与运行期，属于单协议实现（2026-10-24 已改为双 client，见下）。
> 标记：**[事实]** 源码/手册可核对；**[推断]** 基于协议与实现的判断；**[未验证]** 需要本项目实测。
> 引用基线：reqwest `0.13.5`（本地 `~/.cargo/registry` 源码）、hyper-util `0.1.21`、curl 手册（2026-10-24）、RFC 8305/9110/9113/9114/7838。

> **实现状态（2026-10-24）**
>
> - **[事实] 双 client**：`client.rs` 构造 `auto`（ALPN `h2` + `http/1.1`，h2 keep-alive 20 s / 10 s、idle 也保活）与 `http1`（`http1_only()`）；两者共享超时、连接池、代理与 Modrinth 边缘探测地址，`Cargo.toml` 已启用 `http2` feature。
> - **[事实] 选择规则**：单流文件先走 `auto`；所有 Range/chunk 固定 `http1`（一 chunk 一连接）；`edge.rs` 的边缘探测仍是 `http1`。
> - **[事实] 降级**：`421`、HTTP/2 响应上的 `505`、以及连接/响应头阶段的 h2 连接级错误，在零字节写入时把该 origin 记入内存降级表 600 s，并立即用 `http1` 重试同一尝试一次；分片不受降级表影响（本就 h1）。
> - **[事实] h3 未启用**：reqwest 0.13.5 需要 `reqwest_unstable` + `http3`，只有强制 h3 路径，且不能走代理。
> - **[未验证]** 真实网络上的 h2/TLS 协商行为与吞吐基准；5k 小文件下 h2 相对 h1 的实际收益；h3 的 UDP 可达率与收益。

---

## 0. 结论速览

| # | 结论 | 依据 |
|---|---|---|
| 1 | 下载器的“协议”不是一层，而是五层：**地址族 / 传输 / TLS+ALPN / HTTP 版本 / 代理与 scheme**。只测一层等于没测。 | [事实] §1 |
| 2 | 2026-10-24 前我们被钉死在 HTTP/1.1 的**两处**：`Cargo.toml` 没有 `http2` feature，`client.rs` 还调了 `http1_only()`；现在已改为 `http2` feature + 双 client。 | [事实] 源码 |
| 3 | 成熟实现的做法是“**先协商、失败再降级、慢也算失败**”，不是“只试一种”。curl 的 `--http3` 会并行发一条旧版本尝试，带轻微延迟。 | [事实] curl 手册 |
| 4 | 地址族回退我们已经**免费拥有**：hyper-util 默认 300 ms Happy Eyeballs（RFC 8305），reqwest 没有关掉它。 | [事实] `hyper-util/src/client/legacy/connect/http.rs:228` |
| 5 | 协议选择必须是 **per-origin 的状态**，且失败要分类：`421/505/GOAWAY` 降级，`404/403/hash mismatch` 不降级。 | [推断] §4 |
| 6 | 协议切换的原子单位是**一次尝试**（一个 chunk 或一次单流 GET），绝不跨响应拼接；`.part.meta` 与协议无关。 | [推断] §5 |
| 7 | 现在**不建议上 HTTP/3**：reqwest 0.13 只有强制 h3（需 `reqwest_unstable` 编译 cfg，无竞速、无 Alt-Svc），且代理不可用。 | [事实] reqwest 源码、curl 手册 |

一句话：多协议不是“多写几个 URL”，而是**在每一层保留一条可退的路，并且知道什么错误才值得退**。

---

## 1. 分层：一次下载到底经过哪些协议

```
DNS (A/AAAA)                         ← 地址族回退（Happy Eyeballs）
  └─ TCP / UDP(QUIC)                 ← 传输
       └─ TLS 1.2/1.3 + ALPN         ← 证书、SNI、协商 h2 / http/1.1 / h3
            └─ HTTP/1.1 | HTTP/2 | HTTP/3
                 └─ 代理：HTTP CONNECT / SOCKS5
                      └─ scheme：https（唯一）、http（仅 loopback）
```

每一层都可能失败，且**失败语义不同**：

| 层 | 典型失败 | 能否换一条路 |
|---|---|---|
| 地址族 | AAAA 有记录但不可达 | 能：IPv4 兜底 |
| 传输 | TCP 被重置 / UDP 被封 | 能：换传输（h3 → h1/h2） |
| TLS | 证书、主机名、协议版本 | **不能**：这是安全边界，换了就是降级攻击 |
| HTTP 版本 | ALPN 未选 h2、505、GOAWAY | 能：同源降级 |
| 代理 | CONNECT 被拒、认证失败 | 能：换代理或报错；**不自动直连** |
| scheme | https 失败 | **不能**降级到 http（loopback 除外） |

本文的“协议回退”只覆盖**允许换**的那几层；安全边界一律不降级。

---

## 2. 我们现在的实际状态

| 维度 | 现状 | 位置 |
|---|---|---|
| HTTP 版本 | 双 client：`auto` 走 ALPN（`h2` 优先、h1.1 兜底），`http1` 固定 HTTP/1.1；`http2` feature 已启用 | `Cargo.toml`、`client.rs` |
| 版本选择 | 单流文件用 `auto`；Range/chunk 固定 `http1`；per-origin 降级 600 s | `protocol.rs`、`executor.rs`、`chunk_stream.rs` |
| TLS | reqwest 0.13 的 `default-tls` 已指向 **rustls + 平台证书**（`rustls-platform-verifier`，含企业根）+ aws-lc-rs，支持 ALPN/TLS1.2+ | `Cargo.toml`、reqwest `Cargo.toml`、`Cargo.lock` |
| 地址族 | hyper-util 默认 Happy Eyeballs 300 ms；我们未覆盖 | `hyper-util` `connect/http.rs:228` |
| DNS 覆盖 | 仅 Modrinth：探测 Fastly 边缘后 `resolve_to_addrs`，保留 resolver 地址做尾部兜底 | `edge.rs` |
| scheme | 仅 https；http 只允许 loopback | `plan.rs`、`client.rs` |
| 代理 | 只接受 C# 显式下发的 http/socks，不读环境变量；两个 client 用同一份代理设置 | `client.rs` |
| h3 | 未启用（feature 未开，且 reqwest 只提供强制 h3） | `Cargo.toml` |

两个容易忽略的事实：

1. **`http1_only()` 不是全局策略**——它只落在 `http1` client 与边缘探测上；`auto` 不写 `http1_only()`，是否走 h2 完全由 ALPN 决定。改造时两处必须一起看。
2. **边缘探测客户端仍然写死了 h1**（`edge.rs`）。主客户端改协议后，探测仍应保持 h1：它只取 128 KiB，握手成本占主导，不需要 h2。

---

## 3. 外部实现的做法

### 3.1 curl：协商优先，竞速兜底

- HTTPS 下默认通过 ALPN 协商 HTTP/2；`--http2` 不额外增加 RTT（`curl` 手册）。
- `--http3` 的语义不是“只用 h3”，而是：**先尝试 h3，失败或太慢就回退**；并且会**并行发一条旧版本（h1/h2）尝试，带轻微延迟**，谁先成功用谁。
- `--http3-only` 才是“没有回退”。
- `--happy-eyeballs-timeout-ms`：IPv6 先跑，超时后并行 IPv4，取先建连的一方；手册引用 RFC 6555 建议的 150 ms 量级。
- **HTTP/3 不能走任何代理**（`curl` 手册明确写出）。

这几乎就是一套完整的“多协议尝试”规范：**ALPN 负责零成本协商，竞速负责慢路径，显式开关负责强制**。

### 3.2 浏览器：用 Alt-Svc 发现，而不是盲试

浏览器不会对每个域名盲发 QUIC。服务器通过 `Alt-Svc`（RFC 7838）声明“我也支持 h3”，客户端缓存后**下次**升级，并保留 TCP 路径；升级失败就回退。这个模型的代价是状态缓存，收益是不浪费首包。

[推断] 对启动器来说，首次访问就要下载，Alt-Svc 的“下次生效”价值有限；但如果要做 h3，**也应该走 advertisement + 缓存**，而不是对每个 CDN 盲发 UDP。

### 3.3 reqwest / hyper-util 能提供什么

| 能力 | reqwest 0.13.5 | 说明 |
|---|---|---|
| h2 协商 | `http2` feature；默认 `HttpVersionPref::All` → ALPN `["h2","http/1.1"]` | 需要显式开启 feature；开启后不写 `http1_only()` 即自动协商 |
| h2 强制 | `http2_prior_knowledge()` | 明文 h2c / 已知 h2，不用于生产 |
| h3 | `http3` feature + `reqwest_unstable` + `http3_prior_knowledge()`（请求版本也要显式 `HTTP_3`） | **只有强制 h3 的路径**，没有竞速/Alt-Svc；需要 `quinn`、`h3`、`h3-quinn`；`reqwest_unstable` 要额外 `RUSTFLAGS` |
| Happy Eyeballs | hyper-util 默认 300 ms，按地址族分组并行 | 不能从 reqwest builder 直接调，但默认开启 |
| 每地址连接超时 | 总 `connect_timeout` 按**该组**地址数均分（v4/v6 各自分组） | 地址多时每个地址预算变小，需注意 |
| TLS 版本 | `tls_version_min/max` | 我们未设置，rustls 默认 1.2+ |
| 代理 | HTTP CONNECT / SOCKS | h2 可穿 CONNECT；h3 不行 |

结论：**要做 h2 回退，不需要新依赖**，只需打开 `http2` feature 并建第二个 client；本仓库已按此落地。**要做 h3，代价是 quinn 全家桶 + 自己实现竞速/回退**，现阶段不划算。

---

## 4. 抽出的原理

### P1 协议偏好是 per-origin 状态

不能全局 `http1_only`，也不能全局“永远 h2”。正确模型是每个 host 一条小状态：

```
Demoted { until: Instant }   // 表里没有 = 用 auto
```

- 默认 `auto`（ALPN 协商）；
- 协议类失败 → 记入降级表并冷却 600 s；
- 冷却结束 → 移除记录，下一次尝试回到 `auto`（半开探测）。

状态只放内存，进程退出即清空；不写 SQLite（C# 独占），也不跨 plan 持久化。

### P2 先协商，后降级

TLS 的 ALPN 是**零额外 RTT** 的协商：客户端给出 `["h2","http/1.1"]`，服务器选一个。绝大多数 CDN 都支持 h2，所以“默认全部 ALPN”比“默认 h1”更合理；只有明确不适合 h2 的路径（大文件分片）才强制 h1。

### P3 慢也是失败（竞速 / 延迟兜底）

**[未实现]** 当前只按错误分类降级，没有按 TTFB/吞吐比较 h1 与 h2。curl 对 h3 的处理说明慢路径也值得退：不是等 h3 超时，而是**并行**发旧版本，带一点延迟。对我们的 h1/h2 选择，成本更低的等价物是：

- 首次连接后记录该 origin 的 TTFB 与吞吐；
- 若 h2 路径在 N 次采样里显著慢于 h1（或出现连接级错误），降级；
- 不在单个 chunk 上同时发两份请求（会浪费流量，且破坏带宽配额语义）。

### P4 失败必须分类

| 信号 | 归类 | 动作 |
|---|---|---|
| 证书/主机名/TLS 版本错误 | 安全 | **不重试、不换协议、不换源**，报错 |
| ALPN 未选 h2（服务器只给 h1.1） | 正常协商 | 用 h1.1，不算失败 |
| h2 `GOAWAY` / `REFUSED_STREAM` / 帧错误 | 协议 | 同源降级 h1，重试该 chunk |
| `421 Misdirected Request` | 协议/连接复用 | 直接降级 h1 并重试一次 |
| `505 HTTP Version Not Supported` | 协议 | 仅当响应版本是 HTTP/2 时降级；h1 响应的 505 直接失败 |
| h3 握手超时 / UDP 不可达 | 传输 | 回退 h1/h2（仅当启用 h3） |
| `403/404/410/429` | 内容/权限/限流 | 不换协议；按现有 4xx/429 分类 |
| `5xx` | 服务端 | 可重试/换源，不换协议 |
| `HASH_MISMATCH` / `SIZE_MISMATCH` | 内容 | 换源，不换协议 |

### P5 一次尝试只用一个协议

Range 的语义在 h1/h2 下一致，但**不要在同一个响应中途切协议**（也不可能），更不要让不同 chunk 的失败互相归因。保持：

- 单流文件：一次 GET 用 auto client（h2 优先）；
- 分片文件：每个 chunk 用 h1 client，重试也只在该 client 内；
- 协议降级只影响**下一次尝试**，不影响已经在途的响应。

### P6 并发模型要跟着协议变

`Semaphore` 数的是**流（stream）**，不是连接：

- h1：1 流 = 1 连接；每 host 8 流 = 8 条 TCP。
- h2：N 流可以复用 1 条 TCP；`per_host_concurrency` 仍限制流数，但连接数只有 1，收益是省握手，风险是 TCP 层 HOL。

因此“每 host 8”在 h2 下的含义从“8 条连接”变成“1 条连接上的 8 个流”，不能把 h1 的经验数字直接套过去。大文件分片继续用 h1，正是为了避免单连接流控 + TCP HOL 把聚合吞吐压住。

### P7 安全边界不参与回退

- 不允许 https → http 降级（loopback 测试除外）；
- 不允许关闭证书校验“再试一次”；
- 不允许为了 h3 放宽 TLS/证书要求；
- 代理失败时是否直连由用户显式配置决定，不自动绕过。

### P8 可观测性

内部至少要知道“这个 host 实际用了什么协议、失败了几次”。当前实现只在降级时向 `ARCHOERACX_DL_DEBUG` 的 stderr 打一行日志，尚未输出每次尝试的实际版本；**不新增 ABI 字段**，等协议策略稳定后再考虑在结果里加 `transport`（那是一次契约变更）。

---

## 5. 对 `archoera_dl` 的具体设计

### 5.1 两个 client，而不是一个开关（已实现）

```text
build_clients(options) -> Clients {
    clients.auto:  ALPN ["h2","http/1.1"]   // 小文件、单流
    clients.http1: http1_only()             // 分片、大文件
    edge probe:    http1_only(), pool_max_idle_per_host(0)  // edge.rs 专用
}
```

选择规则：

| 文件形态 | client | 理由 |
|---|---|---|
| `size < range_threshold`（默认 8 MiB） | `auto` | 5k 个小资源，握手占主导；h2 多路复用可省连接 |
| `size >= range_threshold` 且分片 | `h1` | 一 chunk 一连接，绕开单连接流控/HOL；与 `981e49f` 的实测一致 |
| 回退重试 | `auto` → `h1` 一次 | 仅协议类失败且本次尝试零字节写入；内容类失败不换 |

**[未验证]** “小文件用 h2 更快”必须用基准验证（H1/H2 对比、每 host 8 流）。若结果不显著，保留 h1 也可接受，但不应再把 `http1_only` 当作全局策略。

### 5.2 失败分类落在哪（已实现）

单流尝试的重试由 `executor.rs` 的 `download_single` 包住；chunk 内重试仍在 `chunk_stream.rs`。分类由两个纯函数完成：

```text
wire::protocol_status(status, version) -> bool   // 421 恒真；505 仅在响应版本为 HTTP/2 时真
protocol::is_protocol_failure(error)   -> bool   // 连接级 h2 错误串、421、505
```

只对 `421`、h2 响应上的 `505`、连接/响应头阶段的 h2 连接级错误返回真；证书/TLS、4xx（非 421）、5xx（非 505）、内容失败一律返回假。配套约束：

- `421` 仍落进 `Http4xx`，但状态映射在进入 `retryable()` 之前就把它标成协议失败，因此回退不依赖 4xx 的可重试性；
- 回退只发生在单流、`auto` 尝试、且本次写入为 0 字节时；分片没有跨协议回退；
- 回退只重试当前尝试，不重置整个文件；`.part.meta` 的 bitmap 与协议无关，恢复语义不变。

这样不改错误码体系，也不影响结果 JSON。

### 5.3 冷却与半开（已实现）

- 默认 `auto`；不记录“该 host 是否协商到 h2”——ALPN 成本已摊薄，不值得再区分。
- 协议类失败：该 host 冷却 **600 s**，期间单流也走 `http1`；状态属于本次执行（每个 plan 一个 `Engine`），不持久化、不跨进程共享。
- 冷却结束：下一次尝试自然回到 `auto`（这次尝试就是半开探测）；再失败则重新冷却。
- 分片请求永远 `http1`，不参与降级表。
- 调试开关 `ARCHOERACX_DL_DEBUG` 打开时，降级会向 stderr 打一行日志。

### 5.4 HTTP/3：本次未启用

| 维度 | 现状 |
|---|---|
| reqwest 支持 | `http3` feature + `RUSTFLAGS='--cfg reqwest_unstable'`；只有 `http3_prior_knowledge()` 这条强制路径，没有 Alt-Svc，也没有竞速；请求还要显式 `Version::HTTP_3` |
| 构建成本 | `reqwest_unstable` 是编译期 cfg，会污染可复现构建；依赖多出 `quinn`/`h3`/`h3-quinn` |
| 网络现实 | UDP/443 在校园网、企业网、部分运营商常被封；失败表现是静默超时 |
| 代理 | curl 明确“HTTP/3 不能走任何代理”；我们的代理路径会直接失去 h3 |
| 收益 | 5k 个小文件的瓶颈是 RTT/握手，QUIC 的 0-RTT 有收益，但只在“第二次访问同一 host”时成立 |

结论：**v1 不引入 h3**；等 reqwest 提供“opportunistic h3 + Alt-Svc + 自动回退”再评估。**[未验证]** 需要先在目标网络矩阵里测 UDP 可达率。

### 5.5 地址族与 DNS

- 保持 hyper-util 默认 Happy Eyeballs（300 ms），不要为了“统一”把它关掉；
- 注意族优先：`split_by_preference` 会先把地址按 IPv4/IPv6 分组，只有**同一族内**才保留我们“快边缘在前”的顺序；如果系统偏好 IPv6，排在前面的 IPv4 快边缘会先等 300 ms 才被并行尝试。这是可接受的成本，但排查“边缘探测没生效”时要先想到它；
- `edge.rs` 的 `resolve_to_addrs` 已把 resolver 地址放在尾部兜底，保持；
- 若未来加自定义 DNS（hickory），必须同时保留 A/AAAA 两个族，并继续交给 Happy Eyeballs；
- 不要把 `resolve_to_addrs` 的结果当成永久列表——Fastly 边缘会变，进程内一次探测即可（现状正确）。

### 5.6 代理

- `Proxy::all` 同时作用于两个 client；h2 over CONNECT 由 rustls ALPN 正常协商；
- 代理失败不自动直连；
- `no_proxy` 语义保持由 C# 下发，Rust 不猜。

---

## 6. 参数建议

| 参数 | 建议 | 说明 |
|---|---|---|
| `connect_timeout` | 10 s（现状） | 多地址时会被均分，地址多时实际每地址预算更小 |
| Happy Eyeballs | 300 ms（hyper-util 默认） | 不覆盖 |
| ALPN 顺序 | `h2`, `http/1.1` | 让服务器选 |
| h2 keep-alive | 20 s / 10 s 超时 | 已在 `auto` client 配置（idle 也保活） |
| h2 流窗口 | 默认 | 先不调，等基准 |
| 协议冷却 | 10 min（600 s） | 仅内存 |
| 半开探测 | 冷却后 1 次 | 下一次尝试自然回到 `auto`，失败再冷却 |

---

## 7. 测试矩阵

| # | 场景 | 期望 |
|---|---|---|
| 1 | 服务器 ALPN 只给 h1.1 | 正常下载，不报错、不反复重试 |
| 2 | 服务器支持 h2，但 h2 路径返回 `GOAWAY` | 单流尝试用 h1 重试一次，host 进入冷却；分片本就是 h1 |
| 3 | 服务器返回 `505` | h2 响应上的 505 降级 h1；h1 响应上的 505 不降级，按普通 5xx 重试/换源 |
| 4 | `421 Misdirected Request` | 立即降级 h1 并重试一次；成功则完成，失败按普通错误处理 |
| 5 | 证书错误 | 直接失败，**不**换协议、**不**换源 |
| 6 | 仅 IPv6 可达 / 仅 IPv4 可达 | Happy Eyeballs 成功连接 |
| 7 | 代理 CONNECT + h2 | 正常协商 |
| 8 | 分片下载（h1）与单流（auto）混跑 | 配额不超、无跨协议拼接 |
| 9 | 强制 h3（若未来启用）+ UDP 丢包 | 回退 h1/h2，不整任务失败 |

本地测试可用 loopback HTTP + `http2_prior_knowledge` 构造 h2c 服务器验证 h2 路径；ALPN 回退需要 TLS 测试证书（**[未验证]** 现测试架尚未支持，需新增夹具）。

已加入的自动化测试（只描述覆盖范围，未在此声明通过）：

| 层 | 位置 | 覆盖 |
|---|---|---|
| 单元 | `src/protocol.rs` | 降级表：默认 auto、按 host（大小写归一）降级、600 s 后恢复 auto；`is_protocol_failure` 的正/负分类 |
| 单元 | `src/wire.rs` | `protocol_status`：421 恒降级，505 仅 h2 响应降级，404/500 不降级 |
| 集成 | `tests/executor.rs` | `421` 后回退到 HTTP/1.1 并完成下载 |

---

## 8. 相邻发现

reqwest 0.13 的 `default-tls` 已改为 `rustls`（`rustls-platform-verifier` + aws-lc-rs）；`05-download-engine.md` §6.1 曾写“Windows/macOS 用 native-tls”，已于 2026-10-24 一并修正，避免文档与 `Cargo.lock` 继续漂移。

---

## 9. 参考

- curl 手册：`--http2`（HTTPS 默认 ALPN 协商）、`--http3`（尝试 + 延迟回退 + 竞速）、`--http3-only`、`--happy-eyeballs-timeout-ms`（<https://curl.se/docs/manpage.html>）。
- RFC 8305（Happy Eyeballs v2）、RFC 9110（语义）、RFC 9113（HTTP/2）、RFC 9114（HTTP/3）、RFC 7838（Alt-Svc）。
- reqwest 0.13.5：`src/async_impl/client.rs:529-545, 820-860, 1566-1595`（ALPN / 版本偏好 / h3 强制）、`Cargo.toml` features（`http2`、`http3`、`default-tls = ["rustls"]`）。
- hyper-util 0.1.21：`src/client/legacy/connect/http.rs:228, 350-365, 732-753`（300 ms Happy Eyeballs、地址分组）。
- 本仓库：`client.rs`、`protocol.rs`、`wire.rs`、`edge.rs`、`plan.rs`、`executor.rs`、`chunk_stream.rs`。
