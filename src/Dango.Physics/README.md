# Dango.Physics

`Dango.Physics` 是项目内可复用的托管物理模块。它只包含版本化配置、纯数据输入/状态、native 加载、世界/实体所有权和错误语义，不依赖 WPF、窗口、Canvas、桌面宿主或输入设备。

- 当前默认配置版本：1。
- 当前可创建容量：1、2 或 3，默认 3；支持空世界，超出配置容量明确拒绝。单/双体和三体已分别通过人工验收。
- 当前平台：Windows x64，native 模块 ABI 1，Box2D 3.1.1。
- 正式桌面宿主仍使用原 `dango_box2d.dll`，本模块不替换它。

构建、引用、API、坐标/显示契约、参数和完整示例见[物理模块说明](../../docs/phase-1-physics-module.md)。

统一使用 `dango_physics_module.dll`，不再构建独立三体预览库。已验收预览 DLL 保留为对照，统一前后证据见[统一记录](../../docs/phase-1-physics-module-unification.md)。
