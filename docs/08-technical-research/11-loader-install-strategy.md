# 11 · 加载器安装策略：无 Java 边界与自研路线

> 状态：2026-10-21 · 只读复核 SJMCL / CmlLib / ProjBobcat / MinecraftLaunch 后整理
> 目标：明确 ArchoeraCX 哪些加载器可以真正无 Java 安装，哪些必须使用 JVM；给出可独立实现的方案。
> 许可边界：只提炼公开协议、数据格式和行为，不复制任何 GPL/AGPL 源码、文案、图标或资源。

---

## 0. 一句话结论

**只有需要改写客户端 jar 的加载器才需要 JVM。**

| 加载器 | 安装方式 | 需要 JVM |
|---|---|---|
| Fabric | 拉 meta profile / launcherMeta，合并版本 JSON，下载库 | 否 |
| Quilt | 同 Fabric（Quilt meta v3，额外 hashed 库） | 否 |
| Forge ≤ 1.12.2 | 解 installer zip，抽 universal jar + versionInfo | 否 |
| Forge 1.13+ / NeoForge | installer + processors 生成 `-client.jar` | **是** |
| OptiFine | Patcher | **是** |

Fabric/Quilt 和 Forge spec 0 是真正的纯 HTTP + 解压路径；现代 Forge/NeoForge 的
`ATR`（official→srg 重映射）和 `binarypatcher`（LZMA + delta）必须运行 Java 工具。

---

## 1. SJMCL 的实际做法（行为复核）

### 1.1 Fabric / Quilt

- 端点：`meta.fabricmc.net/v2/versions/loader/{mc}/{loader}`；Quilt 用 v3。
- 读取 `launcherMeta.mainClass.client` 与 `libraries.common/client`。
- 库以 Maven 坐标加入版本 JSON；下载 URL 由源根 + 坐标路径拼出。
- 不调用 installer，不启动 Java。
- Fabric API / QFAPI 作为独立 mod 安装，不混进加载器事务。

**注意**：SJMCL 把 `server` 库也加入客户端 classpath；我们应只取 `common` + `client`。

### 1.2 Forge / NeoForge

```
installer.jar
  install_profile.json
  version.json
  data/client.lzma
  maven/**（新版通常只有 shim）

  1) 解析 install_profile.libraries（安装期 classpath）+ version.json.libraries（启动库）
  2) 下载有 url 的库；空 url 是生成物，不能下载
  3) 解 maven/** 与 data/client.lzma
  4) 运行 processors（Java）生成 MC_SLIM/MC_EXTRA/MC_SRG/PATCHED 等
  5) 合并 version.json，写实例
```

- SJMCL **不运行** installer 的 `ClientInstall` 主类，只挑 processors。
- processors 使用“启动游戏那个 Java”（系统 Java 或按需下载的 Mojang runtime），不是独立安装 JRE。
- `DOWNLOAD_MOJMAPS` 在无 `--sanitize` 时可以直接下载 `client_mappings` 替代；带 `--sanitize` 时必须真跑。
- SJMCL 不校验 processor outputs 的 SHA-1，没有 staging/receipt/journal，失败后靠重启恢复；这些不能照抄。

### 1.3 其它实现对比

| 关注点 | CmlLib.Core | MinecraftLaunch | ProjBobcat |
|---|---|---|---|
| Fabric/Quilt | 直接把 `profile/json` 写为版本 JSON；无 Java | 同样写 profile，再下载父子库 | Fabric 从 launcherMeta 重建 JSON（会丢 arguments）；Quilt 基本透传 |
| Forge ≤1.12.2 | 快照未包含 | legacy `versionInfo` + universal；无 Java | legacy universal + versionInfo；无 Java |
| Forge 1.13+ / NeoForge | 独立包启动 Java | 逐 processor 启动 Java | 逐 processor 启动 Java |
| 托管 JRE | 有，处理 chmod | 有，但硬编码 gamma，无 chmod | 无，仅系统扫描 |
| processor outputs 校验 | 无 | 无 | 无 |
| staging / receipt / resume | 无 | 无 | 无 |
| 关键缺陷 | 版本 JSON 非原子写 | 异常后仍可能报告成功；参数拼成单字符串 | stderr 即失败、无退出码判断 |

**结论**：Fabric/Quilt 采用 CmlLib 的“profile JSON 即版本 JSON”模型最简单；Quilt 要补
`org.quiltmc:hashed` → `net.fabricmc:intermediary` 的坐标重写。现代 Forge/NeoForge 三家都需要 JVM，
没有可直接借鉴的“完全无 Java”实现；我们要自己补上 staging、outputs 校验、续跑和托管 JRE。

> 详细对比只读审计见 `Reference/upstream`；本项目不复制其代码。

---

## 2. ArchoeraCX 的目标设计

### 2.1 三种安装模式

| 模式 | 适用 | JVM |
|---|---|---|
| `http` | Fabric / Quilt | 否 |
| `legacy-installer` | Forge ≤1.12.2 | 否 |
| `processors` | Forge 1.13+ / NeoForge | 是 |

### 2.2 计划 / 下载 / 提交分离

```
loader.plan
  → 解析 installer / meta，产出：
      downloads[]   远程文件（带 sha1/size）
      extracts[]    installer 内文件
      generated[]   processors 产出（带 producerIndex/outputs）
      versionJson   合并后的自包含版本
      requiresJava  bool
  → DownloadPlan → Rust 下载
loader.commit
  → 写 versions/<id>/<id>.json
loader.check
  → quick/full 校验生成物
loader.repair
  → processors 续跑（需要 JVM）
```

### 2.3 quick / full 校验

- **quick**：所有文件存在 + size；只对 `requiredForLaunch` 的生成物做 SHA-1。
- **full**：manifest 中所有带 hash 的文件做 SHA-1。
- **repair**：从最早缺失/损坏的 processor 开始重跑；每个 processor 后校验 outputs。

### 2.4 状态机

```
NotInstalled → Downloading → Downloaded → Installing → Installed
                                    ↘ PendingProcessors（无 JVM）
                                    ↘ RepairRequired（有 JVM 但失败）
```

`PendingProcessors` 必须在 `launch.prepare` 前被处理；不能把半成品当作可启动状态。

---

## 3. 是否 bundle JRE？

**建议按需下载 Mojang JRE，不随安装包分发。**

| 维度 | Bundle JRE | 按需下载（推荐） |
|---|---|---|
| 包体积 | 每平台/架构 40–180 MB | 0 |
| 更新 | 每次 JRE 补丁都要发版 | 跟随 Mojang `all.json` |
| 许可/公证 | 复杂 | 来源可审计 |
| 离线首装 | 唯一优势 | 可缓存已下载 runtime |

启动本来就需要 JRE；把同一个 runtime 复用于 processors，边际成本最低。

---

## 4. 不建议做的事

- 不下载/分发第三方预生成的 `-client.jar`（含 Mojang 版权 class）。
- 不实现原生 ATR / binarypatcher 替代（ASM 级重映射 + LZMA delta，维护成本过高、版本覆盖低）。
- 不在没有 `--sanitize` 判断的情况下替换 MOJMAPS。
- 不把 processor 的退出码 0 当作安装成功；必须校验 outputs。
- 不复制 SJMCL 的 GPL 代码/文案/图标。

---

## 5. 分阶段路线

| 阶段 | 内容 | Java |
|---|---|---|
| P0 | Fabric/Quilt 纯 HTTP 安装 + 自包含版本 JSON | 否 |
| P0 | Forge ≤1.12.2 installer 解包 | 否 |
| P1 | Mojang JRE 按需下载 + 注册到 Java list | 下载本身否 |
| P1 | processors runner：argv、staging cwd、超时、进程树、env 清理、outputs 校验 | 是 |
| P1 | `PendingProcessors` + `launch.prepare` 预检 | 是 |
| P2 | Forge/NeoForge 全链路 + `--sanitize` 感知 | 是 |
| 不做 | OptiFine 自动安装、第三方 client jar、原生重映射 | — |

当前代码已具备 `loader.plan` / `loader.commit` / `loader.check` / `loader.verify` / `loader.repair`
和 Forge/NeoForge installer 计划；还缺 Fabric/Quilt meta、Forge spec 0 分支、Mojang runtime 落地和
processors 安装编排。
