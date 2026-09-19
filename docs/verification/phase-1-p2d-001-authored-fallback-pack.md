# P2D-001 / AP-005 手工动作与 fallback 候选包验证记录

> 状态：已完成，A5 静态姿势预审通过
>
> 日期：2026-09-03
>
> 实施分支：`feat/p1-004-box2d-soft-body`

## 1. 范围与排除项

本检查点补齐 `Wander`、`ApproachIsland`、`OnIsland`、`ClimbDown`、`Dragged`、`Falling`、`Reacting`，并与 AP-004 的三类动作组成十个可见 Core 状态的手工关键姿势和 fallback 候选包。

本轮没有引入 Box2D 源码、二进制、P/Invoke、动态轮廓或 WPF 播放；没有修改 Core 状态机、默认 placeholder 加载、捏脸 UI、持久化、网络同步或多人功能。`Hidden` 明确不生成可见 clip。

## 2. 先失败验收

1. 先扩展动作验证器，要求十个可见动作按批准顺序存在，并检查姿势顺序、循环、可打断点、降低动画、事件和 final anchor。
2. 对扩展前的三动作 manifest 执行验证，实际失败并报告缺少 `wander`、`approach-island`、`on-island`、`climb-down`、`dragged`、`falling`、`reacting`；旧 `landing` 同时缺少新的 final anchor 要求。
3. 新增 SVG 和来源门禁：身体源必须是单一连续 path、单一纯色 hex fill，不得包含 gradient、filter、opacity、style、mouth、blush 或 shadow；缺少源/导出 provenance 时失败。
4. 新增真实 Core parser 测试，要求候选覆盖所有非 `Hidden` 状态、图层文件存在、没有 shadow layer、fallback 无环，并证明默认 placeholder 仍保持独立。

## 3. 候选资产结果

| 项目 | 实际结果 |
|---|---|
| 可见动作 | 10/10；`Hidden` 无 clip |
| 新增身体姿势 | 27 个确定性 SVG / PNG |
| 总分层 PNG | 42 个：39 身体、2 眼睛、1 帽子 |
| 画布与 Alpha | 全部 160 × 120，具有 Alpha、透明像素和可见像素 |
| 图层 | body / eyes / hat 独立；无嘴、腮红、阴影层 |
| 事件 | `island-grabbed`、`island-released`、`landed` |
| fallback | 除 `idle` 外均可无环回退到 `idle` |
| 运行时候选 | `assets/preproduction/phase-1-dango/candidate.asset.json`，contract v1，可由真实 parser 解析，不默认加载 |
| 静态审查板 | `assets/preproduction/phase-1-dango/motion-samples/review/phase-1-authored-fallback-pack.png` |

运行时 contract v1 仍不能表达身体 tint、帽子 variant set、逐帧帽子挂点、独立眼睛附加轨道和 Box2D profile/软体拓扑。本检查点只记录缺口，没有修改 Schema 或把物理数据塞入无关字段。

## 4. 静态门禁

| 门禁 | 实际结果 |
|---|---|
| `dotnet format .\Dango.slnx --verify-no-changes --no-restore` | 通过 |
| Release build | 通过，0 警告、0 错误 |
| Core tests | 20/20 通过，含候选 runtime parser 测试 |
| Desktop tests | 65/65 通过 |
| 动画导出预检单元测试 | 13/13 通过 |
| AP-005 动作单元测试 | 6/6 通过 |
| 动作 manifest | 10 个动作，错误数 0 |
| PNG 结构与来源预检 | 42 个文件，统一 160 × 120，错误数 0 |
| 项目动画 skill validator | 通过 |
| 确定性重复生成 | 45 个源/metadata 文件、42 个 PNG 和审查板连续生成 SHA-256 一致 |
| Markdown 本地链接 | 通过 |
| `git diff --check` | 通过 |

## 5. 证据分层

- **静态门禁**：通过；资产结构、来源、透明 PNG、动作语义、runtime parser、构建和自动测试均有实际证据。
- **集成门禁**：通过的是候选资产与现有 Core contract v1 的解析兼容；没有 Box2D/native、网络、数据库或桌面宿主组合验证。
- **静态视觉自审**：审查板中未见嘴、腮红、阴影、描边、渐变或肢体；动作差异通过连续轮廓、重心和压缩表达。该自审不替代用户 A5 预审。
- **实时行为**：未验证；没有 WPF 播放、缓动、Box2D、真实岛边遮挡或桌面运行。
- **人工验收**：用户于 2026-09-03 查看交付结果后回复“继续”，确认 A5 静态姿势预审通过。该确认不等于 WPF、Box2D 或真实桌面动态体验通过。
- **交付状态**：仅保存在本地实施分支；未合并回 Phase 1、未合并到 `main`、未推送、未部署，也没有安装或启动外部资源。

## 6. 结论与下一步

P2D-001/AP-005 与 B1 已通过。用户已授权继续 P2D-002 Box2D 获取、构建与加载 Spike。
