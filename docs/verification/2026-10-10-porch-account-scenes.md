# 门廊账号房间场景验证

日期：2026-10-10。范围依据[已确认设计](../planning/2026-10-10-account-sync-nickname-and-porch-design.md)。本记录描述门廊迁移的技术证据，主观效果、手感和流畅度尚待用户验收。

## 实现范围

门廊保留本地普通房和桌宠房，并增加本人账号目录生成的真实房门、建房牌、访客信箱、旧门册及在线状态牌。团子走近目标按 W 或点击近处目标，才展开目标旁边的场景操作区；现有房间及在线操作内容已放入门廊，不使用账号居中浮层作为房间入口。每个目标提供独立 Button、稳定 AutomationId 和 UIA InvokePattern。

目录每页请求 4 条，匹配现有固定物理世界；旧门册承载分页、目录切换、刷新及恢复。目标只能读取当前确认页里的房间，近距检查为 132 世界单位。目录明确标注最后确认状态，实际请求继续经过现有 controller 和服务端权限检查。

创建、修改、解散、七天恢复、公共码、成员、连接恢复、接管、离房和结束租约继续复用原业务流程。危险操作需明确确认；未知写入保留原请求键和正文，不能借切换场景目标绕过核对。场景表单编辑、关闭、失焦及转场清空方向输入，关闭后恢复安全焦点；窄视口操作区有界并可独立滚动。

## 已执行技术检查

| 检查 | 实际结果 | 本工作树回执 |
|---|---|---|
| 门廊及原房间组件、实窗、UIA peer、CRUD／恢复、分页、陈旧目标、离线本地两门、窄视口及主页相关回归 | 87／87 通过，零跳过 | `tests/Dango.Desktop.Tests/TestResults/porch-scenes-final-ui.trx` |
| 真实隔离物理模块：同一实体从首页进入门廊，走到建房牌，展开场景表单后继续健康绘制 | 1／1 通过 | `tests/Dango.Desktop.Tests/TestResults/porch-physical-final.trx` 内 `SameRealPhysicalPetWalksToBuildSignAndKeepsRenderingUnderTheSceneForm` |
| 距离错误实现反例：把交互上限改为 WorldWidth 后执行唯一距离测试 | 恰好 1 个预期失败，远处目标被错误打开；原始源码由 finally 恢复 | `tests/Dango.Desktop.Tests/TestResults/porch-distance-counterexample.trx` |
| 恢复后重新构建并执行同一距离测试 | 1／1 通过 | `tests/Dango.Desktop.Tests/TestResults/porch-distance-restored.trx` |

这些场景测试使用受控 fake 管理和准入服务，验证实际 WPF 窗口、真实场景布局及业务请求边界；UIA peer 检查不能等同独立 UIA client 或正式网络端到端验证。物理测试使用真实隔离 native 模块，并检查实体 ID 连续、绘制可见、运行时无错误和选牌零创建。

实窗截图位于本工作树 `artifacts/test-runs/3e7bf2e21c2e4c01adc79ba31a8bdcd5/porch-account-scenes/same-real-pet-inline-build-form.png`。它记录实际同一团子与目标附近操作区，不是概念图。

## 未验证项与环境边界

同一物理回执中的 `OwnedNativeKeyboardOpensAndWalksThePhysicalPorch` 已实际尝试，但 Windows 拒绝将该精确隔离测试窗口激活为前台；测试以 Inconclusive／NotExecuted 结束，没有注入原生键盘，不能报告通过。阻碍为 `Windows refused foreground activation of this exact isolated scene; no native input was injected.` 该结果不影响已执行的受控物理移动检查，也不能代替真实按键证据。

一次较宽的原生回归因包含长物理运行而被中止；截至中止有 59 项通过及 2 项新增测试 fixture 失败，该轮不是完整通过证据。`RealDoorsHaveStableAccessibleIdentitiesAndDistantInvocationDoesNotOpenAnything` 在 UIA peer Invoke 后立即断言 `IsInPorch` 时为 false；`SameRealPhysicalPetWalksToBuildSignAndKeepsRenderingUnderTheSceneForm` 期望最近目标 `porch-create`，实际为 `local-single`。失败回执为 `porch-native-regression.trx`，不能把它们归为既有基线或 Win32 前台激活阻碍。

最终 fixture 已在异步 Invoke 后等待真实转场，并在物理操作前显式设置 Stage 焦点；两项分别已包含在上述 87 项及物理单项成功证据中。联合接入新的账号实现后，又只定向运行这两项，在同一进程 2／2 通过、零跳过，回执 `porch-previous-two-failures-directed.trx`。这是当前两项的通过证据，不声称初始较宽回归完整通过，也不将先前未捕获的焦点状态推断为已证明的唯一根因。

本阶段未连接数据库、没有修改正在运行的 R2 验收资源，没有真实账号或房间持久写入。受保护 `dango_box2d.dll` 的 SHA256 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。只有已确认归属的本工作树测试进程参与运行和收尾。

正式多人移动、动作和可玩画面不在本次迁移范围内，读取房门、成员列表或租约不构成多人画面准入。用户仍需判断目标布局、交互手感和使用流畅度。没有 main 合并、远端推送、部署、日常安装更新或生产 DLL 替换。

## 首页昵称布局补查

最大合法昵称为 24 个字素。实际窗口探针发现旧的无界字符串按钮会使 520×820 的账号按钮超出窗口，430×500 及 390×740 的设置按钮出现在负 X；四个视口中的三个确实失败，回执为 `porch-nickname-max-probe.trx`。

账号按钮改为有界的单行 TextBlock，过长显示省略号，Tooltip 和 UIA Name 保留完整昵称。宽度按导航当前可用空间收缩，并保留既有标题栏区域；不会靠截断底层昵称、隐藏设置或移动窗口解决。

新增 `HomeAccountNicknameLayoutTests` 在 336、390、430、460、520、960 宽的七种实窗尺寸逐一检查短昵称、24 中文和 24 emoji，以及注销后的入口；检查全部工具按钮在窗口内、品牌无重叠、标题栏保留区域、完整辅助名称及 Tooltip。七项通过，回执 `home-nickname-layout-fixed-r2.trx`。仅禁用响应式宽度预算的反例在 460×820 恰好一项失败，回执 `home-nickname-responsive-counterexample-r2.trx`，证明固定 MaxWidth 仍不足以保留导航安全区域；原始源码由 finally 恢复。

恢复后重新构建并联合执行昵称布局和非原生门廊场景测试，15／15 通过、零跳过，回执 `home-nickname-and-porch-restored.trx`；`git diff --check` 通过。
