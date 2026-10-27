# 三栏位普通房补验与本地 main 整合

日期：2026-10-27。状态：**检查点竞争修复及普通房补验通过，准备执行已授权的本地 main 快进。**

用户指令：“验证完普通房相关/修复（如果有问题）就合并到main吧”。本指令授权普通房验证、必要修复及通过后的本地 main 整合；不包含推送、安装、部署、日常存档操作或清理工作树。前台时段依 AGENTS.md 第 4 节另行取得授权。

## 1. 基线与保护

- 起点为专项 `8fab7b0`，本地 main 为 `d929a8e`，main 是专项祖先，可快进。main 只有已有 `AGENTS.md` 未暂存修改，专项不修改该文件。
- main 上 `AGENTS.md` 初始 SHA-256 为 `82FFA6893B84E7F439B58B64F1E28D5C3FA6D3617B58DD088DDC91F76872BFB7`；受保护 DLL 仍为前轮记录的 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。合并前后重新核对，不暂存、覆盖或丢弃既有修改。
- 继续使用桌面界面规范 v0.1 第 3、4.2–4.3 节的预览、真实反馈及输入规则；本轮没有设计或生产 UI 改动。

## 2. 检查与增量

- 无窗口预检：Desktop 身份／存储／首页／Host／普通房名字／handoff **56／56**，逐团子恢复点 **11／11** 通过；日志 `three-slots-ordinary-preflight.trx`、`three-slots-ordinary-resume-preflight.trx`。
- 原普通房回归仍使用空身份或未命名旧档，并断言编辑覆盖旧 `appearance.json`。已迁移到具名 roster 身份，保存断言目标 profile，核对旧原件字节不变；保留原 owner、首帧和动作断言。变更文件为 `LocalRoomProductTests.cs`、`LocalRoomWindowReturnAppearanceTests.cs` 和 `LocalRoomAppearanceIntegrationTests.cs`。
- 新增 `OrdinaryRoomDangoIntegrationTests.cs`：A／B 具名与不同颜色，实际普通房出入、同一时刻一个 owner、房内及门廊禁止换人、安全首页切换、各自合法位置 500／1500、共享 Near 视角、真实检查点版本增长、B 不改 A 文件、重启保持选择且不自动入房。后续补充设置页返回与新建／旧档承接两种起点。
- 独立只读审查未发现已确认的普通房接线缺陷；指出种子位置不能单独证明真实保存，已补 `ResumeVersion` 必须增加的断言；门廊禁换亦增加强制点击负例。审查不替代实窗结果。

## 3. 首两轮实窗结果与失败

测试使用正式 Host、WPF 窗口及本地 native 模块，在合成数据根运行；控件通过实际 WPF 事件调用，不声称验证了完整物理鼠标／键盘输入或 GPU 呈现。全部夹具自行关闭所创建窗口；未操作用户的原评审根或日常存档。

1. 用户允许后台通过后前台约 5 分钟。新 A／B 完整链最初版本 **1／1** 通过（7 秒有效测试）；原普通房组 **1／5**，另 4 项失败。日志 `three-slots-ordinary-live-first.trx`、`three-slots-ordinary-live-regression.trx`。本时段结束后未继续打开测试窗口。
2. 原 4 项含固定 350–450ms 等待；取消返程用例还未保证目标首帧仍待完成。已改为等待实际目标／健康帧／owner；取消用例先使目标不可呈现并断言 Pending，再验证取消、隐藏与 owner 释放。没有放松这些结果断言，未延长产品 3 秒超时。增加失败状态诊断。
3. 用户另允许约 3 分钟复测：原 4 项通过，增强后的 A／B 链通过，但 `WindowReturnAllowsFaceApplyAndReentryRendersSavedAppearanceOnTheSameEntity` 在“打开控制中心→设置→返回→门廊”首次返程发生首帧超时，回退到 Window。整组 **5／6**，56 秒有效测试；日志 `three-slots-ordinary-live-final.trx`。该用例在前一组曾通过，不能仅归因于固定等待或平台。
4. 已结束第二轮前台。为未关闭的返程超时增加有界状态采样：只在失败时输出测试窗口可见／激活、门廊父树／大小、worker metrics、epoch／revision／pending display、目标代际与 owner；不记录桌面内容，不增加自动重试或改产品超时。诊断首次编译发现插值条件缺括号，已修正并重新构建通过。

至此尚无本轮新增生产修复；不能将旧测试更新或一次 A／B 通过写成普通房全部验证完成。壁纸模式、DPI／高对比等原延期边界未在本轮扩大。

## 4. 检查点与恢复确认的竞争

用户另授权最多 8 分钟。带诊断的设置返回用例及新建／旧档两条 A／B 链 **3／3 通过**，日志 `three-slots-ordinary-diagnostic-live.trx`。随后整个窗口组 **6／7**，日志 `three-slots-ordinary-full-live-final.trx`：`ActualWindowHandoffWaitsForFrameAndPreservesEntityThroughHideReturnAndZoom` 在隐藏后立即恢复的 Window 确认失败，房间已有健康帧，但 admission 报房间状态暂不可核对并回退 Hidden。此前一次门廊首帧超时本轮未重现，原因未确认，不把两种失败混为同一原因。

静态和独立并发审查确认一个可达竞争：`HideLocalRoom` 异步保存检查点，`RoomResumeStore.SaveForDango` 在写入期间持目录独占锁；恢复房间的健康帧确认也会立即申请同一锁，却没有先等待本机写任务。独占锁不排队，可能把正常本机写入误判为 admission 失败。本轮实窗错误与该路径高度吻合，但原现场没有记录异常类型，不能把具体异常归因写成已捕获事实。

新增无窗口 `LocalRoomCheckpointHandoffTests` 控制真实 Host 的检查点任务：修复前 **0／4**（均越过未完成写入直接确认），日志 `three-slots-checkpoint-order-before.trx`。生产最小修复仅在 `CompleteLocalHandoff` 的普通房 Window 确认路径异步等当前／追加的检查点任务，每次续体先核对 disposed、Pending、generation、target，随后重新取 admission guard；不跨 await 持锁，不阻塞 UI，不重试存储，也不延长三秒超时。等待任务失败／取消转为受控失败，过期续体直接退出，保存结果原有警告仍保留。

修复后无窗口新 4 项及相关专项共 **60／60** 通过，日志 `three-slots-checkpoint-order-after.trx`，覆盖旧代际在成功／取消／IO 异常完成后不重新出场，以及等待期间追加的新任务。正式 Release 构建复核另行记录。独立审查未见生产实现必须修改项；补强实窗取消用例的 Dispatcher 屏障，避免在晚到续体运行之前就断言成功。

新增实际窗口持锁用例：目标房间必须先呈现健康帧，同时人为保留本机写入持有的真实目录锁，验证确认等待；释放后恢复，或先取消再释放，均不得误报／重开。已在随后授权时段通过，见下一节。

## 5. 修复后最终验证

用户再允许最多 8 分钟。实际窗口阶段自 04:54:21 UTC 起，04:59:19 UTC 前完成；测试窗口均已由夹具关闭，没有继续使用屏幕或操作用户原有窗口／存档。

| 门禁 | 最终结果 |
|---|---|
| 身份／存储／首页／Host／名字／handoff 与新异步序列检查 | **60／60，通过，0 跳过**；`three-slots-checkpoint-order-after.trx` |
| 逐团子恢复点与共享相机 | **11／11，通过，0 跳过**；`three-slots-ordinary-resume-preflight.trx` |
| 正式 Host 实窗／真实 native 帧 | **9／9，通过，0 跳过**；新建／旧档各一条 A/B＋设置返回＋独立落点＋共享视角＋重启链；实际目录锁占用下的恢复／取消两项；原外观保存／重入一项；原隐藏恢复／取消、首帧超时重试、故障恢复及控制中心在外四项。`three-slots-ordinary-fixed-live.trx` |
| 正式目录入口 | **1／1 通过**；具名身份正常发布本地目录，不伪造正式房间授权，`three-slots-ordinary-catalog-live.trx` |
| 曾偶发的设置返回单项确认 | **1／1 通过**，是上表的一项重复，非另一个独立覆盖；`three-slots-settings-return-confirmation.trx` |
| 正式 Release 构建 | **0 警告／0 错误**；未替换受保护 DLL |
| 静态／并发审查 | 差异检查通过，异步等待／代际核对经独立只读复核；取消用例等待 Dispatcher 屏障后再判定无迟到重开 |

上述是自动实窗工程验证，控件走真实 WPF 事件及真实 owner／首帧门禁；不扩大为全物理鼠标键盘、GPU 呈现或独立人工观感验收。先前那一次门廊首帧超时在诊断和最终组以及单项复测均未重现，具体原因仍未确认，保留原失败证据，不宣称已由检查点修复解释。可控锁竞争和等待顺序则有负控及修复后证据。

测试命令继续用 `-p:WarningsNotAsErrors=NU1903` 保留已有 SignalR 审计告警，未关闭审计或升级依赖。两项旧 480×270 组件布局失败、旧来源兼容限制和未明 IO 历史仍沿用主验证记录；壁纸模式、全 DPI／高对比、多人／账号显示名继续后置，不将本次普通房通过扩成完整 Windows／联网阶段验收。

## 6. 整合门禁

必须先关闭上述返程超时、重跑受影响普通房与无窗口检查，确认正式构建和差异检查通过，再执行已授权的本地 main 整合。合并前复查 live refs、main 的已有修改、索引及保护哈希；若 main 已前进则重新核对，不强制覆盖。用户已提供合并授权，不因文档中的旧“未授权”历史记录再次询问。
