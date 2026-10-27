# 12 · 多连接下载的调度原理（只读研究）

> 目的：把 FDM、aria2 这类下载器的**机制**抽象成可复用的原理，用来判断 `archoera_dl` 该改什么、不该改什么。
> 方法：只读源码与官方手册，记录数据结构、决策条件与默认参数；**不复制任何代码、注释或标识符**。
> 引用基线：aria2 `master`（2026-10-24，GPLv2+）、FDM 3.x SVN 快照 `r58`（`svn2github/FreeDownloadManager`，GPL）。
> NDM（Neat Download Manager）**闭源且没有公开仓库**，只能观察外部行为，本文不作设计依据。
> 标记：**[事实]** 源码/手册可直接核对；**[推断]** 基于协议与实现的判断；**[未验证]** 需要本项目基准确认。

---

## 0. 结论速览

| # | 结论 | 依据 |
|---|---|---|
| 1 | 两种成熟模型：**FDM = 动态二等分（ramp-up）**，**aria2 = 固定 piece + 稀疏领取（pull）**。二者都不预生成全部下载任务。 | [事实] §1、§2 |
| 2 | 分段有**硬下限**：FDM 默认最小段 300 KiB，且剩余量不足 100 KiB 不再切；aria2 `--min-split-size` 默认 20 MiB（下限 1 MiB）。 | [事实] `fsAppSettings.cpp:122`、`fsInternetDownloader.cpp:268`、aria2 手册 |
| 3 | **每个连接独立测速**，并把“慢”当成一等失败：FDM 慢于起始均速 1/3 持续数分钟就重启该段；aria2 低于 `--lowest-speed-limit` 直接 abort 重试。 | [事实] `vmsSpeedTracker.cpp:20`、`DownloadCommand.cc:302` |
| 4 | 断点状态是**位图 + 区间**，检查点**定时**写（aria2 默认 60 s），不是每完成一块全量重写。 | [事实] `--auto-save-interval`、FDM `RestoreSectionsState` |
| 5 | 并发是**多层配额**：全局连接数、每服务器连接数、每文件段数，三者互相独立。 | [事实] 两边都有；§3 |
| 6 | 选源（mirror）与分段是**两个反馈环**：FDM 每段绑一个镜像并每 60 分钟重测；aria2 用 `feedback`/`adaptive` 选择器维护服务器历史速度。 | [事实] `fsDownloadMgr.cpp:60`、`FeedbackURISelector.cc` |
| 7 | 我们与它们的差距主要在**段的生命周期管理**（领取、限速、检查点、尾延迟），不在“线程数”。 | [推断] §4 对照表 |

一句话：多连接下载器的“快”，来自**工作守恒的调度**和**对慢连接的主动放弃**，而不是把文件切得越碎越好。

---

## 1. FDM 3.x：动态二等分

### 1.1 结构

- `fsDownloadMgr`：下载任务状态机（`DS_DOWNLOADING / DS_NEEDADDSECTION / DS_NEEDRESTARTFROM …`），单线程轮询，每轮 `Sleep(100)`（`fsDownloadMgr.cpp:195-360`）。
- `fsInternetDownloader`：持有 `m_vSections`，每个 section 一个下载线程，区间为 `[uStart, uEnd)`、当前位置 `uCurrent`（`fsInternetDownloader.h`）。
- `fsSpeedMeter` / `vmsSpeedTracker`：1 秒采样、保留最近 5 个样本做滑动平均，用于全局与单段速度（`fsSpeedMeter.cpp`）。

### 1.2 段的生命周期

| 阶段 | 行为 | 条件 |
|---|---|---|
| 启动 | 只建 1 段 | `CreateMainSection`（`fsInternetDownloader.cpp:122`） |
| 加段 | 运行 **1.2 s** 后加第 2 段，之后在段结束/用户操作时继续加 | `fsDownloadMgr.cpp:292-302` |
| 段数上限 | `未完成段数 < uMaxSections`（默认 **10**，硬上限 20） | `IsSectionCanBeAdded`（`:2379-2396`）、`MAX_NOT_COMPLETED_SECTION_COUNT`（`fsInternetDownloader.h:299`） |
| 切割对象 | 选**剩余字节最多**的未完成段 | `:238-265` |
| 切割点 | 该段 `uCurrent + 缓存 + 速度×20 s` 再加**剩余量的一半** | `:240-247, 275-286` |
| 拒绝切割 | `剩余量/2 < 最小段` 或 `剩余量 < 100 KiB` | `:268` |
| 段结束 | 复用停止的段，或新建一段接上 | `fsDownloadMgr.cpp:331-343` |

关键点有两个：

1. **修正当前位置**：`uCurrent + dwCacheLen + uSpeed*20` 把尚未落盘的数据和 20 秒在途量算进去，新段从这之后开始，避免与旧段重叠、也避免“追尾”。
2. **二等分**：每次只把最大的剩余区间对半切，保证各段长度接近；不会出现一个 4 MiB 的碎段和大量 1 GB 长段并存的局面。

### 1.3 慢连接与停滞

- `vmsSpeedTracker`（`vmsSpeedTracker.cpp:20-50`）：
  - 前 **20 个采样**建立“起始平均速度”；
  - 之后若 `当前速度 == 0` 或 `起始平均 / 当前 >= factor`，连续计数；
  - 连续超过 `duration` 秒 → `CheckCurrentSpeedTooLow()` 为真，该段重启。
  - 类内兜底 `factor=3`、`duration=60 s`；应用设置默认 `factor=3`、`duration=3`（分钟，调用处乘 60，`fsInternetDownloader.cpp:612`），部分下载属性路径用 `factor=4`、`1` 分钟（`fsDownloadMgr.cpp:1215-1216`）。
- 全局停滞：整任务速度为 0 且仍有活跃段，持续 **120 s** → 整任务重启（`fsDownloadMgr.cpp:309-321`）。
- 慢段重启只重取**该段剩余区间**，不重下整文件。

### 1.4 落盘、镜像、持久化

- **预占盘**：>100 MiB 且只有 1 段时，后台线程预分配文件长度，期间禁止加段（`fsDownloadMgr.cpp:2057-2061`）。
- **每段一个镜像**：`FindNotBusyMirrorIndex()` 优先选空闲镜像（`:217, 3010`）；镜像速度每 **60 分钟**重测（`:60`）。
- **持久化**：段表（`start/end/current/state/speed`）序列化进任务状态，恢复时 `RestoreSectionsState`（`:1235, 2265-2271`）。不是位图，按区间记录。

### 1.5 默认参数（`fsAppSettings.cpp:112-206`）

| 参数 | 默认 |
|---|---|
| `MaxSections` | 10 |
| `SectionMinSize` | 300 KiB |
| `MaxAttempts` | 20 |
| `RetriesTime` | 5 s |
| `ReserveDiskSpace` | 开 |

---

## 2. aria2：固定 piece + 稀疏领取

### 2.1 数据模型

- 文件按**定长 piece** 组织（HTTP 下载的 piece 长度受 `--split` / `--min-split-size` 约束，**[推断]** 由总长与连接数推导），`BitfieldMan` 用 bitfield 标记完成，`useBitfield` 标记“正在下载”（`BitfieldMan.cc`）。
- `Segment` 是“某个连接当前负责的一段”，`SegmentMan::checkoutSegment` 在连接空闲时分配一块（`SegmentMan.cc:134-188`）。
- **没有“每块一个任务”**：连接数由 `--split`（默认 5）和 `--max-connection-per-server`（默认 1）限制，连接自己循环领块。

### 2.2 块选择（`--stream-piece-selector`）

| 选择器 | 行为 |
|---|---|
| `default` | 找**最大连续缺口**；若缺口前一块已被占用，则从缺口中点取；缺口小于 `minSplitSize` 则本轮不取（`BitfieldMan.cc:244-300`） |
| `inorder` | 从前往后取，适合边下边看 |
| `random` | 随机取 |
| `geom` | 从文件头开始、按几何间隔扩散，减少建连数 |

`default` 的意图：连接尽量分散在互不相邻的大缺口里，**保留后续可分段的余地**，同时减少连接建立次数。

### 2.3 慢连接与超时

- 每次读循环检查 `checkLowestDownloadSpeed()`：当前连接速度 ≤ `--lowest-speed-limit` 就抛可重试异常（`DownloadCommand.cc:302-311`）。
- `--lowest-speed-limit` 默认 0（关闭），需要用户显式开启；这与 FDM “默认就管慢段”是产品取向差异。
- `AdaptiveURISelector` 在源全部失败时**加倍超时**重试，上限 60 s（`AdaptiveURISelector.cc`）。

### 2.4 控制文件与写盘

- `*.aria2` 控制文件保存 bitfield、pieceLength、totalLength；`--auto-save-interval` 默认 **60 s** 定时保存，停止时强制保存。
- `--file-allocation` 默认 `prealloc`；`--disk-cache` 默认 16 MiB，用于合并小块写、减少系统调用与碎片。

### 2.5 选源

- `FeedbackURISelector`：用 `ServerStat` 的历史下载速度选最快的镜像，错误源排在后面。
- `AdaptiveURISelector`：优先未测试的源，轮换测试，失败后加倍超时。
- 两者都把“源的质量”当成**跨任务累积的状态**，而不是每次从 URL 列表头部重试。

### 2.6 默认参数（官方手册）

| 参数 | 默认 |
|---|---|
| `--split` | 5 |
| `--min-split-size` | 20 MiB（可设 1 MiB–1024 MiB） |
| `--max-connection-per-server` | 1 |
| `--lowest-speed-limit` | 0（关闭） |
| `--auto-save-interval` | 60 s |
| `--uri-selector` | `feedback` |
| `--file-allocation` | `prealloc` |
| `--disk-cache` | 16 MiB |

---

## 3. 两条模型的差异

| 维度 | FDM 3.x | aria2 |
|---|---|---|
| 分段方式 | 运行时对半切最大剩余段 | 启动时定长 piece |
| 任务生成 | 每段一个线程，事件驱动加段 | 每连接领一块，容量固定 |
| 段数控制 | `MaxSections`（默认 10） | `--split`（默认 5） |
| 分段下限 | 300 KiB | 20 MiB（默认） |
| 慢连接 | 默认启用，慢 3× 持续约 3 min 重启 | 默认关闭，手动阈值 |
| 断点 | 区间 + 当前偏移 | bitfield |
| 检查点 | 事件/停止时序列化 | 60 s 定时 + 停止 |
| 选源 | 每段绑镜像，60 min 重测 | 历史速度反馈 |

两者共同的底层判断：

- **段数有上限，且远小于“块数”**；
- **连接只要在跑，就一定有活干**（FDM 段结束立刻补，aria2 连接结束立刻领）；
- **慢连接要被替换，而不是等它**；
- **状态要能低成本落盘**。

---

## 4. 抽象出的原理，与 `archoera_dl` 的对照

> 现状引用为本仓库 `app/modules/downloader/src`（2026-10-24）。

| # | 原理 | 当前实现 | 差距 | 建议 |
|---|---|---|---|---|
| P1 | **领取制**：worker 按需领取下一块，不预生成全部任务 | `chunk_stream.rs:173-216` 把所有缺块一次性 spawn 进 `JoinSet` 排队等全局信号量 | 大文件会产生海量任务；同 plan 的小文件排在后面 | 改为“每文件 N 个 worker 循环领块”，队列只存索引 |
| P2 | **分段有下限**，按剩余量决定粒度 | `plan.rs:35-38` 固定 4 MiB 块、固定 8 MiB 阈值；1 TiB 文件 → 26 万块 | 块数与文件大小线性，无上限 | `block = max(chunk_size, size / max_blocks)`，并设最小段（如 1 MiB） |
| P3 | **连接级测速**驱动调度 | 只有 30 s 读空闲（`client.rs:20`） | 40 KB/s 的块不超时，但决定整个文件尾延迟 | 每流 EWMA 速度；低速阈值默认关闭，作为高级开关 |
| P4 | **位图 + 定时检查点** | `resume.rs:62-71` 每完成一块重写整份 `completed` JSON（`chunk_stream.rs:161,223`） | O(n²) 写放大 | 定长 bitmap + 1–5 s 节流 + 暂停/取消强制写 |
| P5 | **多层配额**：全局 / 主机 / 单文件 | 全局 16、主机 8（`plan.rs:24-31`），**无单文件上限** | 一个大文件可占满全部 16 条流 | 加 per-file cap（建议 4，可配置） |
| P6 | **源选择是独立反馈环** | 整文件顺序 fallback，失败才换源 | 无历史速度，无按 host 健康度排序 | 保持整文件单源（hash 一致性），只加被动健康排序 |
| P7 | **空间与写放大属于策略** | `set_len` 截断、无预检、无合并写；`dedup` 字段未实现（`plan.rs:86`） | 中途 ENOSPC；重复 hash 重复下载 | 启动前可用空间检查；dedup 由 C# 生成侧决定是否要做 |

补充两条**不该照搬**的：

1. **FDM 的“每秒加一段”不能直接搬**：它的任务是单个大文件；我们的 plan 常是 5k 个 100 KiB 资源，每秒加段会变成请求风暴。文件级并发（我们已有的 `concurrency`）才是这类负载的主调度。
2. **跨镜像分段**要谨慎：FDM 把不同段分给不同镜像，是因为它按段重试；我们的完整性校验是**整文件哈希**，跨镜像分段虽然最终仍能校验，但一旦失败无法定位是哪一段/哪个源。保持“一文件一源 + 镜像整体回退”更符合现有契约。
3. **aria2 的 `--max-connection-per-server=1` 不能照搬**：它面向通用下载（避免被单站点封禁），而启动器场景的 5k 小文件主要受每源并发限制；我们的默认 8 是按这类负载定的，不应回退到 1。

---

## 5. 我们真正需要的最小改动（按原理排序）

| 优先级 | 改动 | 对应原理 | 理由 |
|---|---|---|---|
| P0 | `completed` 改定长 bitmap + 节流 checkpoint | P4 | 去掉 O(n²) 写放大；取消按策略清理 `.part`，只有异常退出/可重试失败保留 |
| P1 | 惰性块队列 + 每文件 worker 上限 | P1、P5 | 去掉任务爆炸与跨文件饥饿 |
| P1 | 块大小随文件大小收敛（块数上限） | P2 | 大文件不再产生 10 万级块 |
| P2 | 每流速度采样 + 可选低速重启 | P3 | 解决“一块慢、全文件等”的尾延迟 |
| P2 | 启动前磁盘空间预检 | P7 | 避免中途 ENOSPC |
| P3 | 被动源健康度（成功/失败、平均速度） | P6 | 改善镜像回退顺序，不改变单源语义 |

---

## 6. 待验证的假设

| # | 假设 | 度量 | 备注 |
|---|---|---|---|
| H1 | 5k×100 KiB 场景下，per-host 8 → 16 的收益明显 | 墙钟、p95 文件延迟 | 若瓶颈是带宽则收益小 |
| H2 | 单 1 GiB 文件，per-file cap 4 与 8/16 的差距小于“慢块重启”的收益 | 墙钟、p99 | 需要 netem 注入慢连接 |
| H3 | 惰性队列相比全量 spawn，尾延迟与 RSS 更好 | RSS、p99、任务数 | 1 TiB 计划下差异最大 |
| H4 | bitmap + 5 s 检查点显著降低写次数 | `strace -c`、IOPS | 与 `fsync` 策略一起测 |

基线工具：`tests/executor.rs` 的本地 Range 服务器（可注入慢速、断流、忽略 Range），配合 `tc netem`。先测再改参数，避免重复“凭感觉调阈值”的回退（对比表见 `05-download-engine.md` §7；上一次 1 MiB 阈值的回退见提交 `981e49f`）。

---

## 7. 参考

- aria2 手册：`--split`、`--min-split-size`、`--max-connection-per-server`、`--lowest-speed-limit`、`--auto-save-interval`、`--stream-piece-selector`、`--uri-selector`、`--file-allocation`、`--disk-cache`（<https://aria2.github.io/manual/en/html/aria2c.html>）。
- aria2 源码：`src/SegmentMan.cc:134`（checkout）、`src/BitfieldMan.cc:244`（稀疏选择）、`src/DownloadCommand.cc:302`（低速检查）、`src/FeedbackURISelector.cc`、`src/AdaptiveURISelector.cc`。
- FDM 3.x：`trunc/fsDownloadMgr.cpp:195-360, 802-826, 1429-1450, 2057-2061, 2379-2396`；`trunc/fsInternetDownloader.cpp:183-300`；`trunc/vmsSpeedTracker.cpp:20-50`；`trunc/fsSpeedMeter.cpp`；`trunc/fsAppSettings.cpp:112-206`。
- 相关本仓库文档：`05-download-engine.md`（契约与落盘）、`10-download-sources.md`（源矩阵）、`09-distribution-and-download-policy.md`（发行与镜像边界）。
