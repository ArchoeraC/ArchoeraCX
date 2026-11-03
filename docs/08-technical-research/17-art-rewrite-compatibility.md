# 17 · 原生 ART 重写：兼容性矩阵与实现边界

> 状态：2026-11-01 · 重命名 + fixer 链已实现，并用真实安装器产物全量对齐；原生 ART processor 已接入
> 目标：用 C# 复刻 Forge/NeoForge 客户端安装所需的 ART 行为，运行时不需要 JVM。
> 前置结论见 [16-no-java-loader-processors.md](16-no-java-loader-processors.md)。

---

## 0. 为什么“Java 版本”不是唯一变量

加载器安装链里同时有四条版本轴：

| 轴 | 例子 | 对重映射的影响 |
|---|---|---|
| Minecraft / class 文件版本 | 1.20.1 → 61，1.21.1 → 65 | 解析/写出的 class 版本、属性集合 |
| ART 版本 | 0.1.22 / 1.0.6 / 2.0.3 | **写出策略、转换器、映射语义** |
| ASM 版本 | 9.2 / 9.5 | 已知属性列表、写出细节 |
| 映射格式 | MCP TSRG、Mojang ProGuard→TSRG、合并 TSRG | 方向、字段描述符可选、继承解析 |

所以不能只做“一个 remapper”，必须做**按 ART 版本分档的 profile**。
Java 运行时版本只决定处理器能否启动；真正影响字节的是 ART/ASM。

---

## 1. 已确认的版本矩阵

| 加载器 | MC | class | ART | ASM | 映射来源 | 关键差异 |
|---|---|---|---|---|---|---|
| Forge 1.20.1 | 1.20.1 | 61 | `ForgeAutoRenamingTool 0.1.22` | 9.2 | MCP `mappings` + Mojang `client.txt` → `MERGE_MAPPING --reverse-right` | `new ClassWriter(reader, 0)`：**复制常量池** |
| Forge 1.21.1 | 1.21.1 | 65 | `ForgeAutoRenamingTool 1.0.6` | 9.5 | `DOWNLOAD_MOJMAPS --sanitize`（official→obf）+ `--reverse` | `new ClassWriter(0)`：**重建常量池** |
| NeoForge 21.1.209 | 1.21.1 | 65 | `AutoRenamingTool 2.0.3` | 9.5 | NeoForm `mappings` + `MERGE_MAPPING` | 同 1.0.6，另有 javadoc 条目（客户端安装不走） |

> 2.0.3 的 `RecordFixer`/`ParameterAnnotationFixer`/`SourceFixer` 与 1.0.6 基本一致；
> `ParameterAnnotationFixer` 在 1.0.6/2.0.3 是 `OptionalChangeTransformer`，而 0.1.22
> 是普通 `Transformer`，两者的移位条件也不同（见 §7.7）。profile 里用开关表达，不复制代码。

### 1.1 最重要的分档：ClassWriter 模式

```java
// ART 0.1.22 (Forge 1.20.1)
ClassWriter writer = new ClassWriter(reader, 0);

// ART 1.0.6 / 2.0.3 (Forge 1.21+ / NeoForge)
ClassWriter writer = new ClassWriter(0);
```

- `CopyPool`（0.1.22）：输出常量池以输入池为基底，重命名产生的新字符串**追加**；
  代码重编码时未变化的索引保持不变，输出与输入结构接近。
- `RebuildPool`（1.0.6+）：常量池从空开始，按 ASM 的 canonical visit order
  重新分配；必须复刻 `ClassReader` 的访问顺序（source → nest → inner → record →
  field → method）和 `ClassWriter` 的分配点。

这两条路径必须分别实现，不能用一个“通用写法”硬套。

---

## 2. 每个 profile 的行为清单

### 2.1 Profile `forge-0.1.22`

| 项 | 行为 |
|---|---|
| 写出器 | `CopyPool` |
| 类名映射 | `obf → srg`（merged TSRG，无 reverse） |
| `ann-fix` | `ParameterAnnotationFixer`（1.0.6 的对应实现） |
| `ids-fix` | `IdentifierFixer(ALL)`，注意 0.1.22 的 `empty` 判断 |
| `src-fix` | `SourceFixer(JAVA)`：把 `SourceFile` 改成映射后的简单类名 + `.java` |
| `record-fix` | 0.1.22 版本：不强制补 `ACC_RECORD` |
| `strip-sigs` | 不传（Forge 1.20.1 客户端参数里没有） |
| 新增类 | `fernflower_abstract_parameter_names.txt` 仅在开启收集时生成；安装链未开启 |

### 2.2 Profile `forge-1.0.6` / `neoforge-2.0.3`

| 项 | 行为 |
|---|---|
| 写出器 | `RebuildPool` |
| 类名映射 | 先 `--reverse` 再应用（ProGuard 文件本身是 official→obf） |
| `ann-fix` | `ParameterAnnotationFixer` |
| `ids-fix` | `IdentifierFixer(ALL)`；2.0.3 保留空名判断 |
| `src-fix` | `SourceFixer(JAVA)` |
| `record-fix` | 1.0.6+：`super=java/lang/Record` 时强制补 `ACC_RECORD` |
| `strip-sigs` | 1.21.1 客户端传 `--strip-sigs`：删除 Jar manifest 里带 `-Digest` 的段；类内 `Signature` **不删**（名字有歧义，以实测为准） |
| javadoc | 2.0.3 的 `JavadoctorEntry` 只在输入含 `*.javadoc.json` 时触发；客户端安装没有 |

> `--strip-sigs` 的实测结论：Forge 1.21.1 的 `MC_OFF` 里仍保留类的 `Signature` 属性，
> 该开关只处理 `META-INF/*.SF/*.RSA/*.DSA/*.EC` 与 manifest 摘要。

---

## 3. class 文件支持范围

| 项 | 范围 |
|---|---|
| class 版本 | 45（Java 1.1）– 69（Java 25），只做透传与解析，不做版本升级 |
| 常量池 tag | 1,3,4,5,6,7,8,9,10,11,12,15,16,17,18,19,20 |
| 属性 | 已知属性按 profile 重编码；未知属性按原始字节透传 |
| 已知属性 | `Code` `StackMapTable` `Exceptions` `ConstantValue` `InnerClasses` `EnclosingMethod` `Signature` `SourceFile` `SourceDebugExtension` `LineNumberTable` `LocalVariableTable` `LocalVariableTypeTable` `MethodParameters` `AnnotationDefault` `BootstrapMethods` `NestHost` `NestMembers` `Record` `PermittedSubclasses` `Module` `ModulePackages` `ModuleMainClass` 及四类注解/类型注解 |
| 新增属性 | 未来 MC 版本出现的未知属性必须原样保留，不能丢 |

> 解析器必须对索引、长度、UTF-8 修改编码做边界检查；输入来自已校验哈希的 jar，
> 但仍按不可信输入处理。

---

## 4. 映射层

```text
MCP zip ──MCP_DATA──► mappings (TSRG v1, obf→srg)
                                  │
Mojang client.txt ──DOWNLOAD_MOJMAPS──► TSRG (official→obf)
                                  │
                     MERGE_MAPPING --reverse-right
                                  ▼
                         merged TSRG (obf→srg)
```

- TSRG v1 行格式：`src dst`；成员 `\tname desc dstName`，字段可省略 `desc`。
- `tsrg2` 头、字段/方法分段标记要预留；当前样本是 v1。
- 映射查找键统一用**内部名**（`/` 分隔）和 JVM 描述符；类名映射必须先于成员映射。
- 继承解析：`EnhancedRemapper` 会沿父类/接口找字段/方法；profile 需要提供
  “类 → 父类/接口/成员”的只读视图（来自同一个 jar，必要时加上 library jars）。

---

## 5. 验收与测试

不把 Mojang 产物写进仓库。验收分两层：

1. **合成夹具**（提交）：测试代码里手写最小 class 结构，覆盖常量池 tag、
   字段/方法、Code、注解、Record、未知属性；做 reader→writer 往返。
2. **真实 oracle**（不提交，env 门控）：`ARCHOERACX_ART_ORACLE=<dir>` 指向本地
   生成的 `client.jar`/`MC_OFF.jar`/`mojmaps.tsrg`，逐类比较 ART 输出。
   验收标准：被补丁覆盖的类必须 `SHA-256` 完全一致；其余类至少结构可解析。

建议的 CI 门槛：`ARCHOERACX_ART_ORACLE` 未设置时只跑合成夹具，
设置时必须 100% 匹配，否则失败。

---

## 6. 里程碑

| 阶段 | 内容 | 验收 |
|---|---|---|
| M1 | class 文件 reader + CP 模型 + TSRG 解析 | 合成夹具往返；真实 jar 全量解析无异常 ✅ |
| M2 | `CopyPool` writer + 0.1.22 基础重命名 | 1.20.1 的 `MC_SRG` 与 ART 逐类一致 ✅ |
| M3 | `RebuildPool` writer + 1.0.6 基础重命名 | 1.21.1 的 `MC_OFF` 与 ART 逐类一致 ✅ |
| M4 | fixer：record / parameter annotation / identifier / source / manifest | 全量一致 ✅ |
| M5 | 2.0.3 profile + NeoForge oracle | 全量一致 ✅（纯重命名与 1.0.6 相同） |
| M6 | 接入 `loader.repair`，按 profile 自动选择；ART 缺失时回退 JRE | ART 已原生接入 ✅；`binarypatcher` 等仍回退 JRE |

---

## 7. 实测补充（2026-10-31）

本机已用 Java 生成两份参考产物；C# 基础层已对真实 jar 做逐字节往返验证。

### 7.1 0.1.22 的 CopyPool 行为

`MC_SLIM` → `ART 0.1.22` 的输出常量池满足：

```text
输出 CP = 输入 CP 原样保留（含未使用项） + 按访问顺序追加的新常量
```

- 例：`TickPriority` 输入 87 项，输出 129 项；前 87 项逐字节相同。
- 追加顺序由 ASM 的访问顺序决定：类头 → SourceFile → 注解 → 内部类 →
  字段 → 方法（名字/描述符 → 注解 → Code 指令）→ 类属性。
- 121 个类因为 `ldc` 常量索引跨过 255 变成 `ldc_w`，Code 长度 +1/+2；
  其余类的 Code 长度不变，但偏移/帧仍需按新长度重算。
- 可选 fixer 是**独立的一遍读写**：只有 `madeChange` 才输出；
  每遍都用 `ClassWriter(reader, 0)`，所以常量池会继续追加。
- `--ann-fix` 每类都重写，但对已经成为 ASM canonical 形式的输入是幂等的
  （1.21.1 实测 `--ann-fix` 与不带该参数 SHA-1 相同）。
- `--src-fix` 会追加新的 `SourceFile` 字符串，是 1.21.1 输出增大的主因。

### 7.2 属性输出顺序（ASM 9.2/9.5 一致）

类属性：
`InnerClasses → EnclosingMethod → Synthetic(<1.5) → Signature → SourceFile →
SourceDebugExtension → Deprecated → 可见注解 → 不可见注解 → 可见类型注解 →
不可见类型注解 → BootstrapMethods → Module 系列 → NestHost → NestMembers →
PermittedSubclasses → Record → 自定义属性`

方法属性：
`Code → Exceptions → Synthetic(<1.5) → Signature → Deprecated → 可见注解 →
不可见注解 → 可见类型注解 → 不可见类型注解 → 可见参数注解 → 不可见参数注解 →
AnnotationDefault → MethodParameters → 自定义属性`

> 注意：`AnnotationWriter.putAnnotations` 先写四类类型注解，再写参数注解；
> 参数注解在前是常见误解，实测 ART 0.1.22 / 1.0.6 / 2.0.3 都按上表。

Code 内属性：
`StackMapTable → LineNumberTable → LocalVariableTable → LocalVariableTypeTable →
可见类型注解 → 不可见类型注解 → 自定义属性`

字段属性：
`ConstantValue → Synthetic(<1.5) → Signature → Deprecated → 注解 → 类型注解 →
自定义属性`

> 因此不能直接“按输入顺序拼回”，要按上表重排；未知属性保持原始 payload。

### 7.3 参考产物（本地，不提交）

| 文件 | SHA-1 | 对应声明 |
|---|---|---|
| `client-1.20.1.jar` | `0c3ec587af28e5a785c0b4a7b8a30f9a8f78f838` | Mojang client |
| `MC_SLIM.jar` | `de86b035d2da0f78940796bb95c39a932ed84834` | `MC_SLIM_SHA` |
| `MC_EXTRA.jar` | `8c5a95cbce940cfdb304376ae9fea47968d02587` | `MC_EXTRA_SHA` |
| `MC_SRG.jar`（ART 0.1.22） | `3c8aa19b710a3a68f721210eb69b74594d13e218` | 无声明，仅作 oracle |
| `PATCHED.jar`（1.20.1） | `3e175b011146785588f1649a20d1834d10282a7c` | `PATCHED_SHA` |
| `MC_OFF.jar`（ART 1.0.6） | `fd32ce6ddf1bc49a9c1a00f273cbcb2aa7ac9d18` | `MC_OFF_SHA` |
| `PATCHED.jar`（1.21.1） | `2d2537d306a05aeac6ed5fa9bf176ffacb78cb4f` | `PATCHED_SHA` |

### 7.4 当前 C# 进度

| 组件 | 状态 |
|---|---|
| class 文件 reader | 完成；真实 jar 23,526 个类全解析 |
| 常量池模型/编码（含 modified UTF-8） | 完成；23,526 个类逐字节往返一致 |
| TSRG v1/v2 解析 | 完成；单元测试覆盖字段无描述符、参数元数据 |
| ART profile 表 | 完成（0.1.22 / 1.0.6 / 2.0.3） |
| CopyPool editor | 完成；1.20.1 全量类与 ART 0.1.22 逐字节一致 |
| RebuildPool editor | 完成；1.21.1 全量类与 ART 1.0.6 / 2.0.3 逐字节一致 |
| fixer 链 | 完成；rename + `--ann-fix --ids-fix --src-fix --record-fix` 与 ART 全量逐字节一致 |
| `loader.repair` 接入 | 待实现（M6） |

### 7.5 RebuildPool（ART 1.0.6 / 2.0.3）实测

- 1.0.6 与 2.0.3 在同一输入、同一映射下输出 **SHA-256 完全相同**
  （1.21.1 client，8269 个 class），所以纯重命名阶段两条 profile 共用一套实现。
- 常量池从空开始，索引 1/2 固定是类名 Utf8/Class；所有常量按访问顺序追加：
  `类头(name→signature→super→interfaces) → SourceFile → NestHost →
  EnclosingMethod → 类注解 → NestMembers → PermittedSubclasses → InnerClasses →
  RecordComponent → Field(name→desc→signature→ConstantValue→注解) →
  Method(name→desc→signature→Exceptions→MethodParameters→注解) → Code`。
- **属性名 Utf8 不是访问期分配**，而是 `ClassWriter.toByteArray` 计算大小时按
  固定顺序补：字段属性 → 方法属性（Code 及其内部属性）→ 类属性 →
  RecordComponent → `Record`。顺序错了只差几个常量，但索引会整体偏移。
- BootstrapMethods 从空开始；`addBootstrapMethod` 先分配 **参数**、再分配
  **方法句柄**，然后才是 invokedynamic 的 NameAndType 与条目本身。
- `SourceFile` / `ConstantValue` / `MethodParameters` 的索引必须重新映射；
  `InnerClasses.inner_name`、`EnclosingMethod`、`NestHost/Members` 等同 copy 模式。
- `StackMapTable` 帧类型保持不变，但 `offset_delta` 按新代码位置重算；
  `same_frame` 在 delta≥64 时改写成 `same_frame_extended`，反之也会收窄。
- 局部变量表按 `ClassReader` 的合并规则处理：以 LocalVariableTable 为准，
  按 `(start_pc, slot)` 关联 LocalVariableTypeTable 的签名；只在类型表出现的
  条目会被丢弃；分配顺序是 `name → signature → descriptor`。
- 成员继承解析：字段按 JVMS 5.4.3.2（先接口、再父类），方法按 5.4.3.3
  （先父类、再接口）；`StatsScreen.b` 这类“接口字段遮蔽父类同名字段”必须按此顺序。
- 局部变量参数名只在映射里存在显式名字时改写；缺失时保留原 LVT 名字
  （ART 的 `mapParameterName` 用 `orElse(currentName)`，不会生成 `varN`）。
- `ldc` 每次重编码都取最窄形式（`ldc` / `ldc_w` / `ldc2_w`），`wide` 前缀也会
  按变量号/增量收窄；因此代码长度可能变化，分支、异常表、调试表、StackMapTable
  的偏移都必须重排（`CodeRelocator`）。

### 7.6 本地重建 oracle

参考产物不入库，放在 `Reference/local/art-oracle/`：

```bash
# 1.21.1 / ART 2.0.3（NeoForge）或 1.0.6（Forge，输出相同）
java -jar AutoRenamingTool-2.0.3-all.jar \
  --input client.jar --output oracle.jar --map MAPPINGS.tsrg

# 1.20.1 / ART 0.1.22
java -jar ForgeAutoRenamingTool-0.1.22-all.jar \
  --input MC_SLIM.jar --output RENAME_ONLY.jar --map MERGED.tsrg
```

测试开关：`ARCHOERACX_ART_ORACLE_1201`（+`_FULL=1`）与
`ARCHOERACX_ART_ORACLE_1211`（+`_FULL=1`）。不设置时只跑不依赖 oracle 的测试。

安装器真实产物也已验证：把 `client-1.20.1-20230612.114412-slim.jar` +
`mcp_config-...-mappings-merged.txt` + `client-...-srg.jar`（以及 1.21.1 的
NeoForm 对应文件）放进同一个目录，两个 `Fixer_pipeline` 测试同样 0 差异，
说明 profile 选档、fixer 链和常量池布局与安装器实际运行的 ART 一致。

### 7.7 fixer 链（已实现并全量对齐）

安装器传入 `--ann-fix --ids-fix --src-fix --record-fix`。ART 的 `RenamerImpl`
按注册顺序**逐遍**处理每个 class：每一遍都是一次独立的
`ClassReader → ClassWriter(reader, 0)`（复制上一遍的常量池），只有
`madeChange` 时才替换字节。因此不能把 fixer 合并进重命名的那一遍。

| fixer | 实测影响（1.20.1 / 1.21.1） | 规则 |
|---|---|---|
| `src-fix` | 7436 / 8269 类全部 | `SourceFile = <最外层简单类名>.java`；内部类/匿名类取第一个 `$` 之前，`package-info` 为 `package-info.java` |
| `record-fix` | 79 / 122 类 | `super=java/lang/Record` 时：FINAL 且非 STATIC 的字段改成 `private final`；没有 Record 属性时用这些字段合成组件；2.0.3 还补 `ACC_RECORD`（0.1.22 不补） |
| `ann-fix` | 7 / 0 类 | 见下 |
| `ids-fix` | 0 / 0 类 | 见下 |

`ann-fix` 的两个实现并不等价：

- **0.1.22**：`ParameterAnnotationFixer` 是普通 `Transformer`，每个类都会重写；
  它把参数注解读进 `ClassNode`，而 `MethodNode` 的数组长度按**方法描述符**分配，
  所以只要构造函数以合成参数开头就无条件左移 `String,int`（枚举）或 outer this
  （非静态具名/局部内部类），与属性里声明的 `num_parameters` 无关。
- **1.0.6 / 2.0.3**：改为 `OptionalChangeTransformer`，只有
  `visitAnnotableParameterCount` 的声明值等于描述符参数个数时才左移；否则原样保留。
  因此 1.21.1 官方 client 上 `--ann-fix` 是 no-op。

两种实现都遵循 ASM 的写出规则：某个参数没有注解时仍写 `u2 0`；整张表都没有
注解时属性完全消失（`madeChange` 仍可能为 false，此时 1.0.6/2.0.3 保留原字节）。

`ids-fix` 使用 `IdentifierFixerConfig.ALL`：LVT/LVTT 的名字必须满足
`Character.isJavaIdentifierStart/Part`，非法（含空串）时按方法与 slot 递增改名为
`lvt_<slot>_<n>_`。改名常量必须在 `Allocate` 阶段进入常量池，否则会写到池外。

各 pass 的 `ClassWriter(reader, 0)` 是 0.1.22 和 2.0.3 共同行为（已用字节码确认），
所以 fixer 链的正确模型是“重命名一遍 + 按需追加的 copy 模式遍”。

### 7.8 原生 ART processor（M6）

`NativeProcessorRunner` 按 `MainClass` 分派给对应的 `INativeProcessorHandler`（其余步骤回退
`ForgeProcessorRunner`）。当前已接管安装器里的全部客户端步骤：`MCP_DATA`、`MERGE_MAPPING`、
`jarsplitter`、ART 与 `binarypatcher`；只有 `DOWNLOAD_MOJMAPS` 和 server 侧步骤仍走 Java。
ART 部分：

- 识别 `net.minecraftforge.fart.Main`（0.1.22 / 1.0.6）与 `net.neoforged.art.Main`
  （2.0.3），版本从 jar 路径的 `0.1.22` / `1.0.6` / `2.0.3` 段解析；
  未知版本直接回退 Java。
- 只接受本实现覆盖的参数：`--input/--output/--names/--map`、四个 fixer、
  `--lib/--threads/--log`（忽略）。`--cfg`、`--reverse`、`--strip-sigs`、
  `--unfinal-params`、`--disable-abstract-param` 等一律回退 Java，避免静默少做变换。
- 输出 jar 按映射后的类名重命名条目（ART 写的是映射后的内部名），
  非 class 条目原样复制；修复服务只读取 jar 内容，容器压缩/时间戳不影响校验。
- `IProcessorRunner.CanRunWithoutJava` 默认 false；修复服务改为在**每个**待跑
  processor 前检查，只有该步骤原生可跑时才允许 `javaPath` 不存在。因此 ART 与
  `binarypatcher` 可以在无 JRE 时完成，`DOWNLOAD_MOJMAPS` 等未覆盖步骤会停在那一步并保留已完成的产物。
- 已知差异：ART 的 `RenamingTransformer` 还会输出
  `fernflower_abstract_parameter_names.txt`（抽象方法参数名提示，2000-01-01 时间戳）。
  它只服务反编译工具，不参与启动，原生输出暂不生成；class 字节与非 class 资源一致。
- 输出条目顺序：ART 按映射后的名字排序并补目录条目。原生实现必须复刻这一点：
  `binarypatcher` 会把输入顺序原样写进 `-client.jar`，而 Forge 的 `install_profile.json`
  带有 `PATCHED_SHA`；顺序不同会导致整文件哈希不匹配。`ArtProcessorHandler` 现在
  先收集全部条目、按 ordinal 排序，并为每个父目录生成目录条目。
  仍不生成 `fernflower_abstract_parameter_names.txt`（非 class，binarypatcher 会丢弃）。
- `binarypatcher` 的新建条目顺序同样会进入整文件哈希：1.1.x（Forge 1.20.1）用 Java
  `HashMap` 顺序，1.2.0 起（Forge 1.21.x）与 NeoForge 2.x 按名字排序。
  `BinaryPatcherProcessorHandler` 从 jar 坐标的版本段判断（≥ 1.2.0 排序），
  未知坐标回退到 NeoForge 判定。

---

## 8. 风险

- **ASM 版本漂移**：ART 升级会改变输出；profile 必须带 ASM 版本号，升级时重跑 oracle。
- **新 class 版本**：Java 25 的属性/常量池变化要先补解析，否则会丢数据。
- **映射格式**：TSRG2、字段无描述符、重载冲突要单独测试。
- **性能**：1.21.1 约 8200 个类、3 秒级；C# 实现要避免逐类分配大量临时对象。
- **许可证**：只依据公开格式与行为重写；不复制 ART/ASM 源码。ASM 的 BSD 许可
  与 ART 的 LGPL 都不影响“独立实现”，但文档与代码里不出现其源码片段。
