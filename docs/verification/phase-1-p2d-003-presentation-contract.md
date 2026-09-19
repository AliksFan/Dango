# P2D-003 表现契约与权威隔离验证记录

> 状态：已完成
>
> 日期：2026-09-03
>
> 实施分支：`feat/p1-004-box2d-soft-body`

## 1. 范围与结论

本检查点在 `Dango.Desktop` 内建立 Core 快照到物理表现层的最小契约。确定性 fake world 与 Box2D adapter 共用 `IPhysicsPresentationWorld`；输出只包含局部归一化轮廓、眼睛/帽子挂点、诊断和 fallback，不含权威位置、动作结果、逻辑碰撞或落点写回入口。

Box2D adapter 本轮只通过 P2D-002 已验证的 native loader 和 create/step/destroy lifecycle probe 证明契约接入与受控降级。软体节点环、弹簧、真实动态轮廓和 WPF 渲染属于 P2D-004 及后续检查点，本轮未实现。

## 2. 契约与隔离边界

| 边界 | 实际结果 |
|---|---|
| 输入 | 复制团子标识、权威边界/动作/起始时间、语义锚点、接触法线、强度、参数版本与确定性种子 |
| 输出 | `[-1, 1]` 局部轮廓点、眼睛挂点、帽子挂点、backend/step/code 诊断和 fallback 原因 |
| 权威隔离 | frame 类型没有 authoritative、landing 或 collision 输出属性；调用前后 `SceneSnapshot`、位置、状态和落点相等 |
| 依赖方向 | 架构测试扫描 `Dango.Core` 与 `DesktopHost` 源码，禁止 Box2D、`NativeLibrary`、native DLL 名称及表现 adapter 引用 |
| 双体 cue | 只允许两个不同参与者、视觉接触法线和强度；类型不含房间、好友或关系语义 |
| 框架范围 | 未新增 DI、消息总线、项目或第三方 .NET 依赖 |

## 3. 生命周期与 fallback

world 使用 `Created → Active/Suspended → Disposed` 显式状态。固定步长必须大于 0 且不超过 100 ms；非有限接触输入、越界强度、非法参数版本和非法双体参与者会被拒绝。

| 场景 | 实际结果 |
|---|---|
| `Hidden` | 不执行物理 step，输出不可见 frame，原因 `Hidden` |
| 降低动画 | 不执行连续物理 step，返回 authored pose，原因 `ReducedMotion` |
| 宿主失效 | 不执行物理 step并释放 native session，原因 `HostUnavailable` |
| native 缺失/加载失败 | 不抛出未受控异常，返回 authored pose，原因 `NativeUnavailable` |
| native probe 失败 | 释放 session 并返回 authored pose，原因 `NativeProbeFailed` |
| dispose 后 step | 抛出 `ObjectDisposedException` |

## 4. 先失败验收

实现契约前先加入 P2D-003 测试并运行筛选测试。测试因 `Dango.Desktop.Physics.Presentation` 命名空间不存在产生 `CS0234`，退出码为 1；实现后同一测试范围 9/9 通过。

验收覆盖：fake 重放确定性、Core 快照不变、局部输出限制、隐藏/降低动画/宿主失效、非法输入和步长、dispose、双体 cue 语义边界、native 缺失降级、固定 native DLL 通过共享接口运行。

## 5. 构建与测试门禁

| 门禁 | 实际结果 |
|---|---|
| `dotnet format` | 通过 |
| Release build | 通过，0 警告、0 错误 |
| Core tests | 20/20 通过 |
| Desktop tests | 81/81 通过，其中 P2D-003 9/9 |
| 动画 Python tests | 19/19 通过 |
| AP-005 manifest | 10 个动作，错误数 0 |
| Markdown 本地链接 | 通过 |
| `git diff --check` | 通过 |

## 6. 证据分层与下一步

- **静态门禁**：格式、Release 构建、单元/架构测试、动画资产回归和文档检查通过。
- **native 集成门禁**：固定 Box2D DLL 通过共享表现接口完成 lifecycle probe；缺失 DLL 受控降级通过。
- **实时动画行为**：未验证；本检查点没有软体环、连续 Box2D 形变或 WPF 动态渲染。
- **人工体验**：不适用；没有新增可视效果。
- **交付状态**：仅本地实施分支；未合并、未推送、未部署、未启动外部资源。

P2D-003/B3 达到完成条件。下一步是 P2D-004：在无 WPF 的固定步长测试中实现单团子软体环稳定性；本记录不授权自动进入下一检查点。
