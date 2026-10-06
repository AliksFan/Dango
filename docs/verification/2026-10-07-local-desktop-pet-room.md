# 桌宠房间本地单人实施与验证

日期：2026-10-07。分支 `codex/desktop-pet-room`，基线 `main@8ff78e0`。用户明确要求开始开发；范围见[实施计划](../planning/2026-10-07-local-desktop-pet-room-plan.md)。当前为可运行候选，人工验收待完成，不关闭整个 Windows、Phase 1 或正式多人房间阶段。

## 本轮行为

- 门廊新增独立「桌宠房间／本地单人」门，保留普通房门及 132 DIP 接近限制。W 和点击分别请求对应房间，不构造账号、房间 Authority 或网络输入权限。
- 透明 WorkerW 壁纸层不绘制普通房背景、岛或软垫。只承载本机的一只团子，复用同一 runtime、实体、外观和交互；主屏 WorkingArea 内收 16 DIP，使用真实原生地面与侧墙。
- 新健康物理步和身体帧应用后，再经过后续渲染确认及当前代际确认才揭示目标和开放输入。门廊、普通房和桌宠至多一个活动帧 owner；回门廊时源画面先冻结，待目标健康帧后收起。
- 自身右键提供返回门廊、隐藏和控制中心；托盘按当前房型显示恢复入口。隐藏保留本次选择及 X，清输入、释放 owner、停止物理；恢复要求新帧。恢复挂接超时保留选择和重试入口。
- 桌宠固定在图标下方；此房间内禁用窗口模式及图标层级切换，并显示实际下层状态。其它宿主的保存偏好保留。没有新增依赖或房间／位置持久化。

## 失败证据及修复

结果均保存在 `artifacts/desktop-pet-room/results/`，没有删除红例。

| 记录 | 实际结果 | 结论／修复 |
|---|---|---|
| `desktop-pet-entry-before.trx` | 11 失败，0 通过 | 原实现没有独立入口和接线；测试可编译后以断言失败 |
| `desktop-pet-first-green.trx` | 19 通过，1 失败 | 文件名不表示通过；参考框夹边导致首个 Tick 再次 rehost 并清移动意图。只扩展控制参考域，真实安全地面和侧墙不变 |
| `desktop-pet-cancel-position-before.trx` | 3 失败 | 首入 Pending 返回未释放 Worker owner；隐藏取消未清模式锁；正常隐藏恢复从 X≈432 跳到入口 X≈1084。修复源释放、上下文清理及隐藏前位置捕获 |
| `desktop-pet-restore-timeout-before.trx` | 1 失败 | 隐藏后的恢复超时丢弃已选桌宠房间。按已有选择保留 Hidden 和托盘重试 |
| `desktop-pet-layer-setting-before.trx` | 1 失败 | 设置允许上层但实际宿主固定下层。修复上下文限制及显示，保留原偏好 |

另补充失败后／冻结源的 Shell timer 停止和旧回调失效门禁，防止隐式重挂及重新取得 owner。自身轮廓命中拒绝透明矩形空白角、无效轮廓和不可见身体。

## 最终自动与集成结果

最终桌宠及设置组 `desktop-pet-final-new-real-shell-settings.trx`：**42/42，通过，0 失败，0 跳过**。

| 用例类 | 通过 |
|---|---:|
| DesktopPetRoomEntryTests | 11 |
| DesktopPetPresentationGateTests | 4 |
| DesktopPetRoomPhysicsTests | 6 |
| DesktopPetRoomReviewEntryTests | 1 |
| DesktopPetRoomSettingsTests | 3 |
| DesktopPetRoomProductTests | 6 |
| 原有 ProductSettingsPageTests | 11 |

桌宠新专项合计 **31/31**。其中 6 项产品用例显式设置 `DANGO_RUN_DESKTOP_PET_REAL_SHELL=1`，在真实 Windows WorkerW、原生实体和帧调度器上执行：进入／隐藏／恢复／返回、普通房互斥、首入取消两路径、物理不可用、隐藏恢复挂接超时后重试。超时用例仅替换该失败尝试的宿主发现函数；成功进入和重试使用真实 Shell，不制造健康帧。这不证明 Explorer 实际重建恢复，也不代替前台鼠标体验。

原有受影响组按原断言、按类独立进程复验 **65/65**，零失败／零跳过，结果文件为 `desktop-pet-final-<类名>.trx`：DesktopHostCoordinator 3、DesktopSceneSession 2、HostFrameScheduler 10、PorchSceneIntegration 16、UnifiedLiveHome 12、ProductHomeInteraction 10、LocalRoomProduct 5、LocalRoomAppearanceIntegration 5、LocalRoomWindowReturnAppearance 2。连同原有设置组 11 项，受影响旧用例合计 **76/76**。

保留合批不稳定记录：`desktop-pet-existing-regression-r1.trx` 为 63/65，原因是旧单门断言需同步为两门、旧模块夹具未指定；前者只更改 room-review 门数，face-review 仍无门，后者补正确旧模块。r2、r3 均为 63/65，但失败项分别落在门廊和首页的全局焦点前置；原样专项／按类复验通过。测试 helper 没有等待屏外 WPF 窗口取得焦点，本轮没有更改其 guard 或断言；准确根因仍未完成诊断，不宣告合批稳定或全量 Desktop 通过。

静态检查：限定本轮 C# 的 `dotnet format whitespace --verify-no-changes` 与 `git diff --check` 通过。隔离 Release 候选 `review-r2` 构建 **0 警告／0 错误**。

## Native 与候选入口

- 显式构建模块 SHA256：`BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`；构建前后及候选复制件一致。
- 受保护 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` SHA256：`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；未替换。
- 原有旧模块回归夹具 SHA256：`00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`；通过 `DANGO_PHYSICS_BASELINE_PATH` 显式指定，不把 BFDD 复制件当作旧模块。
- 候选：`artifacts/desktop-pet-room/review-r2/bin/Dango.Desktop/release/Dango.Desktop.exe --desktop-pet-room-review`。独立实例标识和 `desktop-pet-room-review-state` 设置目录，不读取或更新日常安装设置。
- 重建入口：`eng/run-desktop-pet-room-review.ps1`，调用者提供新的 `RunName` 及明确的已批准 `NativeModule`；已有产物不覆盖。当前候选窗口作为用户人工验收现场保留。测试进程已结束；隔离测试设置和构建产物保留在 artifacts。本轮未启动网络服务或数据库。

## 人工与交付边界

按[人工清单](2026-10-07-desktop-pet-room-checklist.md)操作：首页 W 进入门廊，D 走近右侧桌宠房门，再 W 或点击进入；对团子右键选择房门、隐藏或控制中心，也可用托盘恢复。

仍未验证：真实点击／轻抚／Shift 与右键手感、桌面图标及其它应用前台输入、透明视觉边缘、实际跳跃与可选键盘焦点切换、显式重新生成、其它缩放／显示器、热插拔、Explorer 实际重建、长期性能。当前仅主屏安全区域；不声称多屏交付。

本轮为独立本地分支候选，人工待验；未合并 main、推送、更新日常安装或部署。LAN、正式账号／数据库／准入／固定访客、多人和普通房壁纸继续各自独立范围。
