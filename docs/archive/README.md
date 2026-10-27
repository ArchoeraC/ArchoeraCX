# 归档文档（docs/archive）

本目录存放**已完成、已废弃或被取代**的设计稿与历史快照，仅作溯源与历史对照，**不再是现行规范**。
现行文档在 `docs/` 根目录；冲突时以现行文档为准。

> 说明：归档文件保留归档时的原文与文件名（部分小标题仍沿用旧的编号体系），不做内容维护；路径引用已尽量改写为归档后的位置。

| 归档文件 | 原位置 | 原状态 | 归档理由 | 现行替代 |
|---|---|---|---|---|
| [`ui-tech-selection.md`](ui-tech-selection.md) | `docs/01-tech-selection.md` | 选型对比稿 · 2026-10 | UI 已定为 Flutter，对比仅作决策依据 | [`../10-architecture-design.md`](../10-architecture-design.md)、[`../11-ui-design-system.md`](../11-ui-design-system.md) |
| [`ffi-architecture.md`](ffi-architecture.md) | `docs/02-architecture.md` | 架构设计稿 v1 | ABI、模块协议与存储重写后由现行架构取代 | [`../10-architecture-design.md`](../10-architecture-design.md) |
| [`polyglot-core-ffi.md`](polyglot-core-ffi.md) | `docs/07-polyglot-core.md` | FFI 契约稿 | 契约已落在 `app/contracts/`，正文由现行 ABI 章节取代 | [`../10-architecture-design.md`](../10-architecture-design.md) §6–§7 |

## 维护规则

- 只有被现行文档明确取代、且仍具溯源价值的内容才进入本目录。
- 归档时同步更新上表：原位置、归档理由、现行替代缺一不可。
- 归档文档不再接受修改；需要新结论时写进现行文档，而不是复活归档稿。
- 本地临时草稿与反编译产物放 `Reference/local/`，不进入本目录。
