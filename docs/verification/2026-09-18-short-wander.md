# 短距离自主移动：实施中验证记录

> 2026-09-19：用户认为此处记录的原候选距离太短，要求至少屏宽 10%。当前调整与新证据见[距离修订记录](2026-09-19-wander-distance.md)；下文保留 09-18 版本历史结果，不代表新距离已经人工验收。

2026-09-18，用户明确授权实施，并确认“左右 16 DIP，上下完整可见、保持原落地位置”的边界修订。分支 `codex/short-wander`，起点 `b56a5b3`；设计与计划分别见 [设计](../planning/2026-09-18-short-wander-design.md)、[计划](../planning/2026-09-18-short-wander-plan.md)。实现、隔离候选构建及最终回归已完成，无新增测试失败；本批普通 Product 候选已按用户授权启动，人工验收待完成。

证据根：`artifacts/short-wander/implementation-20260918/`。

| 检查 | 结果 | 证据 |
|---|---|---|
| 默认真实物理输入 | 12/12：左右、24/48 DIP 目标、10/20/40 ms 成功步；含 500 ms 卡住退出、2 秒驱动上限 | `gate/gate.trx` |
| 位移与减速 | 最终位移 23.89–48.50 DIP，最大释放后偏移 0.26 DIP，最大单步 1.41 DIP，恢复连续稳定 0.50–0.52 秒 | `measurements.json` |
| 纯调度先红后绿 | stub 下 6 项断言失败，实现后 6/6 | `controller-red/red.trx`、`controller-green/green.trx` |
| 原有交互及眼睛待机 | 56/56，无新增失败 | `integration/integration.trx` 内原有用例 |
| 初次组合验证 | 总计 76/80，4 项新增用例因固定张望与移动时刻反复冲突而无法开始；失败日志保留 | `integration/integration.trx` |
| 可视几何 | 实际渲染身体与三种帽子包含于安全包围盒，1 项通过；近边缘运动初测同受固定时刻影响失败 | `geometry/geometry.trx` |
| 默认桌面位置 | 失败：默认落地距视口底边约 10 DIP，不满足四边 16 DIP，无法启动 | `placement/placement.trx` |
| 修正夹具后的运行时复验 | 8/9；唯一失败仍为默认桌面位置。真实左右移动及停止、眼睛恢复、手动抢占、隐藏/旧宿主/重置/多实体、近边缘滑行、可视几何和手动物理对照通过 | `runtime-isolated/runtime.trx` |
| 获批边界修订后的默认位置 | 3/3；覆盖 10/20/40 ms、480×360 较小窗口、24–48 DIP 方案中的 48 DIP 目标及驱动/滑行完整可见 | `placement-approved/placement.trx` |
| 最终 Release solution build | 0 warnings/errors | `final-build.log` |
| 最终 Desktop | 409/410；新增 31 项全部通过，连同原交互/待机共 87 项通过；唯一失败仍为 `SharedSelfWorldExportsAreAvailable` 缺旧导出 `dango_box2d_self_bodies_create` | `final/desktop/desktop.trx` |
| 最终 Core | 37/37 | `final/core/core.trx` |
| 最终 Physics 常规 | 32/32；明确排除 `DangoPhysicsStabilityTests` | `final/physics/physics.trx` |
| 最终 format | 未通过，仍只有原有 5 个文件诊断，没有本批新增项 | `final/format.json`、`final/format.log` |
| 文档与空白 | 相对链接有效，新增文件无尾随空白，`git diff --check` 通过 | 最终检查 |

固定时刻夹具已将张望间隔由 15 秒固定为 25 秒，以隔离“移动本身”验证；30 秒移动与 15 秒张望重复碰撞时跳过属于设计规定，不改生产规则。专门的 Busy 重排测试继续保留。修正后 8/9，仅默认位置的设计冲突失败。

默认位置失败是独立设计冲突：900 DIP 高视口中，保守可视包围盒底边约 890.006 DIP。用户确认后将 16 DIP 边距限定为左右，纵向保持既有落地位置并要求完整可见；对应真实位置复验 3/3。历史失败保留。没有用挪动宿主、改变坐标或地面参数掩盖此问题。

当前实现包括小型调度器、身体与帽子保守包围盒、桌面场景独立连续水平输入，以及 runtime/WorkerW 的取消接线和控制中心帮助。原有手动方向/跳跃/下压/拖动仍优先，零自主输入对照保持物理快照相同；多实体或 Review 不调度。

包围盒采用实际身体轮廓控制点凸包，并包含三种已有帽子素材控制点在最大形变、当前姿态及 1.35 倍尺寸下的保守范围。帽子素材的控制点界限记录在 `WanderVisibleBounds` 注释；未来新增素材时必须同步扩展范围及包含测试。测试没有改变身体/眼帽数学。

最终完整回归无新增失败；完整实时宿主矩阵或人工验收未完成，本批已从普通项目输出启动供人工验收。受保护 DLL 未重建/替换，前后指纹分别记录于 `baseline-hashes.json`、`current-hashes.json`。

本批未提交、合并、推送或部署。用户明确同意替换上一批实例后，核对旧 PID 31652 及路径，仅结束该实例并启动本批 PID 31740；旧目录、实验工作树和证据保留。没有新增持久服务、端口或用户数据。

## 候选与未验证边界

普通 Product 输出为 `final/build/bin/Dango.Desktop/release/Dango.Desktop.exe`。用户同意后已结束上一批安静待机候选 PID 31652，无参数启动本批 PID 31740。已核对实际可执行路径、Dango 窗口标题、非零窗口句柄、进程响应及实际加载的统一模块 SHA-256；证据为 `final/launch.json`、`final/launch-observed.json`。没有打 ZIP、使用 review/smoke 或手动改写日常设置。窗口保持运行，启动检查不替代真实交互和观感验收。

`final/hashes.json`：旧生产库 SHA-256 为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；统一模块及本批候选副本均为 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`，与实施前一致。

待验收：普通窗口与 Wallpaper 的真实连续至少 3 分钟观感、后台焦点、近边缘移动、操作接管、隐藏与切宿主恢复。第二种真实 DPI、完整故障恢复矩阵及稳定性长测未验证。受控测试证明输入/状态/物理帧，不证明真实 Explorer 与鼠标键盘交互观感。整体 Phase 1 未完成。

格式历史问题仍为 `VerificationModels.cs`、`HatBodyFollowTests.cs` 的缩进，以及 Window/WorkerW/overlay 三宿主文件的 UTF-8 BOM；没有整仓清理这些问题。
