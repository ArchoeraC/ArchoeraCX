# app/modules/downloader · Rust 下载器

`archoera_dl` 是一个独立的 **DownloadPlan 执行器**：把 C# 生成的只读文件清单变成文件。
它不生成 URL、不理解 Mojang/Modrinth/加载器语义、不解压归档、不写 SQLite，也**尚未导出模块 ABI**
（未接入 C++ ModuleHost）。crate 版本跟随 `app/pubspec.yaml`（`0.0.1+1`）。

参考 [`docs/10-architecture-design.md`](../../../docs/10-architecture-design.md) §9、
[`docs/08-technical-research/05-download-engine.md`](../../../docs/08-technical-research/05-download-engine.md)、
[`docs/08-technical-research/13-transport-protocol-fallback.md`](../../../docs/08-technical-research/13-transport-protocol-fallback.md)。

## 构建与测试

```bash
cd app/modules/downloader
cargo fmt -- --check
cargo test
cargo build --release

# 或在 app/ 下：
./tool/build-dl.sh
# 产物：app/modules/downloader/target/release/libarchoera_dl.so
```

crate 同时生成 `cdylib`（后续模块集成）和 `rlib`（测试/嵌入），并提供 `archoera-dl` CLI：

```bash
cargo run --release --bin archoera-dl -- /path/to/plan.json
./tool/try-modrinth.sh sodium 1.21.1 fabric   # app/ 下的 Modrinth 端到端脚本
```

CLI 只读取计划、执行下载：进度 JSON 写 stderr，结果 JSON 写 stdout；退出码 `0` 完成、
`1` partial/failed、`2` 计划或参数错误、`130` 取消。Ctrl+C 取消并清理 `.part`/`.part.meta`。

## 库 API

```rust
use archoera_dl::{CancellationToken, DownloadPlan, ProgressCallback, execute_plan};

let plan = DownloadPlan::from_json_slice(json_bytes)?; // 解析 + 全量校验
let cancel = CancellationToken::new();
let result = execute_plan(plan, cancel, Some(callback)).await?;
let json = result.to_json()?;
```

- `validate_plan(&plan)` 只校验；`execute_plan(plan, cancel, progress)` 先校验再执行：计划错误返回
  `DownloadError`，单文件错误进入 `DownloadResult.failed`。
- `ProgressCallback = Arc<dyn Fn(Progress) + Send + Sync>`：由下载任务推动，按
  `options.progressIntervalMs`（默认 150，钳制 20–5000）节流，开始/结束强制发送；无轮询。
  字段：`done_bytes/total_bytes/files_done/files_total/current_dest`。
- `DownloadResult` 字段：`planId/status/ok/skipped/failed/bytes/elapsedMs/post`。

## DownloadPlan 子集

- `schemaVersion: 1`、`planId`、绝对 `root`（canonicalize，必须是已存在目录）。
- `files[]`：`url`、`mirrors`、`hash{algo,hex}`（sha1/sha256/sha512，兼容旧字段
  `sha1/sha256/sha512`）、`size`、`dest`、`mode`、`priority`、`headers`。
- `mode`：`ensure`（默认）、`replace`、`skip`；`link`/`symlink` 在校验期直接拒绝。
- `concurrency`：1–64，缺省 **16**；文件按 `priority` 稳定排序后依次占位。
- `options.perHostConcurrency`：每 host 流上限，缺省 8，`0` 关闭该限制（上限 64）。
- `options.rangeThreshold` / `options.chunkSize`：缺省 8 MiB / 4 MiB（详见下节）。
- `options.verifyExisting`：`size`（默认）/`hash`/`none`；`durability`：`normal`（默认）/
  `full`（额外 fsync 父目录）/`fast`（跳过 fsync）。
- `bandwidthLimit`：全局粗粒度令牌桶（字节/秒，`0` = 不限速）。
- `options.proxy`：显式代理 `http(s)/socks5(h)` + `noProxy`；未配置时**禁用**环境变量代理。
- `post`：兼容字段，**不执行**，结果固定返回 `[]`。

路径与 URL 校验：拒绝绝对 `dest`、`..`、空段/`.` 段、NUL、反斜杠、`:`（ADS）、控制字符、
Windows 保留名、尾随点/空格、符号链接父目录、重复 `dest`、`.part`/`.part.meta`/`.part.lock`
自身、超 1 TiB 的文件；URL 仅 HTTPS，测试允许 loopback HTTP（`127.0.0.0/8`、`::1`、
`localhost`），拒绝 userinfo；`headers` 白名单仅 `Authorization`/`Cookie`。

## 传输协议策略

两个 `reqwest` 客户端共享全部设置（UA、超时、重定向白名单、代理、连接池），只有协议协商不同：

- 小文件/单流使用 ALPN 客户端：服务器提供 `h2` 时走 HTTP/2，否则 HTTP/1.1，并带 HTTP/2
  keep-alive，供成千上万个小文件复用连接。
- Range 分片一律使用 **仅 HTTP/1.1** 客户端：一个分片占一条 TCP 连接，避免单连接流控压住总带宽。
- 协议层失败（HTTP/2 连接错误、`421`、或 h2 响应上的 `505`）且**尚未收到任何字节**时，把该
  origin 降级为 HTTP/1.1 共 600 s，并立即用 h1 重试一次；TLS/内容错误永不触发降级。降级表仅进程内。
- 边缘探测客户端保持 HTTP/1.1；未启用 HTTP/3（reqwest 0.13.5 需 `reqwest_unstable` + `http3`，
  且只能强制 h3、不支持代理）。

`cdn.modrinth.com` 会在启动时对若干 Fastly anycast 地址 + 解析结果做 128 KiB Range 探测，
按实测吞吐排序后交给客户端；全部失败则沿用 DNS，探测结果进程内只算一次。

## 并发与断点

- 一条全局流信号量覆盖所有 HTTP 响应：全局额度 = `concurrency`（缺省 16），分片与单流共用；
  文件槽位单独计数，大文件不会阻塞自己的分片。
- 每个 host 另有独立 gate（缺省 8，`0` 关闭），先取全局再取 host，无锁序反转。
- `size >= rangeThreshold`（缺省 8 MiB）且大于 `chunkSize`（缺省 4 MiB）时按 `chunkSize`
  切分，每片一条连接并发抓取，`Content-Range` 逐片校验。
- 断点：`.part.meta` 记录 `size`/`hash`/`chunkSize`/`ETag`/`Last-Modified`/已完成分片索引，
  仅当这些键全部匹配才续传；续传时带 `If-Range`（强 ETag，退回 Last-Modified）。
- 服务器忽略 Range（`200`/`416`）或校验器变化时，整文件回退为一次普通 GET。
- 重试：每源 3 次、每文件累计 12 次，退避 500 ms 起按 2 的幂增长、上限 5 s；镜像在同源耗尽后
  依次接替；本地错误（磁盘/权限/路径）立即终止该文件。
- C# `DownloadPlanRunner.ApplyTuning` 用 AppSettings 覆盖计划的 `concurrency`、
  `perHostConcurrency`、`chunkSize`、`rangeThreshold`（计划仍是声明式的）。

## 落盘与错误

每个文件写同目录的 `<dest>.part`：单流边写边算 hash，分片在全部完成后整文件重算；校验 `size`
+ `hash` 通过后按 `durability` fsync 再 `rename` 到 `dest`（文件被占用时指数退避重试 5 次）。
可恢复的失败会保留 `.part` + `.part.meta`，其余失败与取消删除两者。结果状态
`completed`/`partial`/`failed`/`cancelled`。错误码（完整表见 docs/08-05 §9）：
`INVALID_PLAN` `PATH_ESCAPE` `INVALID_HASH` `DUPLICATE_DEST` `UNSUPPORTED_SCHEME` `PLAN_TOO_LARGE`
`DNS` `CONNECT` `TLS` `TIMEOUT` `HTTP_4XX` `HTTP_429` `HTTP_5XX` `SIZE_MISMATCH` `REDIRECT_LOOP`
`ENCODING_UNSUPPORTED` `HASH_MISMATCH` `DISK_FULL` `PERMISSION_DENIED` `FILE_LOCKED` `PATH_TOO_LONG`
`CROSS_DEVICE` `DEST_IS_DIR` `READ_ONLY` `CANCELLED` `INTERNAL`。

## 已知限制

- 无 `.part.lock`，同一目标被两个 plan 同时下载没有 `DEST_BUSY` 保护。
- 无内容库去重：`options.dedup` 会被解析，但不产生任何行为。
- 无磁盘空间预检；只有在写入时才会得到 `DISK_FULL`。
- 未处理 `Retry-After`；429/503 只按固定退避重试。
- 无被动源健康分：镜像顺序固定为 `url` 后按 `mirrors` 顺序。
- 单流无续传：小于 `rangeThreshold` 的文件重试/重启时从头覆盖 `.part`。
- 未做 NFC 归一化和大小写不敏感的跨平台冲突检测（仅 Windows/macOS 去重键小写）。
