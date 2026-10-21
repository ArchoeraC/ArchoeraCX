# app/modules/downloader · Rust 下载器

`archoera_dl` 是一个独立的 **DownloadPlan 执行器**：把 C# 生成的只读文件清单变成文件。
它不生成 URL、不理解 Mojang/Modrinth/加载器、不解压归档、不写 SQLite。

当前为第一切片（单流 GET + 并发 + 镜像回退 + hash + 原子落盘 + 取消 + 进度），
尚未接入 C++ ModuleHost，也**没有导出模块 ABI**。

参考 [`docs/10-architecture-design.md`](../../../docs/10-architecture-design.md) §9、
[`docs/08-technical-research/05-download-engine.md`](../../../docs/08-technical-research/05-download-engine.md)。

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

crate 同时生成 `cdylib`（后续模块集成）和 `rlib`（测试/嵌入），并提供
`archoera-dl` CLI：

```bash
cargo run --release --bin archoera-dl -- /path/to/plan.json
# 或使用 app/ 下的 Modrinth 端到端脚本：
./tool/try-modrinth.sh sodium 1.21.1 fabric
```

CLI 只读取计划、执行下载并输出固定结果 JSON；Ctrl+C 会取消并清理 `.part`。

## 库 API

```rust
use archoera_dl::{CancellationToken, DownloadPlan, ProgressCallback, execute_plan};

let plan = DownloadPlan::from_json_slice(json_bytes)?; // 解析 + 全量校验
let cancel = CancellationToken::new();
let result = execute_plan(plan, cancel, Some(callback)).await?;
let json = result.to_json()?;
```

- `validate_plan(&plan)`：只校验，不启动下载。
- `execute_plan(plan, cancel, progress)`：校验后执行；计划错误返回 `DownloadError`，
  单文件错误进入 `DownloadResult.failed`。
- `ProgressCallback = Arc<dyn Fn(Progress) + Send + Sync>`：由下载任务推动，
  默认 150 ms 节流（`options.progressIntervalMs`），开始和结束时强制发送；无轮询。
- `DownloadResult` 字段：`planId/status/ok/skipped/failed/bytes/elapsedMs/post`。

## 已支持的计划子集

- `schemaVersion: 1`、`planId`、绝对 `root`（canonicalize）。
- `files[]`：`url`、`mirrors`、`hash{algo,hex}`（sha1/sha256/sha512），兼容旧字段
  `sha1/sha256/sha512`、`size`、`dest`、`mode`、`priority`、`headers`。
- `mode`：`ensure`（默认）、`replace`、`skip`。
- `options.verifyExisting`：`size`（默认）/`hash`/`none`；`durability=full` 时额外 fsync 父目录。
- `concurrency`：1–64，缺省 8；文件按 `priority` 稳定排序。
- `bandwidthLimit`：全局粗粒度令牌桶（0 = 不限速）。
- 其他选项（`dedup/rangeThreshold/chunkSize`）被解析但本切片不实现其行为。
- `post`：兼容字段，**不执行**，结果固定返回 `[]`。

路径与 URL 校验：拒绝绝对 `dest`、`..`、空段、NUL、反斜杠、Windows 保留名/ADS/
尾随点空格、符号链接父目录、重复 `dest`、`.part` 自身；URL 仅 HTTPS，
测试允许 loopback HTTP（`127.0.0.0/8`、`::1`、`localhost`），拒绝 userinfo。

## 落盘与错误

每个文件下载到同目录的 `<dest>.part`，流式写入、边写边算 hash，校验
`size` + `hash` 后 `rename` 到 `dest`；失败/取消清理 `.part`。结果状态：
`completed`/`partial`/`failed`/`cancelled`。错误码子集（完整表见 docs/08-05 §9）：

`INVALID_PLAN` `PATH_ESCAPE` `INVALID_HASH` `DUPLICATE_DEST` `UNSUPPORTED_SCHEME`
`DNS` `CONNECT` `TLS` `TIMEOUT` `HTTP_4XX` `HTTP_429` `HTTP_5XX`
`SIZE_MISMATCH` `HASH_MISMATCH` `DISK_FULL` `PERMISSION_DENIED` `FILE_LOCKED`
`DEST_IS_DIR` `CANCELLED` `INTERNAL`。

## 本切片限制

- 单流 GET，无 Range 探测/分片；无断点续传（`.part` 下次从头覆盖）。
- 无全局/每 host permit broker；并发仅限单 plan。
- 无 `.part.meta`/`.part.lock`、无内容库去重、无 `link`/`symlink`（校验时拒绝）。
- 重试为固定指数退避，未实现 `Retry-After`、被动源健康分和 jitter。
- 限速为块级粗粒度，不是 docs 中的 64 KiB 精密令牌桶。
- 取消后清理 `.part`（文档建议保留 7 天，作为后续增强）。
- 未做 NFC 归一化和大小写不敏感的跨平台冲突检测（仅 Windows/macOS 去重键小写）。
- `headers` 白名单仅 `Authorization`/`Cookie`；不执行解压型 `post`。
