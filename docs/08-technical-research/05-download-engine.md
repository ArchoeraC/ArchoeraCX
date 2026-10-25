# 05 · Rust 下载引擎（消费 DownloadPlan）

> `archoera_dl` 只做“字节 → 文件”：不生成 URL、不理解 Mojang/Modrinth 语义、不写 SQLite。输入是 C# 的 `DownloadPlan`，输出是固定结构体事件 + JSON 结果。
> 标记：**[事实]** 有官方文档/源码/实测；**[推断]** 基于协议与实现的判断；**[未验证]** 需本项目实测。
> 对比源码为 2026-10-20 HEAD：CmlLib.Core `ffa2e90`、ProjBobcat `57532e2`、HMCL `8827fd0`、XMCL `75a52fa`、Modrinth `7c5c1b5`；只读行为，不复制 GPL 代码。

## 1. 任务模型
### 1.1 契约（兼容 07 §2.5）

> **后续修订**：`10-architecture-design.md` §9 已定 **Rust 不解压任何归档**；下文 `post.extract_natives` 仅作早期草案保留，实际由 C# 在 continuation 中执行。
```json
{ "schemaVersion": 1, "planId": "8f2c...", "root": "C:/.../instances/abc",
  "concurrency": 16, "priority": 0, "bandwidthLimit": 0, "totalBytes": 123456789,
  "options": { "dedup": true, "verifyExisting": "size", "durability": "normal",
               "rangeThreshold": 8388608, "chunkSize": 4194304, "progressIntervalMs": 150 },
  "files": [{ "url": "https://piston-data.mojang.com/.../client.jar",
    "mirrors": ["https://bmclapi2.bangbang93.com/..."],
    "hash": { "algo": "sha1", "hex": "0d1c..." }, "size": 25165824,
    "dest": "versions/1.21.4/1.21.4.jar", "mode": "ensure", "priority": 0,
    "executable": false, "headers": { "Authorization": "Bearer ..." } }],
  "post": [{ "type": "extract_natives", "src": "libraries/...", "dest": "natives",
             "exclude": ["META-INF/**"], "required": true }] }
```
| `mode` | 语义 |
|---|---|
| `ensure`（默认，兼容旧 `create`） | dest 存在且大小匹配则跳过；`verifyExisting=hash` 时再校验哈希；否则原子替换 |
| `replace` | 无条件重下并替换 |
| `link` | 内容库硬链接；缺失则下载到内容库再链接，失败降级复制 |
| `symlink` | 仅高级选项；创建相对符号链接，Windows 需权限，失败降级复制 |
| `skip` | 存在即跳过、不校验；仅生产者确认的标记文件，默认禁用 |

**校验**（失败即 `INVALID_PLAN`，不启动下载）：兼容 07 旧字段 `sha1/sha256/sha512`，归一化为 `hash`；`root` 绝对路径并 canonicalize；`dest` 必须相对，拒绝绝对路径、`..`、空段、NUL、Windows 保留名（CON/PRN/AUX/NUL/COM1-9/LPT1-9，含扩展名及尾随点/空格）、ADS 冒号；Windows/macOS 大小写不敏感冲突检测；规范化后仍在 root 内，父目录符号链接/junction 解析后仍在 root 内。URL 仅 https（测试允许 loopback）；拒绝 userinfo；mirror 去重。hash 必填，白名单 `sha1/sha256/sha512`；`size` 必须给出（0 允许为空文件）。上限：200k 文件、64 MiB 计划 JSON、单文件 1 TiB；`totalBytes` 按 `sum(size)` 重算，仅用于进度。默认 `verifyExisting=size`（对 10k assets 避免全量哈希）；jar、可执行文件、用户 Mod 建议 C# 设 `hash`，否则同尺寸损坏会被误判为已完成。`planId` 幂等：运行中重复提交返回同一 `taskId`，完成后缓存 10 分钟；不同 plan 写同一 dest 时按 dest 加锁，后到者等待或 `DEST_BUSY`。

### 1.2 状态机、取消/暂停
```
Plan: Created → Validating → Running ⇄ Pausing → Paused
                               ├→ Finalizing → Completed
                               ├→ Failed
                               └→ Cancelled
File: Pending → Checking → Probing → Downloading → Verifying → Committing → Done
                    │                     │→ RetryWait → Downloading
                    │→ Skipped(已存在)     └→ Failed
```
**暂停**：停止派发新 chunk，在途请求可 drop；已完成 chunk 的 bitmap 落盘；`checkpoint()` 后发 `PAUSED`，任务保留，`resume` 继续。**取消**：停止派发、丢弃在途响应，并**立即删除 `.part` 与 `.part.meta`**（不保留续传数据），终态 `Cancelled`。**异常退出/崩溃**：已落盘的 `.part` 与 `.part.meta` 保留，下次按 `(dest, hash, size)` 匹配续传；启动时清理超期残留。**退出**：取消 token，`JoinSet` 等 2s，超时 abort，但 `.part.meta` 必须先写完；禁止 `std::process::exit`。

### 1.3 进度聚合（100–200ms）
每 chunk 一次 `AtomicU64::fetch_add(Relaxed)`，不逐字节回调；`Instant::now()` 只在 chunk 边界取。单一定时器 `interval(150ms)`（`MissedTickBehavior::Skip`）读全局/文件原子量，算 1s EWMA 速度；无变化且非状态迁移则跳过。事件用固定结构体走 C ABI：
```c
typedef struct { uint64_t task_id, done, total, bytes_per_sec;
  uint32_t files_done, files_total, current_file; uint8_t state; uint8_t _pad[3]; } archoera_dl_progress;
```
只发 `current_file` + 聚合值；UI 要完整列表时调 `download.snapshot` 分页读，避免 150ms 发 10k 条。最终事件强制发送；`done` 包含已存在/去重跳过文件（按 size 计入），保证单调。

### 1.4 结果与错误
```json
{ "planId": "8f2c...", "status": "completed", "ok": 1180, "skipped": 12,
  "failed": [{ "dest": "...", "code": "HASH_MISMATCH", "source": "...", "attempts": 4,
               "retryable": false, "message": "sha1 expected ..., got ..." }],
  "bytes": 123456789, "elapsedMs": 43210, "peakBytesPerSec": 52428800, "retries": 17,
  "post": [{ "type": "extract_natives", "ok": true }] }
```
`status ∈ completed | partial | failed | cancelled | paused`；默认可继续跑完其余文件，一次暴露多个问题。错误码见 §9；C++ 映射：1xxx→`E_INVALID`、2xxx→`E_NETWORK`、4xxx→`E_DISK`、5xxx→`E_CANCELLED`、其余→`E_INTERNAL`。

## 2. 并发与调度
### 2.1 三层配额
| 层 | 默认 | 说明 |
|---|---|---|
| 全局 streams | 16（1–64） | 每个 plan 一份（进程内多 plan 尚未共享配额）；plan 的 `concurrency` 就是该额度 |
| 每 host streams | 8（0 关闭） | host = 小写 hostname，不含路径；不同 mirror 是不同 host |
| 每文件 streams | 未单独设限 | 分片与其它文件共用全局/host 配额；per-file cap 仍是待补项（12 §5） |

**不要嵌套 Semaphore 后各自 await**：持有全局许可等 host 会浪费配额。用单一 `Mutex<SchedState>` + `Notify` 的 permit broker，临界区只做计数/唤醒，每 4 MiB 才进入一次：
```rust
loop {
  let mut s = state.lock().await;
  if s.global < s.max_global && s.host(host) < s.max_host && s.plan(pid) < s.max_plan {
    s.global += 1; s.host_inc(host); s.plan_inc(pid); break;
  }
  let n = s.notify.notified(); drop(s); n.await; // 避免丢唤醒
}
```
释放时递减并 `notify_waiters()`；plan 间按 `priority`（小者先）+ 轮转，避免大安装饿死小任务；文件 `priority` 只在所属 plan 内排序。同优先级内 >4 MiB 的 bulk 按大小降序启动（减少尾延迟），<1 MiB 的 small 保留 1/4 全局许可（让 UI 快速出现“已完成 N 个”）。这是启发式，按 §11 调参。**抢占**只停止派发新 chunk，不 abort 在途大 chunk，否则重传成本高于调度收益。

### 2.2 带宽限速（令牌桶）
全局 `BandwidthLimiter`：rate bytes/s，容量 `max(1 MiB, rate*0.2)`；每 64 KiB 读之前 `acquire(64 KiB)`，不足则 `sleep_until`，**先拿令牌再从 socket 读**，让 TCP/H2 流控形成背压，避免内存膨胀。低速（<2 MB/s）用 16 KiB 粒度，避免“突发—停顿”；高速用 64 KiB 减少 await。可选每 plan 桶；不要直接用 `governor` 的 per-second cell 表达字节/秒（单位不匹配，除非把 cell 定义为 64 KiB）。限速时把每文件 streams 降到 2–4：并行流只增开销，不增总带宽。

### 2.3 HTTP/1.1 vs HTTP/2、Range
**[事实]** HTTP/1.1 一条连接同时只有一个在途请求；HTTP/2 一条连接多路复用多个 stream。实现建两个 client（超时、连接池、代理、边缘地址等设置完全相同，只差协议协商）：`auto` 走 ALPN（`h2` 优先，服务器不支持时 `http/1.1`），`http1` 固定 HTTP/1.1；连接池按 scheme/host/port 复用（`ClientBuilder` 文档）。详见 `13-transport-protocol-fallback.md`。

| 场景 | 策略 |
|---|---|
| 大量小文件（assets，<1 MiB） | 不用 Range；`auto` client 经 ALPN 协商（h2 优先、h1.1 兜底）+ 连接池；全局 16（可调 1–64），per-host 8 |
| 1–8 MiB | 单流顺序；不做 Range 探测（省 1 RTT） |
| 8–32 MiB | 超过 `rangeThreshold` 后按 `chunkSize`（4 MiB）切分 → 2–8 个分片 |
| 32–256 MiB | 同样固定 4 MiB → 8–64 个分片；per-file cap 未实现，大文件会占满全局配额 |
| >256 MiB | 分片数随文件线性增长（1 TiB ≈ 26 万片）；动态块大小/上限见 12 §5 |
| 不支持 Range / size 未知 | 单流；当前不做续传（分片才用 `.part.meta`），重启从头覆盖 |
| 高丢包/高 RTT | **[推断]** H1 多连接可能优于 H2（H2 仍有 TCP HOL），需 A/B；当前由 per-origin 协议降级兜底（见 13） |

> 资源/模组计划使用单独的 `DownloadProfile.Resource`：小文件仍单流，但单文件 >512 KiB 会主动分片，多文件计划的默认 per-host 上限提升到 16；这是为了改善大量小 mod 的延迟与尾部吞吐，不复用到游戏资源计划。

**分片固定 HTTP/1.1**：所有 Range/chunk 请求都走 `http1` client（`http1_only()`），一个 chunk 独占一条 TCP 连接，避免单连接流控与 TCP HOL 压低聚合吞吐；单流文件才用 `auto`。协议类失败（421、h2 响应上的 505、连接/响应头阶段的 h2 连接级错误）会把该 origin 降级到 `http1` 600s，并对零字节的尝试立即用 h1 重试一次；`edge.rs` 的边缘探测保持 h1。

**分片参数有边界**：`chunkSize` 只接受 64 KiB–64 MiB（0 关闭 Range），单文件最多 1024 片；文件更大时按 `size / 1024` 放大块大小，而不是继续增加请求与任务数。计划校验在 `plan.rs`，不要只依赖 C# 生产者的 clamp。

**资源/模组计划**：C# 的 `DownloadProfile.Resource` 将 chunk/range 上限压到 512 KiB，并在多文件计划沿用默认 per-host 8 时提升到 `min(concurrency,16)`（显式更低值保留）；同时使用 `fast` durability，避免大量小 mod/config 每个文件一次 fsync。Rust 对最后一段 ≥512 KiB 的 range 再拆成最多 8 个尾分片，`tailParts` 写入 `.part.meta` 并在续传时重建，解决下载末尾只剩一个连接的问题。

**探测**：`GET` + `Range: bytes=0-0`；仅当 `206` 且 `Content-Range: bytes 0-0/总长` 才认为支持。`200` 表示忽略 Range，立即 drop body。不要用 HEAD 判断 Range（CDN 的 HEAD/GET 行为可能不同）。**小文件合并**：不做 HTTP/1.1 pipelining，也不把多文件拼成一个请求；协议层没有安全的“合并”，只能靠 `auto` client 协商出的 H2 多路复用，或由 C# 生成“一个 zip + extract”的计划（另一计划，不是下载器职责）。**Range 与压缩**：固定 `Accept-Encoding: identity`，禁用 reqwest gzip/brotli/deflate/zstd；若响应仍带 `Content-Encoding`，直接 `ENCODING_UNSUPPORTED`，否则字节范围与哈希都会错。

### 2.4 超时
| 项 | 默认 | 实现 |
|---|---|---|
| DNS+TCP+TLS | 10s | `connect_timeout(10s)`；单独 DNS 超时仅在 hickory 时设置 |
| 响应头/TTFB | 20s | `timeout` 包住 `send()` 到 headers |
| 读空闲 | 30s | `read_timeout(30s)`，每次成功 read 重置 |
| 单 chunk 总时长 | 5min | `chunk_size/min_speed` 与固定上限取小 |
| 整个 attempt | 30min | 防止极慢连接永久占许可；大文件正常远低于此 |
| 重定向 | 5 跳/30s | `Policy::limited(5)`，逐跳校验 scheme |

不要给 `ClientBuilder::timeout` 设全局值：它包含整个 body，会杀死正常大文件。用 `read_timeout` + 外层 watchdog。

## 3. 可靠性
### 3.1 分类与重试
| 类别 | 例子 | 同源重试 | 换源 | 备注 |
|---|---|---|---|---|
| 瞬时网络 | DNS 临时失败、连接重置、TLS 握手超时、body 截断 | 是（3 次） | 是 | TLS 证书错误不重试 |
| 限流 | 429，或 503+`Retry-After` | 是 | 是 | 尊重秒/HTTP-date，上限 120s；超过则冷却源并换源 |
| 服务端 | 500/502/503/504/52x | 是（4 次） | 是 | 指数退避 |
| 客户端 | 400/401/403/404/410/451 | 否 | 是 | 403 可能签名 URL 过期；Rust 不刷新，换源或 `URL_EXPIRED` 让 C# 重生成 |
| Range | 206 的 `Content-Range` 不匹配、416 | 是（降级单流） | 是 | 不能把 200 当 206 继续写 |
| 完整性 | 大小不符、哈希不符 | 哈希不符不重试同源 | 是 | 记录 `(host, hash)` 黑名单，本会话不再用 |
| 本地 | 磁盘满、权限、占用、路径过长 | 否（占用可短重试） | 否 | 立即终止，避免无意义重试 |

**退避**：`min(30s, 500ms*2^(attempt-1)) + rand(0..250ms)`；429/503 优先 `Retry-After`。每源最多 4 次，整文件最多 12 次（含换源），全局预算 `3*file_count`，防止镜像异常导致风暴。**镜像 fallback**：初始顺序 = `url`+`mirrors`（尊重 C# 偏好）；失败后按健康分重排 `score = success_rate/(1+ewma_latency_s)`，冷却源排最后。哈希不符：删 `.part`，把源加入 `(hash, host)` 黑名单，换下一个；全失败返回 `HASH_MISMATCH`，结果带每源 actual hash，便于 C# 判断脏镜像。不同源必须字节一致；C# 不能保证时只给一个源。只做被动健康统计，不做主动测速（HMCL 的镜像测速是产品功能，不是每任务都该做）。**磁盘满**：启动前 `fs4::available_space(root)` 检查 `totalBytes*1.05+64 MiB`（去重命中先扫描抵扣），不足 `DISK_FULL`；运行中 `ENOSPC`/Windows 112/39：停该 plan 所有写入，删当前未完成 `.part` 释放空间，不重试。**权限/占用**：`PermissionDenied`、Windows 5 → `PERMISSION_DENIED`；Windows 32/33（共享/锁冲突）短重试 5 次（100ms 起），仍失败 `FILE_LOCKED`，提示关闭游戏/杀软扫描，绝不删除已有 dest。

## 4. 落盘
### 4.1 目录、写入、提交
```
<dest 所在目录>/client.jar.part        # 数据，必须与 dest 同卷
<dest 所在目录>/client.jar.part.meta   # JSON：hash/size/etag/bitmap/validator
<dest 所在目录>/client.jar.part.lock   # fs4 排他锁（sidecar，rename 后不丢）
```
`.part` 必须放 `dest.parent()`，不要放系统临时目录：跨卷 rename 会 `EXDEV`，只能 copy，失去原子性；`tempfile` 文档也明确“不能跨文件系统 persist”。长文件名接近 255 时用 `.dl-<hash8>.part` 短名，提交时再 rename。`part.meta` 用 `meta.tmp`→`fsync`→`rename`，损坏则忽略重下。
```rust
create_dir_all(parent); let _lock = lock_exclusive(part_lock)?;
let f = OpenOptions::new().read(true).write(true).create(true).truncate(false).open(&part)?;
f.set_len(size)?;                                  // 允许随机写；Unix 稀疏不占满
for chunk in missing_chunks(&meta) {
  let bytes = download_range(url, chunk, validator).await?;
  f.write_at(&bytes, chunk.start)?;                // pwrite / seek_write
  if chunk.done { meta.mark(chunk); checkpoint(&f, &meta).await?; }
}
f.sync_all()?; drop(f);
let actual = hash_file(&part, algo).await?;
if actual != expected { remove(part); return Err(HASH_MISMATCH); }
fs::rename(&part, &dest)?;                         // Windows: MoveFileExW/FileRenameInfoEx
sync_parent_dir(&dest_parent)?;                    // Unix only
remove(part_lock);
```
**checkpoint**：每 8–16 MiB 或 2s：`sync_data()` 数据 → 更新 bitmap → `meta.tmp` 写 + `sync_all` → rename。崩溃最多丢一个 checkpoint；未标记 chunk 重下。**哈希**：SHA-1 是顺序摘要，分片乱序无法并行合并；多分片/续传统一在提交前顺序读 `.part`（1 GiB 约 0.5–1s，页缓存命中），单流可选边下边算。**顺序模式（未实现）**：单流/无 Range 本可用 `contiguous_len` 续传（`Range: bytes=contiguous_len-` + `If-Range`）；当前单流失败后从头覆盖 `.part`，只有分片模式有 `.part.meta` 断点。**分片模式**以 chunk 为完成单位，未完成 chunk 整块重下，避免半 chunk 边界。**fsync 取舍**：`full` 做 `sync_all`+父目录 fsync；`normal`（默认）只在提交前 `sync_all`，checkpoint 用 `sync_data`；`fast` 不 fsync（游戏文件可重下）。macOS 真正落盘需 `fcntl(F_FULLFSYNC)`，`File::sync_all` 只到 `fsync`（平台差异，需 libc）。**Windows 原子替换**：`std::fs::rename` 文档说明会替换已存在文件，底层 `MoveFileExW`（并可能用 `FileRenameInfoEx`）；`tempfile::persist` 也承诺“存在则原子替换”，但不做 sync。dest 被占用时 rename 返回共享冲突，重试 5 次（100/200/400/800/1600ms），仍失败保留 `.part` 并报 `FILE_LOCKED`。**长路径**：传统 `MAX_PATH=260`，扩展上限 32,767，需 `\\?\`（UNC 用 `\\?\UNC\`）；`\\?\` 下不能有 `/`、`.`、`..`，必须先规范化再前缀；显示/传给 Java 时去前缀（`dunce::simplified`）。Rust std 对部分长路径自动加前缀，但不要依赖。**杀软**：Defender 可能短暂持有新文件；共享冲突按上重试，不要用共享标志绕过；提示加排除项是产品选择。**跨盘**：内容库可在另一卷，但 `.part` 永远在目标卷；root 是 junction 时 canonicalize 后比较卷序列号（Windows）或 `statfs` 的 `f_fsid`（Unix），不一致禁用硬链接。

### 4.2 断点与 validator
优先强 ETag；弱 ETag（`W/`）不能用于 `If-Range`（RFC 9110 要求强比较）；无强 ETag 用 `Last-Modified`（1s 粒度）。恢复请求 `Range: bytes=<n>-` + `If-Range: <strong-etag|last-modified>`：`206` 且 `Content-Range` 起点/终点/总长匹配→继续；`200`→服务端忽略或资源变了，截断从 0 开始；`416`→`.part` 长度等于 size 则进入校验，否则丢弃；validator 变化→丢弃，不拼接。多分片恢复：meta 保存 `chunk_size`+bitmap，每 chunk 独立 Range，响应必须 206 且 `Content-Range` 精确；任何一个 200 就退出分片，整文件重下。`.part.meta` 记录 `dest/hash/size/etag/last_modified/chunk_size/bitmap/source/updated_at`，全部匹配才复用；`planId` 变了但 `(dest,hash,size)` 相同也可复用。

### 4.3 路径安全
规范化后检查 `dest.starts_with(root)`；每级父目录 `symlink_metadata`，拒绝 reparse point/symlink 逃逸。TOCTOU 彻底做法是 Unix `openat(O_NOFOLLOW)`、Windows `FILE_FLAG_OPEN_REPARSE_POINT` 逐级打开；MVP 先 canonicalize+父目录检查，威胁模型是本地误操作而非提权。拒绝 dest 指向内容库内部、`.part/.meta/.lock` 自身、计划重复项。若未来增加解压型 post，必须同样检查绝对路径、`..`、symlink entry、超长路径、解压总量/压缩比（zip bomb）；v1 不提供。

## 5. 去重
内容库 `<data>/downloads/objects/<algo>/<hex[0:2]>/<hex>`；只在哈希校验通过后写入。索引可用文件系统本身（路径即索引），MVP 不引入 SQLite（架构规定只有 C# 写 SQLite）；若未来要引用计数，可由 C# 暴露只读查询或 Rust 维护独立只追加日志，不要双写同一库。提交优先 `std::fs::hard_link(object,dest)`；失败：跨卷（Unix `EXDEV`，Windows `ERROR_NOT_SAME_DEVICE`）、文件系统不支持（FAT/exFAT/部分网络盘）、权限/重解析点→复制或 `PERMISSION_DENIED`。**符号链接**：Windows 需 SeCreateSymbolicLinkPrivilege 或开发者模式，且部分反作弊/Java 对 symlink 不友好；默认禁用，仅显式 `mode:"symlink"` 且高级选项开启。**硬链接风险**：共享 inode，任何一方原地修改影响所有实例；只对不可变 artifact（jar/assets/libraries/native jar）去重，配置/日志/可编辑 Mod 一律复制。**跨盘**：`std::fs::copy`（Windows `CopyFileEx`，Linux 可能 `copy_file_range`）；可选 reflink（Linux `FICLONE`/Windows block clone/macOS `clonefile`），失败回落复制，**[未验证]** 需矩阵测试。**GC**：对象 `nlink<=1` 且 `last_used>30 天` 可删。Unix `MetadataExt::nlink()` 稳定；Windows `number_of_links()` 目前是 nightly `windows_by_handle`，稳定版需 `windows-sys` 调 `GetFileInformationByHandle`。GC 与下载并发用全局读写锁：GC 持写锁，提交/链接持读锁；删除失败（占用）跳过。**哈希碰撞**：Mojang 用 SHA-1，SHAttered 证明故意碰撞可行；去重键包含算法，SHA-1 对象只与同 SHA-1 计划共享；用户 Mod 若 C# 能提供 SHA-256，优先用 SHA-256 做键，避免“恶意文件借碰撞复用可信对象”。

## 6. 性能
### 6.1 reqwest/tokio 配置（reqwest 0.13，MIT/Apache-2.0）
```rust
// 两个 client 共用同一份基础设置；只有协议协商不同。
fn base() -> reqwest::ClientBuilder {
  reqwest::Client::builder()
    .user_agent("ArchoeraCX/<ver> (+https://...; contact)")
    .connect_timeout(Duration::from_secs(10)).read_timeout(Duration::from_secs(30))
    .tcp_keepalive(Duration::from_secs(15)).tcp_nodelay(true)
    .pool_idle_timeout(Duration::from_secs(30)).pool_max_idle_per_host(8)
    .no_gzip().no_brotli().no_deflate().no_zstd()
    // 实际实现为自定义策略：≤5 跳，逐跳校验 scheme 白名单
    .redirect(reqwest::redirect::Policy::limited(5))
}
let auto = base()                // 小文件/单流：ALPN 协商，h2 优先
  .http2_keep_alive_interval(Duration::from_secs(20))
  .http2_keep_alive_timeout(Duration::from_secs(10)).http2_keep_alive_while_idle(true)
  .build()?;
let http1 = base().http1_only().build()?;  // 分片/大文件：一 chunk 一连接
```
TLS：reqwest 0.13.5 的 `default-tls` feature 解析为 **rustls + `rustls-platform-verifier`（系统信任库，含企业根）+ aws-lc-rs**，不再按平台切 native-tls；`Cargo.lock` 中没有 `native-tls`/`hyper-tls`。**[未验证]** reqwest 0.13 自动系统代理在各平台的行为（尤其 PAC/WPAD）需实测；C# 已解析的代理 URL 应显式传给 Rust，避免两套行为。DNS：默认 `getaddrinfo` 走阻塞线程池，连接池摊薄成本；可选 `hickory-dns` 异步+缓存，但**[未验证]** 是否遵循 Windows NRPT/企业 DNS，MVP 先用默认。**缓冲**：`response.chunk()` 返回 `Bytes` 直接 `write_all`，读缓冲 64–256 KiB/stream；不要 `response.bytes()`（整文件进内存）；分片用 `write_at`，不要 `seek+write`（共享句柄竞态）。**写盘线程**：`tokio::fs::File` 每次操作走 `spawn_blocking`，16 stream×64 KiB 可能产生大量 blocking 任务；先 benchmark，若 sys CPU>10% 或 p99 抖动，改“每文件一个 writer 线程+有界 `mpsc<Bytes>`”，writer 用 `std::fs::File::write_at`，队列 2–4 个 chunk 背压；`max_blocking_threads(64)` 防止默认 512 堆积。**零拷贝**：HTTPS 下 TLS 加解密决定不存在端到端零拷贝；`sendfile/splice` 只适用明文。能做的是避免用户态二次拷贝（`Bytes` 直接写）；文件复制才可能 `copy_file_range`/reflink。**进度成本**：每 64 KiB 一次 `fetch_add`+150ms 快照，10k 文件约 5–7 事件/s；不要在 chunk 里 `serde_json`、`Instant::now()`、`tracing::info!`。**内存上限**：streams×(chunk_buffer+inflight Bytes)≈16×128 KiB≈2 MiB；计划解析是主要占用（200k 文件约 40–80 MiB）；URL/dest 用 `Arc<str>` 去重，mirror 共享；峰值 RSS<256 MiB。

### 6.2 连接池
H1：`pool_max_idle_per_host`≥每 host 并发+1，否则频繁建连；`pool_idle_timeout=30s` 平衡 NAT 与复用。H2（仅 `auto` client）：一条连接多 stream；可选 `http2_adaptive_window` 扩高 BDP 窗口（当前未设置，等基准）；`keep_alive` 防 NAT/防火墙静默断链；服务端 `SETTINGS_MAX_CONCURRENT_STREAMS` 可能 100，仍受 per-host 8 限制。Rust 只做文件下载，不要复用 C# 的 HttpClient。

## 7. 与现有实现对比（只读）
| 项目 | 语言/许可证 | 并发 | Range/分片 | 断点/validator | 落盘/原子性 | 镜像/重试 | 去重 |
|---|---|---|---|---|---|---|---|
| **Prism Launcher**（2026-10-24 HEAD，只读行为） | C++/GPL-3.0 | `NetJob`/`ConcurrentTask`，默认 6 个并发（设置可调） | **无 Range**，每文件一个 GET 流 | `HttpMetaCache` 保存 ETag/Last-Modified；无跨进程续传 | `PSaveFile` 临时文件，校验后提交；不做逐文件 fsync | 整个 job 失败后最多重试 3 次，404 不重试；记录失败 URL 并弹出手动重试 | 无内容库去重 |
| **CmlLib.Core** | C#/MIT | TPL Dataflow：checker 1–4，downloader ≥8（`Math.Max(8, cores)`），队列 2048 | **无 Range**，整文件流 | 无；启动时按文件 SHA-1 全量校验 | 直接 `File.Create(dest)`；取消留下半文件；无 temp/fsync | 单 URL（未见 mirror fallback） | 无 |
| **ProjBobcat** | C#/MIT | 共享连接 gate；每文件自适应 parts（最小 1 MB，最多 16） | `Range:0-0` 探测；分段 `RandomAccess.Write` | **无跨进程续传**；无 ETag/If-Range | `.<name>.<guid>.download` 在目标目录；校验后 `File.Move(...,true)`；无 fsync；失败删 temp | 多源按权重；重试 200ms×2^n 上限 3s+jitter；8s header/12s stall；100ms 聚合 | 无 |
| **HMCL** | Java/GPL-3.0 | 全局 `Semaphore`，默认 `min(cores*4,64)` | **单流**，无并行分片 | **有**：强 ETag/Last-Modified+`If-Range`；416/200 回退 | `Files.createTempFile`（系统 temp）→校验→`Files.move(REPLACE_EXISTING)`；跨卷有风险；无显式 fsync | 候选 URL 顺序；默认 5 次、200ms 固定；connect/read timeout；ETag 缓存 | 内容缓存仓库（按 hash 查已有文件），非硬链接 |
| **XMCL** | TS/MIT | undici `ConcurrencyDispatcher` 全局 active 限制 | 阈值 5 MiB、4 并发；首请求 `0-(threshold-1)`，再拆子 range | 单流可 Range 续传；无持久 bitmap/ETag | 直接打开 dest fd；失败 truncate/unlink；无 temp/fsync/原子 rename | 多 URL fallback；undici retry；BMCLAPI 自适应 controller：TTFB/stall/slow 重掷、熔断、host 信誉 | 无（未见通用内容去重） |
| **Modrinth App** | Rust/GPL-3.0 | `FetchSemaphore(max_concurrent_downloads)`+独立 `IoSemaphore` | **无 Range**；`bytes_stream` 顺序读 | 无；每次重下；hash 不符重试 2 次 | `tempfile::NamedTempFile::new_in(staging)`；SHA-512 后 `sync_all`；TempPath drop 清理；内容库发布有锁 | 多镜像顺序；connect 15s/read 30s/keepalive 10s；429 API 限流、熔断 | **有**：SHA-512 内容库+SQLite 元数据+`fs4` 进程锁；硬链接/reflink 代码在该 commit 被注释掉 |

结论：CmlLib 最简单但缺 Range/续传/原子落盘；ProjBobcat 分片与重试最接近本设计，但无跨进程续传、validator、fsync、去重（“temp 在目标目录”值得借鉴）；HMCL 的 validator 最严谨但单流吃不满高速链路、系统 temp 跨卷有隐患；XMCL 的慢镜像重掷/熔断最实用但直写 dest、无原子提交；Modrinth 的 temp+内容库+进程锁最工程化但完全无 Range/断点且 GPL 不可复制；Prism Launcher 刻意不做 Range，每文件一个 GET、默认 6 路并发，靠整任务重试与 HttpMetaCache 保证完成。本设计保留大文件分片，但把默认阈值提高到 8 MiB、分片 4 MiB，并让每 host 默认回到 8 路，避免 5k 级资源安装时的请求/TLS 握手风暴。

## 8. 模块、结构体、伪代码
```
archoera_dl/src/{lib,plan,scheduler,limiter,http,file,hash,dedup,post,event,error}.rs
```
`lib.rs` = C ABI + `catch_unwind` + 单例 runtime；`scheduler` = permit broker/优先级；`http` = client/探测/Range/重试/错误映射；`file` = `.part/.meta`/checkpoint/commit/锁；`dedup` = objects/hardlink/copy/GC；`post` = chmod/remove/copy（v1 不执行 extract_natives）；`event` = 固定结构体+150ms 聚合。
```rust
pub struct Engine { client: reqwest::Client, sched: Arc<Scheduler>, objects: Arc<ObjectStore> }
pub struct PlanTask { id: u64, plan: Arc<Plan>, cancel: CancellationToken, state: AtomicU8 }
pub struct FileJob { idx: usize, url: Url, mirrors: Vec<Url>, dest: PathBuf,
  hash: HashSpec, size: u64, mode: Mode, priority: i32, progress: Arc<FileProgress> }
pub struct PartMeta { version: u32, dest: String, hash: HashSpec, size: u64,
  etag: Option<String>, last_modified: Option<String>, chunk_size: u64,
  bitmap: Vec<u64>, source: String, updated_at: i64 }
```
```rust
async fn commit(job: &FileJob, part: &Path, meta: &PartMeta) -> Result<()> {
  let f = File::open(part)?; f.sync_all()?; drop(f);
  let actual = hash::file(part, job.hash.algo).await?;
  if actual != job.hash.hex { fs::remove_file(part)?; return Err(Error::HashMismatch { actual }); }
  if job.mode == Mode::Link { objects::publish_and_link(part, &job.hash, &job.dest).await?; }
  else { fs::rename(part, &job.dest)?; sync_parent(&job.dest)?; }
  fs::remove_file(meta_path(part)).ok(); Ok(())
}
```
C ABI 与 07 的 method+JSON 一致：`download.submit/pause/resume/cancel/snapshot`；`submit` 立即返回 `task_id`，内部只建任务不阻塞；所有导出 `catch_unwind`，panic→`INTERNAL`。post 在全部文件完成后按数组顺序执行，v1 只支持 `chmod/remove/copy`；`extract_natives` 由 C# 在 continuation 中执行；未知类型 `UNSUPPORTED_POST`。

## 9. 错误码
| 码 | 名称 | 重试/换源 | 用户动作 |
|---|---|---|---|
| 0 | OK | - | - |
| 1001/1002/1003/1004/1005/1006 | INVALID_PLAN/PATH_ESCAPE/INVALID_HASH/DUPLICATE_DEST/UNSUPPORTED_SCHEME/PLAN_TOO_LARGE | 否/否 | 报告 C# 生成器、检查路径 |
| 2001/2002/2003/2004/2005 | DNS/CONNECT/TLS/TIMEOUT_CONNECT/TIMEOUT_READ | 除 TLS 外可重试、可换源 | 检查网络/代理/系统时间 |
| 2006/2007/2008 | HTTP_4XX/HTTP_429/HTTP_5XX | 4xx 不重试但换源；429/5xx 可重试换源 | 403 可能 URL 过期 |
| 2009/2010/2011/2012/2013 | RANGE_UNSUPPORTED/SIZE_MISMATCH/REDIRECT_LOOP/PROXY_AUTH/ENCODING_UNSUPPORTED | 内部降级/可换源/否/否/换源 | 检查镜像或代理 |
| 3001/3002 | HASH_MISMATCH/EMPTY_FILE | 不重试同源、换源 | 全失败则报告脏镜像 |
| 4001/4002/4003/4004/4005/4006/4007 | DISK_FULL/PERMISSION_DENIED/FILE_LOCKED/PATH_TOO_LONG/CROSS_DEVICE/DEST_IS_DIR/READ_ONLY | 占用短重试；跨卷内部复制；其余否 | 清盘/换目录/关游戏/缩短路径 |
| 5001/5002/5003/5004 | CANCELLED/PAUSED/SHUTDOWN/DEST_BUSY | 否/否/否/可等待 | 用户操作 |
| 6001/6002/6003 | POST_FAILED/ZIP_SLIP/UNSUPPORTED_POST | 否 | 看详情/拒绝恶意包/C# 实现 |
| 9001 | INTERNAL | 否 | 收集日志/minidump |

## 10. Crate 选型
| crate | 用途 | 许可证 |
|---|---|---|
| `tokio` 1.x / `tokio-util` | runtime/fs/time/sync、CancellationToken | MIT |
| `reqwest` 0.13.x | HTTP/1.1、HTTP/2、TLS、代理 | MIT OR Apache-2.0 |
| `hyper` 1.x / `http` 1.x / `bytes` 1.x | 底层类型（通常不直接用） | MIT / MIT OR Apache-2.0 / MIT |
| `futures-util` / `serde` / `serde_json` | stream/select、计划/结果/meta | MIT OR Apache-2.0 |
| `sha1` / `sha2` / `httpdate` / `rand` | 校验、Retry-After、jitter | MIT OR Apache-2.0 |
| `tempfile` 3.x / `fs4` 1.x | post 临时目录、文件锁/可用空间/预分配 | MIT OR Apache-2.0 |
| `thiserror` / `tracing` / `dunce` | 错误、日志、Windows 路径 | MIT / MIT / MIT OR Apache-2.0 |
| `zip`（可选）/ `windows-sys`（可选） | extract_natives、nlink/卷序列号/长路径 | MIT / MIT OR Apache-2.0 |
| `rustls`（经 reqwest `default-tls`）/ `hickory-resolver`（可选） | TLS、系统证书、异步 DNS | Apache-2.0 OR ISC OR MIT / MIT OR Apache-2.0 |

不选 `fs2`（停止维护，`fs4` 是其分支）；不选 `governor` 做字节限速（单位不匹配，除非自定义 cell）；不选 `sled`（维护状态/体积不适合），去重索引先用文件系统路径。CI 用 `cargo deny` 锁定许可证。

## 11. 基准与测试
本地源用 `axum`/`hyper`：固定伪随机内容（可复算 hash），支持 Range/ETag/Last-Modified/Content-Length，可注入忽略 Range、错误 `Content-Range`、429+`Retry-After`、500、慢速、N 字节断开、错误 hash、chunked、gzip。真实网络：Linux `tc netem`（20/100ms RTT，0.1%/1% 丢包，50/200 Mbps）；Windows 用 `clumsy`/NetLimiter 手动矩阵。回环测上限，netem 测策略。
矩阵：1×1 GiB；1000×1 MiB；10000×64 KiB；混合 1 GiB+1000×1 MiB+10000×64 KiB；并发 4/8/16/32；per-host 2/4/8；Range 开/关；H1/H2；限速 0/10/50/100 Mbps；NVMe/SATA；Defender 开/关。指标：墙钟、goodput、p50/p95 TTFB、p99 文件延迟、CPU user/sys、峰值 RSS、磁盘写 MB/s、syscall（`strace -c`/ETW）、fd 数、重试数、事件数、hash 耗时。工具：`criterion`（hash/令牌桶/路径校验）、`hyperfine`（CLI）、`dhat`/`heaptrack`、`tokio-console`、`perf`/WPA、`blktrace`。验收目标（**[推断]**，实测校准）：1 GiB 回环≥700 MB/s；10k×64 KiB≥800 files/s（32 并发）；峰值 RSS<256 MiB；进度 CPU<2%；取消<200ms；续传重下≤1 chunk。
必测：1) 50% `kill -9` 后重启只补缺失 chunk；ETag 变化必须从头。2) 探测 206、后续 Range 返回 200/错误 `Content-Range`，必须降级或失败，不能写错位。3) 主源 200 但字节错，hash 失败换 mirror 成功并记录。4) `Retry-After` 秒/HTTP-date 两种；>120s 冷却换源。5) 小 tmpfs ENOSPC：删 `.part`、`DISK_FULL`、不破坏已有 dest。6) 只读目录；Windows 另一句柄独占 dest→`FILE_LOCKED`+重试。7) chunk 中间取消/暂停，meta 可解析、恢复 hash 正确、无残留锁。8) >260 字符/`CON`/`a:b`/尾随点→`PATH_TOO_LONG`/`INVALID_PLAN`。9) `../`、绝对路径、symlink/junction 逃逸，拒绝且不建文件。10) 同 hash 两实例硬链接；跨卷复制；删一实例后 GC 不删仍被引用的对象；Windows nlink。11) zip slip/symlink entry/zip bomb/META-INF 排除，required/optional 行为。12) 两 plan 写同一 dest：`DEST_BUSY` 或排队；全局/每 host 配额不超。13) 随机杀进程/断电 100 次：要么旧文件完整，要么 `.part` 可续，绝不出现半截 dest。14) 200k 文件计划+10k 任务，RSS 不随文件数线性爆炸，无整文件 `bytes()`。

## 12. 风险与参考
| 风险 | 级别 | 处理 |
|---|---|---|
| reqwest 0.13 系统代理/PAC 与 C# 不一致 | 中 | C# 显式下发代理；PAC 由 C# 解析；Rust 只接受 URL |
| 杀软/索引器持有新文件 | 高 | rename 重试+`FILE_LOCKED`；文档建议排除目录 |
| `\\?\` 路径传给 Java/外部进程 | 中 | 内部 verbatim，边界转普通路径，统一封装 |
| 硬链接被原地修改 | 中 | 只对不可变 artifact 去重；配置/Mod 复制 |
| SHA-1 碰撞/脏镜像 | 中 | 算法入键；SHA-256 优先；hash 失败换源记录 |
| 分片来自不同 CDN 边缘 | 低 | 一文件一源；mirror 必须字节一致；hash 兜底 |
| H2 在丢包链路的 TCP HOL | 中 | 基准对比 H1；已实现按 host 降级到 `http1`（13 §5） |
| fsync 尾延迟 | 中 | 默认 `normal`；checkpoint 16 MiB；可配置 |
| 200k 文件计划内存 | 低 | 上限+`Arc<str>`+分页 snapshot；必要时流式解析 |

参考：reqwest `ClientBuilder`（0.13.5，`read_timeout`/`pool_*`/`http2_*`/`no_gzip`/系统代理）<https://docs.rs/reqwest/latest/reqwest/struct.ClientBuilder.html>；`tempfile::NamedTempFile::persist`（原子替换、不可跨文件系统、不负责 sync）<https://docs.rs/tempfile/latest/tempfile/struct.NamedTempFile.html>；`std::fs::rename`/`hard_link` <https://doc.rust-lang.org/std/fs/fn.rename.html>、<https://doc.rust-lang.org/std/fs/fn.hard_link.html>；Windows 路径限制 <https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation>；`fs4` <https://docs.rs/fs4/latest/fs4/>；RFC 9110 §13.1.5/§14.2/§15.3.7 <https://www.rfc-editor.org/rfc/rfc9110>；HMCL `FetchTask`/`FileDownloadTask`（`8827fd0`）；ProjBobcat `DownloadHelper`（`57532e2`）；XMCL `packages/file-transfer`（`75a52fa`）；Modrinth `packages/app-lib/src/util/fetch.rs`（`7c5c1b5`）；CmlLib `ParallelGameInstaller`/`HttpClientDownloadHelper`（`ffa2e90`）。
