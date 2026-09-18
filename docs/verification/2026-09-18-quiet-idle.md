# 第二批第二小步：安静待机验证记录

日期：2026-09-18。设计和实施计划均获用户确认后，从 `85d239e` 创建 `codex/quiet-idle`，实现自然眨眼与偶尔张望。**普通 Product 项目候选启动后，用户回复“通过”，本批整体人工验收通过。专项未验证项和历史门禁问题继续保留。**

## 范围与文件

- `QuietIdleController` 管理成功物理步上的稳定资格、随机间隔、动作期限、丢弃重抽与取消；`QuietIdleSceneController` 管理每实体状态和错误。
- `AttachmentSceneController` 为每实体代际固定注册独立自主 token；`StackedDangoSceneRuntime` 在附件消费前复核稳定和手动占用，并在消费后计时。使用原 Blink 与 Gaze，不改 router 仲裁或眼帽数学。
- `LightInteractionSession` 将有效 Down 至结束的候选占用交给 runtime，旧会话不能解锁新会话；Window/WorkerW 提供当前宿主资格，WorkerW 前台状态仅在变化时取消一次。协调器显示取消失败，控制中心增加帮助说明。
- 四个 `QuietIdle*Tests.cs` 文件覆盖纯调度、真实物理稳定、真实双眼帧和受控宿主。没有新增依赖、线程、网络、设置字段、native 构建或身体自主移动。

## 自动证据

证据根：`artifacts/quiet-idle/implementation-20260918/`。完整结果来自当前源码的隔离 Release 输出。

| 检查 | 结果 | 相对证据 |
|---|---|---|
| Q0 基线 | Core 37/37、Physics 常规 32/32、Desktop 354/355；唯一旧导出失败 | `baseline/` |
| Q0 真实落地稳定资格 | 1/1；真实核心可连续 500 ms 满足 ≤0.15，跳跃离地 | `stability/stability.trx` |
| Q1 先红后绿 | stub 下 7 项断言失败，实现后 7/7 | `q1-red-assertions/red.trx`、`q1-green/green.trx` |
| Q2 先红后绿 | 缺 runtime 接入时 3 项断言失败，实现后相关 43/43 | `q2-red/red.trx`、`q2-green/green.trx` |
| 扩大交互回归 | 55/55，含上一批 32 项；最终补充速度边界后另见完整回归 | `expanded/expanded.trx` |
| 最终 Release solution build | 0 warnings/errors | `final/build.log` |
| 最终 Core | 37/37 | `final/core/core.trx` |
| 最终 Desktop | 378/379；本批新增 24 项和上批交互 32 项全部通过；唯一失败仍为 `SharedSelfWorldExportsAreAvailable` 缺旧导出 `dango_box2d_self_bodies_create` | `final/desktop/desktop.trx` |
| 最终 Physics 常规 | 32/32；明确排除 `DangoPhysicsStabilityTests` | `final/physics/physics.trx` |
| format | 未通过；仅原有 5 个文件诊断，没有新增诊断 | `final/format.json`、`final/format.log` |
| 空白差异 | `git diff --check` 通过 | 最终复核 |

初始 Q1 stub 的未使用参数编译错误仅作为历史日志，不作为断言红灯证据；真正红灯见 `q1-red-assertions`。最终新增边界测试覆盖恰好 0.15 的稳定资格、超过阈值和 NaN 后重新等待。

真实帧验证包括 10/20/40 ms 步长、双眼约 2 DIP 张望、首次消费后 1 秒保持及释放回正、启用/关闭待机时物理快照相同、帽子数值误差 ≤1e-7；张望中跳跃立即释放并恢复物理纵向眼动。另覆盖手动候选全程占用、旧会话/宿主回调、捕获失败、取消容量拒绝锁存与显式恢复、物理故障冻结、Review 不调度，以及自主 Release 不清除手动 Gaze/EyePose。

宿主测试使用编译后的 Window/WorkerW 与受控隐藏夹具，明确注入当前宿主资格；后台状态 true→false→false 只取消一次，之后成功物理步可重新触发。**这些是受控集成验证，不是真实 Explorer、焦点、鼠标或 DPI 的桌面操作验收。**

格式遗留：`VerificationModels.cs`、`HatBodyFollowTests.cs` 的缩进，以及 `DangoSceneWindow.xaml.cs`、`DesktopDangoSceneOverlayWindow.cs`、`WorkerWWindow.xaml.cs` 原有 UTF-8 BOM。本批没有清理旧问题。

## 保护与候选

`final/hashes.json` 记录最终指纹：

- 旧生产库：`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
- 统一模块：`00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`；最终候选加载目录内副本一致。

两份库与 Q0 一致；旧实验工作树、预览和证据未修改。没有用替换 DLL 消除历史失败。

普通 Product 候选：`final/build/bin/Dango.Desktop/release/Dango.Desktop.exe`。用户明确回复“启动”后，核对上一批 PID 6008 及精确旧路径，仅结束该实例，再无参数启动本批候选 PID 31652。已核对实际路径、Dango 窗口、非零窗口句柄、进程响应及加载的统一模块指纹；见 `final/launch.json`、`final/launch-observed.json`。没有使用 review/smoke 参数、打 ZIP 或手动改写日常设置。这些检查证明启动成功，不替代视觉与交互人工验收。

## 尚未验证与交付状态

- 用户在本批候选启动后回复“通过”，确认自然眨眼与偶尔张望整体人工验收通过。未提供逐项操作明细，因此不补写两宿主、90 秒观察时长、各类接管、后台焦点、隐藏恢复和层级切换全矩阵通过记录。
- 第二种真实 DPI、完整故障/切换回滚矩阵、碰动与非选中多实体专项：未逐项验证，不以受控测试或上一批认可推定通过。
- Physics 稳定性长测明确排除，没有运行。
- 本批整体人工验收通过；不等同专项门禁全绿或整个 Phase 1 完成。
- 本批实现、测试、设计计划及验收记录随本次本地提交保存；未合并、推送、部署或发布。上一批 PID 6008 已按授权结束，本批 PID 31652 保持运行；测试自有隐藏夹具在 finally 关闭，没有新增持久服务、端口或数据。
