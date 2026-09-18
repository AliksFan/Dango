# 第二批第一小步：点击与抚摸验证记录

更新：2026-09-18。设计、计划与恢复继续实施均获用户确认。用户在项目候选启动后明确回复“没问题，验收通过”，本批点击与抚摸整体人工验收通过。专项矩阵与历史自动门禁问题仍单列保留，整个 Phase 1 未收口。

## 范围与来源

- 分支 `codex/light-interaction`，实施起点 `72199ef93febaec88280fb2bd3c27c2fe594f3ca`。用户于 2026-09-18 授权整理并本地提交：先以 `59a77d7` 单独保存第一批继承快照，再以本记录所在提交保存点击与抚摸增量。未合并或推送。
- 第一批继承 33 个源码/测试/文档/脚本变更；清单及原始副本为 `artifacts/light-interaction/implementation-20260917/baseline/inherited-files.json` 和 `files/`。它们不是本批新增成果。09-18 逐项复核源树 33 个文件指纹，0 个差异。
- 本批增量：`LightInteractionController/Session`，共享 runtime 按实体反馈与代际，附件健康窄接口，Window、WorkerW WPF/proxy/overlay 输入与取消，DPI 位移换算及现有帮助文字。
- 普通移动到 3 DIP 转原物理拖动；短点双眼 40/10/80 ms 眨眼；Shift 按下分支在 600 ms 内水平净位移 16 DIP、竖向最多 8 DIP，双眼 Soft/0.35/1，成功步累计 600 ms 后释放。每实体共享 300/1000 ms 门，不排队。DeveloperReview 保留原入口。
- 无 native、DLL、物理参数、眼帽数学、设置 schema 或网络变更。历史 `p1-004-box2d-soft-body` 未修改。

## 证据与自动结果

恢复前日志仅作历史证据：L1 初始断言失败、L2 stub 下 13 项失败；原反馈 8 项和输入/会话 13 项曾通过。09-18 检查发现缺失实现后，经用户授权按原补丁恢复；以下全部来自恢复后重新运行。

证据根：`artifacts/light-interaction/recovery-20260918/`。

| 检查 | 当前结果 | 证据 |
|---|---|---|
| 恢复后的原输入/会话/反馈 | 21/21 通过 | `results/restored.trx` |
| 新宿主消息、捕获取消与 DPI | 6/6 通过 | `host-green/host.trx` |
| 本批扩大后的全部用例 | 32/32 通过 | `expanded/expanded.trx` |
| 隔离 Release solution build | 0 warnings/errors，最终再次构建一致 | `final/build.log`、`final/build-final.log` |
| 完整 Desktop | 354/355；唯一失败仍为旧 DLL 缺 `dango_box2d_self_bodies_create` 的 `SharedSelfWorldExportsAreAvailable` | `final/desktop/desktop.trx` |
| Core | 37/37 | `final/core/core.trx` |
| Physics 常规 | 32/32；明确排除 `DangoPhysicsStabilityTests` | `final/physics/physics.trx` |
| 最终输出本批复验 | 32/32 | `final/interaction/interaction.trx` |
| format | 退出 2，仍为 5 个历史问题文件；没有本批新增格式诊断 | `final/format-final.json` |
| diff whitespace | `git diff --check` 通过 | 最终复核 |

测试直接检查真实附件双眼帧、gaze 保持、自动恢复、相同输入下物理快照逐字一致和帽子数值等价（允许 1e-7 浮点误差）；覆盖冷却、停机前取消、未消费/播放中取消、跨 runtime/重置代际、其他实体路由、方向打断、物理故障锁存、附件故障时仍可拖动、取消提交失败必须显式重置，以及 10/20/40 ms 成功步与无推进的 UI 查询。

格式遗留为 `VerificationModels.cs`、`HatBodyFollowTests.cs` 的缩进，以及 `DangoSceneWindow.xaml.cs`、`DesktopDangoSceneOverlayWindow.cs`、`WorkerWWindow.xaml.cs` 的 UTF-8 BOM。三宿主文件虽为本批修改范围，但已逐项确认 HEAD 中原有 BOM，未顺手整文件清理。最终仅调整新增测试的 using 顺序，重新构建并复验 32 项；完整回归对应相同生产源码。

宿主测试使用编译后的 Window/WorkerW 清输入和 LostCapture 处理器、共享会话及 Win32 消息处理器；native proxy 挂载到测试自有隐藏 HWND，overlay 受控注入捕获状态。验证正常 Up 与异常失捕获区分、晚到另一实体回调、旧捕获通知、禁用交互、100%/150% DIP 阈值。**这不是对真实 Explorer、鼠标捕获、前台焦点或两种显示 DPI 的操作验收。** 首次宿主用例 5/6，失败源于对未显示 overlay 调用 EnsureHandle 时 WPF PresentationSource 为空；调整为隐藏测试父句柄的受控状态后 6/6，原失败日志保留。

## 保护核对

- 旧生产库 SHA-256：`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
- 统一模块 SHA-256：`00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。
- 两项与导入基线一致；没有重建、替换受保护库或隐藏缺失导出失败。

## 实时、人工及交付

2026-09-18 用户明确授权结束旧试运行实例并启动候选。核对旧 PID 10584 与已确认可执行路径后，仅结束该进程；确认无其他 Dango 实例，再从 `final/build/bin/Dango.Desktop/release/Dango.Desktop.exe` 无参数启动普通 Product，PID 6008。复核候选路径、窗口标题 `Dango`、非零窗口句柄、进程响应及实际加载的统一模块路径；模块 SHA-256 与已验证基线一致。没有打 ZIP、使用 review/smoke 入口或手动改写日常设置。精确路径、时间及观察见 `final/launch-20260918.json`、`final/launch-observed-20260918.json`。这些检查只证明候选进程及窗口已启动，不代表输入或视觉验收。

人工结论：2026-09-18 用户回复“没问题，验收通过”，确认本批点击与抚摸整体体验通过。该确认不附带逐项操作明细，因此不补写 Window/Wallpaper 图标上/下、前台/焦点/捕获、两种真实 DPI、Explorer 故障恢复的全矩阵通过记录。上述专项仍未逐项验证；Physics 稳定性长测未运行。历史唯一 DLL 导出失败与 5 个格式问题不因人工认可改写为通过。

本批普通 Product 候选启动为 PID 6008，本次提交整理不操作应用进程；旧试运行 PID 10584 已按授权结束，旧目录和文件保留。测试创建的隐藏夹具按 finally 关闭。继承快照和本批增量分开保存为两个本地提交；未合并 main、推送、部署、发布或清理旧资源。`final/protection-and-launch.json` 保留启动前状态与库指纹，启动后的实际状态以上述 launch 记录为准。
