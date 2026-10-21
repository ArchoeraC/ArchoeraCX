# 08 · Java 探测与 `java.scan`

> 状态：已实现（Linux x64），Windows/macOS 逻辑已写入但需真机复核。
> 实现：`app/modules/business/Archoera.Infrastructure/Java/`
> 契约：`java.scan` 结果统一走 `CoreClient.scanJava()`。

---

## 0. 结论

- 采用“**候选枚举 + 逐个校验**”，不扫全盘、不跑 shell。
- 校验命令统一为：
  ```text
  java -XshowSettings:properties -version
  ```
  解析 `java.version` / `java.home` / `java.vendor` / `os.arch`；失败再退回 `java -version` 横幅。
- 版本解析兼容：
  - `1.8.0_472` → major `8`
  - `21.0.9` → major `21`
  - `26-ea` → major `26`
- 去重按规范化绝对路径；Windows/macOS 大小写不敏感。
- 单次探测超时 5s；并发最多 4 个，避免一次扫描拉起几十个 JVM 把机器打满。

**本机实测（2026-10-20）**：`/opt/java` 下 72 个 `java` 可执行文件，`java.scan` 返回 **72/72**，全部通过校验。UI 卡片只展示前 3 个，另有“查看全部”。

---

## 1. 候选来源

### 环境

| 来源 | 规则 |
|---|---|
| `JAVA_HOME` | `$JAVA_HOME/bin/java`（Windows 为 `java.exe`） |
| `PATH` | 每个 PATH 目录下的 `java` / `java.exe` |
| 手动目录 `roots[]` | 在用户选择的目录下递归查找 `bin/java` / `jre/bin/java`，深度 ≤5、目录数 ≤2000 |
| 手动可执行 `executables[]` | 直接校验用户选择的 `java` / `java.exe` 文件 |

### Linux

| 根 | 规则 |
|---|---|
| `/usr/lib/jvm` | `<root>/<home>/bin/java`、`<home>/jre/bin/java` |
| `/usr/java` | 同上 |
| `/opt/java` | 同上，并额外展开一层 `<vendor>/<jdk>/bin/java`、`<vendor>/<jdk>/jre/bin/java` |
| `/opt` | `<root>/<home>/bin/java`、`<home>/jre/bin/java` |

> `/opt/java` 的常见布局是 `vendor/jdk/bin/java`，所以单独多展开一层；这也是本机 72 个 JDK/JRE 全部命中的原因。

### macOS

| 来源 | 规则 |
|---|---|
| `/Library/Java/JavaVirtualMachines` | `<vm>/Contents/Home/bin/java` |
| `/System/Library/Java/JavaVirtualMachines` | 同上 |
| `/usr/libexec/java_home` | 输出目录 + `/bin/java`；失败忽略 |

### Windows

| 来源 | 规则 |
|---|---|
| `%ProgramFiles%` / `%ProgramFiles(x86)%` | `Java`、`Eclipse Adoptium`、`Zulu` 下的 `bin\java.exe`/`jre\bin\java.exe` |
| `%ProgramFiles%\Microsoft` | `jdk*` 目录 |
| 注册表 | `HKLM\SOFTWARE\JavaSoft\{Java Runtime Environment,JDK,Java Development Kit}` 的 `JavaHome`，64/32 位视图都查 |
| `JAVA_HOME` / `PATH` | 同上 |

注册表/目录不可读时静默跳过，不使整次扫描失败。

### 1.1 `java.scan` 手动路径参数

```json
{
  "roots": ["/opt/java/Adoptium"],
  "executables": ["/opt/java/Adoptium/jdk-21.0.9+10/bin/java"],
  "includeDefaults": true
}
```

| 字段 | 默认 | 语义 |
|---|---|---|
| `roots` | `[]` | 递归搜索目录；结果 `source=MANUAL_ROOT` |
| `executables` | `[]` | 直接校验文件；结果 `source=MANUAL_FILE` |
| `includeDefaults` | `true` | 是否同时跑 `JAVA_HOME`/PATH/平台目录/注册表 |

- `includeDefaults=false` 只验证手动选择，适合“这个目录/文件里到底有没有 Java”。
- 手动目录限制：64 条、每条 ≤4096 字符、递归深度 5、最多访问 2000 个目录；跳过 reparse point/symlink 子目录、`.git`、`node_modules`、`build`、`bin` 递归。
- 手动可执行直接走同一套 `JavaProbe`，不信任文件名或扩展名。
- 手动候选排在默认候选之后；同一路径保留先出现的 `source`。需要验证选择时用 `includeDefaults=false`。

### 1.2 UI 回退流程

```text
默认扫描（includeDefaults=true）
  ├─ 找到 → 显示数量 + 前 3 个 + “查看全部”
  └─ 没找到
      ├─ “Search a folder” → 加入 roots → includeDefaults=false 验证
      │    ├─ 找到 → 显示
      │    └─ 仍没找到 → 提示下一步
      ├─ “Select JDK home” → 加入 roots（JDK 根目录）→ includeDefaults=false 验证
      └─ “Select java executable” → 加入 executables → includeDefaults=false 验证
           ├─ 找到 → 显示
           └─ 无效 → “该路径没有可用的 Java” + 详情
```

- 手动目录/可执行文件保存在 UI 偏好（`shared_preferences`：`java.roots` / `java.executables`），后续默认扫描自动带上。
- 自定义路径以可删除 chip 展示；删除后立即持久化。
- 用户选择目录/文件后使用 `includeDefaults=false`，避免“默认的 72 个 Java 掩盖了所选路径无效”的假成功。
- 核心/模块不可用时只显示错误，不显示手动回退（因为扫描本身不可用）。
- 手动扫描仍走 `CoreClient.scanJava()` 的事件驱动链路，不使用定时器。

---

## 2. 校验与解析

```text
candidate path
  → File.Exists
  → ProcessStartInfo(argv): java -XshowSettings:properties -version
      CreateNoWindow = true
      UseShellExecute = false
      Redirect stdout/stderr
      timeout 5s
  → 解析 properties（java.version/java.home/java.vendor/os.arch）
  → 无 version 时退回 -version 横幅
  → 推断 vendor/arch
  → JavaRuntimeInfo
```

### 版本 → major

| 输入 | major |
|---|---|
| `1.8.0_472` | 8 |
| `1.7.0_80` | 7 |
| `21.0.9` | 21 |
| `25` | 25 |
| `26-ea` | 26 |

### 结果模型

```json
{
  "path": "/opt/java/Adoptium/jdk-21.0.9+10/bin/java",
  "version": "21.0.9",
  "major": 21,
  "vendor": "Eclipse Adoptium",
  "arch": "amd64",
  "source": "SYSTEM"
}
```

`source` 取值：`JAVA_HOME`、`PATH`、`SYSTEM`、`REGISTRY`、`JAVA_HOME_UTIL`。

---

## 3. UI 行为

- 首页 `JavaStatusCard`：
  - 空闲 → “扫描 Java”
  - 扫描中 → 进度条 + 进度事件（wake 驱动）
  - 完成 → 显示总数 + 前 3 个运行时（路径、版本、架构）
  - 超过 3 个 → “查看全部 N 个”，弹出可滚动列表
  - 模块缺失/扫描失败 → 可读错误 + 详情 tooltip，不崩溃
- 扫描通过 `CoreClient.scanJava()`，事件由 `EventPump` 的 wake 驱动，不用定时器轮询。

---

## 4. 本机验证

```bash
cd app
./tool/build-core.sh
./tool/build-cs.sh
dotnet run --project modules/business/Archoera.DevHost -c Release -- --check
dotnet run --project modules/business/Archoera.DevHost -c Release -- --scan
dotnet run --project modules/business/Archoera.DevHost -c Release -- \
  --scan --root /opt/java/Adoptium --no-defaults
dotnet run --project modules/business/Archoera.DevHost -c Release -- \
  --scan --java /opt/java/Adoptium/jdk-21.0.9+10/bin/java --no-defaults
flutter test test/java_status_card_test.dart
```

实测：

```text
ok: 72 Java runtime(s) discovered
manual root count 13        # /opt/java/Adoptium，全部 source=MANUAL_ROOT
manual file count 1         # 指定 jdk-21.0.9+10/bin/java
java.scan crosses the C ABI and reports a valid runtime ✅
java.scan accepts manual roots and restricts discovery to them ✅
```

对比：

```bash
find /opt/java -type f -name java | wc -l   # 72
find /opt/java/Adoptium -type f -name java | wc -l  # 13
# java.scan items count                     # 72
```

结论：`/opt/java` 下所有二进制 `java` 都被识别；UI 默认只显示 3 个，不是漏扫。手动目录/可执行文件路径也已验证。

---

## 5. 已知边界

| 情况 | 行为 |
|---|---|
| `java` 不在候选根/环境变量中 | 默认扫描不会发现；UI 可“选择目录搜索”或“手动选择 java” |
| 可执行文件不叫 `java`/`java.exe` | 手动选择文件仍可校验；自动扫描不会发现自定义命名的 wrapper |
| 路径是 symlink | `File.Exists` 会跟随；若目标存在则可发现 |
| JDK 自带 `jre/bin/java` | 会作为独立条目出现，可能看起来重复 |
| 探测超过 5s | 跳过该候选，不阻断整次扫描 |
| Windows 注册表不可读 | 跳过注册表来源，继续目录/PATH |
| macOS `java_home` 不存在 | 跳过，不影响目录扫描 |
| 全盘搜索 | 不做；避免慢扫描与权限噪音 |

当前已支持“目录/可执行文件”手动添加；仍不做全盘递归，避免慢扫描与权限噪音。

---

## 6. 参考实现（MIT，只读）

| 项目 | 参考点 |
|---|---|
| [MinecraftLaunch](https://github.com/Lunova-Studio/MinecraftLaunch) `JavaUtil` | `-XshowSettings` 版本解析、Windows 注册表/目录、去重 |
| [ProjBobcat](https://github.com/Corona-Studio/ProjBobcat) `DeepJavaSearcher` | 深度搜索边界、排障思路 |
| [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) `MinecraftJavaPathResolver` | 托管 runtime 路径解析 |

本项目按行为重写；未复制上游代码。参考 commit 与许可证见 [`Reference/MANIFEST.md`](../../Reference/MANIFEST.md)。

---

## 7. 下一步

1. Java 选择策略：按游戏版本 `javaVersion.majorVersion` 选最佳候选（exact major 优先，其次更高 major）。
2. 托管 runtime：Mojang `java-runtime` manifest 下载/校验/安装。
3. 设置页：把手动路径从 UI 偏好迁移到 C# 设置/SQLite，支持排除、置顶、拖拽排序。
4. Windows/macOS 真机回归。
