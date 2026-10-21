# app/native · 平台桥接

计划中的平台桥接层：

- 无边框窗口 / 拖动 / 双击最大化 / 窗口控制按钮
- 托盘、单实例、深链、文件关联、开机自启
- Windows Mica/Acrylic、Linux 合成器模糊、macOS 视觉效果的封装与回退

Dart 不直接使用 `Process.run` / MethodChannel 处理这些能力；统一经 C++ 暴露的 ABI。
参考 [`docs/10-architecture-design.md`](../../docs/10-architecture-design.md) §5.14、§10.9。
