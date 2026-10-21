# app/contracts · ABI 与契约

计划中的契约文件：

```
contracts/
├─ abi/archoera_abi.h
├─ module/archoera_module_abi.h
├─ methods/*.schema.json
├─ events/*.schema.json
└─ errors/codes.json
```

契约是 Dart / C++ / C# / Rust 的共同事实来源；实现不得各自定义私有一份。
规范性文本见 [`docs/10-architecture-design.md`](../../docs/10-architecture-design.md) §6–§7、§13。
