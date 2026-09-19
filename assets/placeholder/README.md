# 占位资产与 Phase 1 资产契约

本目录只保存仓库内创建的虚构占位文件，不包含正式团子、悬浮岛或动画美术。

`manifest.json` 中的文件均为仓库内创建的虚构占位内容。新增文件必须使用相对路径，并明确记录来源和许可证。

`phase-1.asset.schema.json` 是 P1-001 的最小运行时资产 Schema，`phase-1.asset.json` 是对应的确定性样例。它们只覆盖当前 Spike 需要的画布、图层、挂点、动作 clip、Transform 轨道、锚点、简化命中形状、事件和 fallback，不是通用游戏引擎格式。

运行时以 `assetVersion` 和 `compatibility` 判断兼容性；Core 解析器还会检查跨引用、画布边界和 fallback 循环。JSON Schema 用于说明结构，Core 解析器的语义校验才是运行时事实来源。
