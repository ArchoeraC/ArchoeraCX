# 05 · 路线图

> 原则：**每个阶段都以"可运行、可验收"结束**，不做"半年后一起联调"。
> 估算按 1–2 人全职计；单人业余请 ×2.5。

---

## 当前状态（2026-10-21）

| 阶段 | 状态 | 已完成 |
|---|---|---|
| P0 | 完成 | 选型、许可证、架构、构建骨架 |
| P1 | 部分完成 | C++ 核心、模块/方法注册表、事件泵、C# AOT、Java 扫描、Forge/NeoForge 生成物、Rust 计划执行器 |
| P2 | 部分完成 | Flutter AppShell、实例/设置/下载页、CoreClient |
| P3 | 部分完成 | Microsoft Device Code、XBL/XSTS/Minecraft、离线账户；凭据保险库已接入（crypto/file），待 Minecraft client_id 审核 |
| P4 | 部分完成 | Forge/NeoForge 计划、生成物校验/修复、实例内安装 UI |
| P5 | 部分完成 | 实例 SQLite、Modrinth；CurseForge/整合包未完成 |
| P6 | 进行中 | 设计令牌、主题、i18n、账户/实例 UI |
| P7 | 未开始 | 更新器、签名、安装包、正式发布 |

---

## 阶段总览

| 阶段 | 主题 | 产出 | 预估 |
|---|---|---|---|
| P0 | 选型与文档 | 本仓库文档、参考项目清单、许可证结论 | 已完成 |
| P1 | ABI + 枢纽骨架 | C++ 枢纽、C#/Rust 模块、垂直切片跑通 | 4–5 周 |
| P2 | FFI 接线 + UI 壳 | Flutter 通过 ABI 装原版并启动 | 3–4 周 |
| P3 | 账户 | 微软登录 / 离线 / 第三方 | 2–3 周 |
| P4 | 加载器 | Fabric / Quilt / Forge / NeoForge | 4–6 周 |
| P5 | 实例与资源 | 多实例、Mod 管理、整合包 | 6–8 周 |
| P6 | 体验与打磨 | 主题、动效、3D 皮肤、托盘、i18n | 持续 |
| P7 | 发布 | 更新器、签名、安装包、合规 | 2–3 周 |

---

## P0 · 选型与文档（已完成）

**产出**

- `README.md` + `docs/README.md` + 当前架构/认证/发行/UI 文档
- 参考项目克隆清单与许可证矩阵
- 架构决策（AGPL-3.0-only / Flutter / C++ 枢纽 / C ABI）的结论

**验收**

- [x] 分发模式：AGPL-3.0-only（开源，不接受外部贡献）
- [x] UI：Flutter
- [x] 边界：C++ 枢纽 + C ABI
- [x] HTTP：API 归 C#，文件下载归 Rust，C 网络层取消
- [x] `Reference/upstream` 不参与构建，已忽略
- [x] FFI 垂直切片已跑通 `ping`、`java.scan`、实例与账户；安装/启动待完成

**风险**：在"要不要 4 门语言"上反复横跳。→ 架构已定，用垂直切片验证实现，而不是继续讨论。

---

## P1 · ABI + 枢纽骨架（垂直切片）

**目标**：三平台跑通 **Dart → C++ → C#(AOT) → Rust** 的最小链路，并完成"装原版 + 启动"。

**范围**

- 实现依据：`docs/08`（合并 / 安装 / 下载 / 搜索 / 崩溃）
- `archoera_abi.h` v1：init/poll/call/cancel/shutdown + 版本握手
- `archoera_core`（C++）：MethodRegistry、ModuleRegistry、TaskRegistry、EventPump、日志、崩溃处理器
- `archoera_cs`（C# NativeAOT）：版本清单、rules 求值、Java 探测、实例最小模型、SQLite、**API 客户端**
- `archoera_dl`（Rust）：并发下载、hash 校验、断点续传、原子落盘
- 下载计划由 C# 生成，经 ABI 交给 Rust（契约见 `10-architecture-design.md` §9）
- 启动：拼装 classpath/JVM 参数，spawn JVM，转发 stdout/stderr
- CLI 备用入口（JSON-RPC over stdio，仅开发/CI）

**验收**

| # | 标准 |
|---|---|
| 1 | 三平台：Dart 加载 C++ → 调 C# → 返回字符串 |
| 2 | `install 1.21.4`（固定测试基线）成功，文件哈希全部匹配 |
| 3 | `launch 1.21.4 --offline TestPlayer` 进入主菜单 |
| 4 | 断网中断后重跑可续传，不重复下载 |
| 5 | 1000 文件安装时核心 RSS < 300MB（不含 Flutter/游戏），进度事件不丢 |
| 6 | 取消任务后 2s 内所有 worker 停止 |
| 7 | 任一模块故意 abort → 生成 minidump 且 UI 有提示 |
| 8 | 无 UI 时 CLI 也能跑完同一流程 |

**风险**

- 规则求值细节与官方不一致 → 真实版本 JSON 快照测试。
- NativeAOT 导出/AOT 限制 → 先做最小导出样例，再写业务。
- 跨语言调试 → 业务放纯类库 + DevHost。
- 构建矩阵 → 第一天就上三平台 CI，不要最后补。

---

## P2 · FFI 接线 + UI 壳

**目标**：Flutter 通过 C ABI 驱动 P1 的全部能力，无同步阻塞。

**范围**

- Dart binding（`dart:ffi`）+ `CoreClient`
- `NativeCallable.listener` 事件泵 + 进度合并
- UI：首页、版本列表、安装进度、日志面板、设置骨架
- 单实例、窗口状态持久化
- 热重启 `attach/detach` 与任务快照重放
- 开发模式：UI 连 CLI（JSON-RPC），便于隔离问题

**验收**

- [ ] UI 点击"安装 1.21.4"→ 进度条 → 完成 → 启动
- [ ] UI isolate 无任何阻塞调用（火焰图验证）
- [ ] 事件 10k/s 压测下 UI 仍 60fps
- [ ] 模块版本不匹配时拒绝启动并给出可读错误
- [ ] 冷启动 < 1.5s

**风险**：进度事件过密导致 UI 卡顿 → 合并 + 节流（100–200ms）。

---

## P3 · 账户

**目标**：正版登录可用，token 安全，UI 不接触敏感数据。

**范围**

- Microsoft OAuth 2.0 device code flow（自有 Entra 应用）
- Xbox Live → XSTS → Minecraft Services → profile
- refresh token 持久化（凭据保险库：OS 安全存储 crypto / file 降级）
- 离线账户（明确提示仅离线/局域网）
- 第三方认证：`authlib-injector` 以独立 agent 调用
- 多账户切换、token 失效自动刷新、登出清理

**验收**

| # | 标准 |
|---|---|
| 1 | 正版账户可进入正版服务器 |
| 2 | 重启应用无需重新登录 |
| 3 | 日志/内存转储中不出现 token 明文 |
| 4 | token 失效时给出明确引导，不静默失败 |
| 5 | 离线账户启动不请求网络认证 |

**风险**：微软接口变更、地区风控、设备码体验差 → 提供"浏览器回调"备选；错误文案可读。

---

## P4 · 加载器

**目标**：主流加载器一键安装，版本兼容提示准确。

**顺序**：Fabric → Quilt → NeoForge → Forge → OptiFine（最后，条款受限）

**范围**

- Fabric/Quilt：meta API 取 loader profile，合并进版本 JSON
- Forge/NeoForge：下载 installer → 解析 `install_profile.json` → 执行 processors（需要 Java）→ 合并版本
- 加载器版本与游戏版本兼容矩阵
- 安装失败的可恢复状态（半成品标记 + 重装）
- 启动参数合并（`-DignoreList`、`--add-opens` 等）

**验收**

- [ ] 1.20.1 + Forge 安装并进入主菜单
- [ ] 1.21.x + NeoForge 同上
- [ ] Fabric/Quilt 安装 < 30s（缓存命中）
- [ ] 安装中断后可重试，不产生"幽灵实例"
- [ ] 每个加载器有集成测试（假 JVM 断言 argv）

**风险**

- Forge processors 对 Java 版本/路径敏感；Windows 空格与中文路径。
- NeoForge 与 Forge 的版本命名差异。
- OptiFine 无公开 API 且条款限制 → 考虑"引导用户手动放置"。

---

## P5 · 实例与资源

**目标**：多实例隔离、磁盘高效、整合包可用。

**范围**

- 实例 CRUD、复制、导入导出、图标、分组/标签
- 共享 libraries/assets + 实例级 gameDir
- Mods/资源包/光影扫描与启停（`.disabled`）
- Modrinth 搜索/安装/更新（API v2）
- CurseForge 集成（API key + 条款）
- `.mrpack` 导入/导出；CurseForge zip 导入
- 依赖解析与冲突提示（仅提示，不做自动解决）
- 文件 hash 去重（可选硬链接，失败降级为复制）

**验收**

| # | 标准 |
|---|---|
| 1 | 同一版本建 3 个实例，共享库只存一份 |
| 2 | 导入 500MB 整合包 < 2 分钟，失败可回滚 |
| 3 | Mod 启停不需要重启启动器 |
| 4 | 删除实例不影响其他实例与共享库 |
| 5 | 500 个 Mod 的列表滚动 60fps |

**风险**：文件锁（Windows）、路径长度、整合包内的恶意路径（Zip Slip）→ 强制规范化校验。

---

## P6 · 体验与打磨

**目标**：从"能用"到"想用"。

**范围**

- 设计令牌系统（颜色/字号/间距/动效曲线），明暗主题 + 强调色
- 背景：纯色 / 图片 / Mica / Acrylic + 模糊缓存
- 页面转场、列表动效、骨架屏
- 3D 皮肤查看器（预渲染方案 + 2D 回退）
- 托盘、通知、开机自启、最小化行为
- i18n（zh-CN / en-US 起步）、字体回退（中文）
- Java 管理界面（下载/切换/校验）
- 崩溃分析：exit code、`hs_err_pid`、日志尾部 → 人话 + 建议
- 下载中心（队列、限速、镜像选择、测速）
- 可访问性基础（键盘导航、对比度）

**验收**

- [ ] 主题切换无闪烁，模糊不掉帧
- [ ] 中文界面无截断/错位（150% 缩放）
- [ ] 崩溃后用户能拿到一段可复制的诊断信息
- [ ] 所有文案走 i18n，无硬编码

---

## P7 · 发布

**范围**

- 自动更新（独立 updater，替换时释放自身占用）
- Windows 代码签名（EV / Azure Trusted Signing）
- macOS arm64 签名 + notarization（仅 Apple Silicon）
- 安装包：MSI/EXE、AppImage/Flatpak、DMG
- 官网 + 下载页 + 文档
- 合规：`THIRD-PARTY-NOTICES`、EULA、隐私政策、免责声明
- 可选：匿名遥测（默认关闭，首次启动询问）

**验收**

- [ ] 从旧版本一键升级成功，失败可回滚
- [ ] 杀软/ SmartScreen 无拦截（签名有效）
- [ ] 安装包内所有许可证声明齐全
- [ ] 卸载不留垃圾（保留用户数据可选）

---

## 风险登记表

| 风险 | 影响 | 概率 | 缓解 |
|---|---|---|---|
| 多语言工具链拖慢迭代 | 高 | 高 | 已是 4 语言（去 C）；容器化构建；垂直切片验证 |
| 单进程崩溃不隔离 | 高 | 中 | Crashpad/minidump；关键任务可降级子进程 |
| NativeAOT 调试困难 | 高 | 高 | 业务纯类库 + DevHost + 单测 |
| ABI 漂移 | 高 | 中 | 版本握手；同包发布；契约测试 |
| 跨语言内存所有权错误 | 高 | 中 | 所有权矩阵 + ASan/UBSan |
| 两套 HTTP 行为不一致 | 中 | 高 | 指定唯一 owner |
| 微软认证接口变动 | 高 | 低 | 抽象 `IAuthProvider`；错误可诊断 |
| 加载器安装碎片化 | 高 | 高 | 每个加载器独立适配层 + 集成测试 |
| 许可证返工 | 高 | 低 | 已定 AGPL-3.0-only；CI 白名单（第三方 copyleft 需审批，避免影响未来闭源） |
| UI 性能（大列表/日志） | 中 | 高 | 虚拟滚动 + 进度节流 + 基准测试 |
| 磁盘/权限问题 | 中 | 高 | 原子写、路径规范化、降级策略 |
| 竞品功能覆盖压力 | 中 | 高 | 先做"稳定 + 快 + 好看"，不追功能数量 |

---

## 里程碑定义（对外可讲）

- **M1 "链路通"**（P1 结束）：三平台 Dart → C++ → C# → Rust 垂直切片，装原版并启动。
- **M2 "能用"**（P3 结束）：图形界面 + 正版登录 + 原版启动。
- **M3 "能玩 Mod"**（P4 结束）：主流加载器一键安装。
- **M4 "能管资源"**（P5 结束）：多实例 + 整合包。
- **M5 "愿意用"**（P6 结束）：视觉与稳定性达标。
- **M6 "可发布"**（P7 结束）：签名、更新、合规齐全。
