# 家具编辑接入主程序普通房：实施与验证

日期：2026-10-10。状态：**I0–I3 本地实现已完成，最终源码 Release 回归 235/235 通过；I4 真实产品往返、保存、独立进程恢复及完整 600 秒有效观察已通过，正常释放断言通过。实际主程序候选已打开供用户操作，人工验收与 OS DPI／辅助设置／既有 59 FPS 门禁待完成，阶段尚未验收。**

依据[接入计划](../planning/2026-10-10-furniture-room-integration-plan.md)与[I0 合同](../planning/2026-10-10-furniture-room-i0-contract.md)。用户批准主程序普通房、本地单人范围并回复“开始实现”，授权本轮实现、隔离构建、测试、真实候选窗口与本地提交。实现分支为 `codex/furniture-room-integration`，起点 `4765435e`，包含家具来源 `06c367f` 和批准的主线界面规范 `2acb532`。原独立家具试窗人工通过不能替代本轮验收。

## 实际接入

- 使用产品现有 `DesktopSceneSession → StackedDangoSceneRuntime → DesktopPhysicsScene` 世界和 `HostFrameScheduler`。新协调器借用该世界；编辑开关不创建实体、重置、换场或秘密 Step。
- 门廊面对本地普通房门按 E，复用正常入房及渲染首帧确认后开编辑；房内 Esc 菜单提供布置入口。重复按键、错误门、桌宠门及未确认首帧不能开编辑。
- 三段侧栏、两列家具缩略图仓库；选择、拖动预览、方向微调、确认添加／移动、直接删除、本人手套、右键镜头。用户明确仓库须显示实际家具图片，操作工具仍用图标。退出恢复进入前镜头。仅空布局或一件官方木平台，参数显示后续开放。
- 文件单独保存到数据根下 `rooms/local-ordinary-room/furniture-layout.json`。房内 X 与距真实碰撞地面的高度独立于相机、DPI、origin，绘制和逆变换共同使用当前 GroundY 与身体对齐。
- 预览 native 检查最多 20 Hz；提交在唯一 owner 步边界按最新身体 Prepare 复核。真实文件临时写入、lease、原子提交、Apply 与共同帧确认沿已批准合同。编辑入口也核对实际 lease，暂时争用可重试，提交仍重新获取租约防止竞态。
- 隐藏保留运行绑定。真正离房先卸载真实碰撞，再换场；卸载不保存空布局或增版本。取消入房的首帧也经过卸载与健康门廊交接。迟到故障先校验 generation。
- 保存中最小化恢复可呈现窗口，使原 owner 能结清；拒绝退出不提前改变 handoff。已保存未应用／保存未知保留核对恢复；运行未知停止 owner、保留明确故障，不自动重建世界或放进空房。
- 实际关闭验证发现 Home 延迟 Unloaded 仍引用已释放 runtime。退出前增加永久解绑，保留原 `ReleaseLiveScene` 临时停止语义，并增加 `Host.Dispose → Dispatcher Pump` 真实反例。

本轮未接入球、多家具、可调参数、圆形新身体、桌宠家具、普通房壁纸家具、自定义 PNG、账号保存、服务端或多人。

## 文件与界面规则

Core 增加显式 Trial／OrdinaryRoom 合同，保持旧 API 默认 Trial。Desktop Store 按对应合同验证；新增普通房协调器、实际世界 bridge、渲染及编辑 panel；产品门廊、菜单、Driver、Window、host 换场接线。`OfficialWoodPlatformVisual` 将已验收木平台画面保存为冻结的共享 ImageSource，场景与仓库 Image 使用同一图片，避免为仓库另画抽象图标；碰撞矩形仍独立。新增 `eng/run-ordinary-furniture-room-review.ps1` 与 `OrdinaryRoomFurnitureVerification`，后者驱动实际产品类，原试窗没有嵌入正式房间。

界面规范 v0.1 §2–6：复用 ProductHome 语义资源与图标模板，暖白／棕色、32 DIP 操作图标、中文提示及辅助名称、常驻细滚动条、固定确认与恢复区域。仓库是内容项，按用户补充采用实际家具图片缩略图，不用操作线性图标。业务例外是逐操作发布、取消只撤预览、直接删除及场景滚轮镜头。未批量迁移现有主题。实际 OS DPI、高对比与减少动画仍分别留作验收项。

## 自动门禁与失败证据

| 验证 | 实际结果 | 证据 |
|---|---|---|
| I0 Core 家具合同／操作 | 89/89，通过 | `artifacts/furniture-room-i0-results/i0-core-furniture.trx` |
| I0 Store 真实文件／租约 | 42/42，通过 | `artifacts/furniture-room-i0-results/i0-store-furniture.trx` |
| 最终受影响桌面回归 r6 | 210/210，通过，0 跳过 | `artifacts/furniture-room/results-r6/ordinary-room-r6.trx` |
| 原生实时家具、静态平台与统一模块 r2 | 68/68，通过，0 跳过 | `artifacts/furniture-room/physics-results-r2/ordinary-furniture-physics-r2.trx` |
| 退出解绑后局部回归 r7 | 77 通过、2 失败、0 跳过；含新退出反例通过 | `artifacts/furniture-room/results-r7/ordinary-room-disposal-r7.trx` |
| r7 两个失败方法复核 r8 | 9/9，通过；一项补齐独立旧模块，一项未改源复测焦点相关输入 | `artifacts/furniture-room/results-r8/ordinary-room-disposal-recheck-r8.trx` |
| 最终源码 Debug 扩大复核 r9 | 63 项已通过后主动终止，测试运行 Aborted；整体未通过 | `artifacts/furniture-room/results-r9/ordinary-room-final-r9.trx`、`stall-diagnosis.json` |
| r9 慢的既有桌宠配饰窗口 Debug 单独复测 r10 | 5/5，通过；48.05 秒，未修改产品源 | `artifacts/furniture-room/results-r10/slow-desktop-pet-isolation-r10.trx` |
| 家具与退出相关 Debug 复核 r11 | 69 通过、1 失败后因首帧取消方法 90 秒超时中止；整体未通过 | `artifacts/furniture-room/results-r11/ordinary-room-final-r11.trx`、`configuration-diagnosis.json` |
| 手套／取消方法 Debug 隔离 r12 | 手套方法 90 秒超时中止，未通过 | `artifacts/furniture-room/results-r12/handoff-glove-isolation-r12.trx` |
| 最终源码 Release 回归 r13 | 235/235，通过，0 跳过，111.33 秒；含缩略图、首帧取消、手套、退出解绑及慢的五项桌宠方法 | `artifacts/furniture-room/results-r13/ordinary-room-final-release-r13.trx` |
| 正式产品候选构建 | product-r2 Release，0 错误／0 警告 | `artifacts/furniture-room/product-r2/` |
| 最终正式产品候选构建 | product-r3 Release，0 错误／0 警告，源码为本地实现提交 `776ed4b` | `artifacts/furniture-room/product-r3/` |
| 最终产品软件驱动实时验证 | 50 次房间往返、50 次编辑开关、51 次逐次核对保存、两次独立 OS 进程恢复；活动观察 246.2541/600 秒后按用户要求结束，整体观察未通过 | `artifacts/furniture-room/product-r3/verification/progress.json`、`product-r3/observation-deferred.json` |
| 用户重新授权后的完整观察 | 复用同一 product-r3 Release，50／50 往返、51 次保存、两次独立进程恢复；有效观察 600.054 秒、排除 5.015 秒；故障 0、过载 1、长暂停 0；正常释放三项均通过，进程退出码 0 | `artifacts/furniture-room/product-r3-resumed-r3/verification/verification-summary.json`、`owned-process-result.json`、`final-identity-and-process-check.json` |

各轮不相加冒充同一全量结果。r3 的 9 项缺 native 环境失败、r5 的 2 个真实交接失败及修复前 I4 product-r1 退出失败均保留。最初物理 r1 的 7 个失败来自缺失／错误对照模块，补齐独立旧库后同门禁 r2 通过，未改测试约束。

加入家具缩略图后，r9 按 r6 实际方法集合加全部普通房家具方法复核。因数倍超出 r6 的约 110 秒耗时且没有完成输出，核实路径后终止所属 testhost，结果为 63 项已过、0 失败但整个运行中止，不能用控制台局部“已通过”当作套件成功。TRX 定位到原有 `DesktopPetAccessoryIntegrationTests` 的非 stableViewport 窗口方法，单项 178／42／56 秒（r6 相应约 1.6／0.6／0.8 秒）；这些方法单独 r10 完整通过，耗时仍有波动。随后 r11 手套方法没有完成目标入房、取消首帧方法超时，r12 单独手套也超时，全部保留。

只读核对最终发现 r9–r12 命令漏设 `-c Release`，使用了 Debug 托管程序集；r6、先前回归与正式产品候选均为 Release，两边 native SHA 一致。Debug 纯 runtime 方法约慢 3.8 倍；透明窗动态定位／region 更新及 Background Dispatcher Pump 饥饿是延迟放大的线索，尚未做 Debug 消息级根因确认，不声称 Debug 已修好。未为让测试通过修改产品代码、降低等待或省略断言。按同一最终源码改用 Release r13 复核原 r6 方法与新增家具／退出相关门禁，235/235 完整通过；五项桌宠方法恢复约 0.3–1 秒，手套与取消首帧约 0.9–1 秒。正常 Release 结果单独记录，不覆盖 Debug 失败。r10–r13 诊断采用逐测试 90 秒无转储超时，没有采集进程内存或凭据。

扩展回归 `regression-results-r1` 为 202 通过、4 失败、16 未执行。4 个失败是旧普通房壁纸 Shell／前台要求未满足，16 个是真实窗口输入／人工条件未执行；不算通过，不扩大本轮修壁纸。r6 明确选择本轮受影响门禁并排除这些环境专项，既有专项门禁保持开放。

新反例覆盖首次平台应用到首帧确认间取消返回／隐藏、编辑准入阻断、迟到故障整帧不变、运行未知停止、保存中实际最小化／恢复、已保存未应用恢复不重写、第三种未知字节保护、坏文件／非空布局缺 ABI 拒绝目标首帧、真实 lease 拒绝入口、手套取消和迟到输入，以及新旧合同隔离。真实产品组合测试读取两件实际已佩戴图片，检查眼部／配饰输出、单一实体与重入保存；另测原生身体进入预览后最新边界拒绝及手套速度约束。

## 候选身份与真实运行

固定 native 候选 SHA256 `EFEBFC2E8F93953F8DA91244BF70363773A01E58C07C490A87B56C25BEC4815A`。

独立对照：缺实时家具 ABI 的静态模块 `872B585F1048557766B9F4C4528FE71C0824DAC5C9FEA2AFE047E87F3CE86D36`；缺静态平台模块 `B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`；已验收三体模块 `B2C4D4AD7DC983B9C2C3B0179344ABCF4226EB4A0A28F0E5F6DF53BA16A91094`；旧门廊前模块 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。

保护的 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` 保持 SHA256 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。旧脚本固定 hash 没有改写。候选 `source-snapshot.json` 记录构建前实际源码相对路径与逐文件 SHA256，manifest 明确 base HEAD 和 dirty 状态，构建后复核未漂移。

I4 product-r2 完成实际产品 host 的 50 次编辑和房间往返、51 次保存、隐藏／恢复及两个独立 OS 进程保存／加载。活动观察仅累计 249.8867 秒；窗口随后被隐藏，未达到 600 秒。用户在观察期间补充家具缩略图要求，本次只结束路径核实后的所属 verifier PID，保留修改前部分证据 `product-r2/aborted-observation.json` 和 `verification/progress.json`，不视为通过，也不将进程终止记成正常释放断言通过。

最终候选纳入仓库缩略图及强化后的工具断言。工具对初次保存与每次移动均检查文件和 native 一致、layoutVersion 恰好增加 1、锚点等于本次请求，断言通过后才增加 SavedOperations；独立进程保存与加载也核对 version 1 和锚点 1440/120。手动启动前只读检查准确的 review mutex，避免误激活旧候选；新 PID 须通过初始存活核对。

最终 product-r3 已纳入上述缩略图与断言，构建于干净实现提交 `776ed4b6548538db645a65d7c1a99ea2684f90b8`。源码快照为 761 项、SHA256 `58A53A3BEF8760A20ECC8F086AC01DBCCB1B3BB6BF91B208D844B843D40AB86B`，构建后核对一致。本记录后续只更新验证结果，未改产品或工具源；后续文档提交不是另一个生产候选。

product-r3 已完成上述 50／50 循环、51 次保存及两次独立进程恢复。提交边界 p95 5.6442 ms、最大 6.5358 ms；部分活动观察累计 246.2541 秒，健康 Synced、0 scheduler faults。窗口再次隐藏后停止累积；用户回复“别执着于观察了，我还要用电脑”，据此核实路径并结束所属 verifier PID 35124，没有降低 600 秒门槛或改报通过。保留 progress 与 observation-deferred 证据。进程被准确终止，不能把未执行的最终 Dispose 释放断言写成该观察通过；r13 的产品退出测试仍是单独通过证据。

600 秒活动时间要求窗口可见、未最小化、活动焦点、健康且唯一 driver 已准入，并有真实物理步增加；采样期间生命周期变化整段排除。工具有内部观察界限和外部进程超时，仅清理本次所属进程。正常完成结果写入 `verification/verification-summary.json`；最初按用户要求提前结束，没有该完成结果，不视为通过，当时没有自动继续观察或安排唤醒。

用户随后明确回复“你可以观察了”，重新授权本轮观察。直接复用冻结的 product-r3 Release 可执行文件与模块，不重新构建或重复运行已完成的测试。首次恢复的临时数据 purpose 写成 `ordinary-furniture-room-resumed-observation`，被 CLI 的精确 `ordinary-furniture-room-acceptance` 校验拒绝；PID 34544 已退出，尚未创建观察输出，保留 `product-r3-resumed/startup-failure.json`，未修改产品源。新的 `product-r3-resumed-r2` 自动生成正确 owner，完成往返与恢复后失焦，有效 119.356 秒、排除 180.735 秒。排除时间已经超过冻结观察墙钟上限 720 秒的 120 秒余量，不可能再累计满 600 秒；经路径核实结束所属 PID 35508，保留 `aborted-observation.json`，不将部分时间相加，也不声称最终 Dispose 已运行。用户回复“已切回普通房窗口”后，使用另一个全新数据及证据目录重开。

完整成功结果为 `product-r3-resumed-r3/verification/verification-summary.json`：PID 20264，有效观察 600.053985 秒，排除 5.014845 秒，含前置流程的墙钟 657.524201 秒；同一世界和实体，布局版本 51，51 次保存均逐次核对文件／native／锚点／版本。观察内真实物理步增加 36293，Scheduler Faults 0、Overloads 1、LongPauses 0。提交边界 p95 4.111 ms、最大 4.2864 ms。WPF distinct Rendering 回调 35961，回调率约 59.93/s，仍不能视为 compositor Present 或既有 59 FPS 门禁通过。

持续运行第 2 分钟后至结束的资源采样，私有内存 188.19–190.19 MiB、句柄 682–693、线程 36–41；本次采样未见持续上升，不能据此推定更长运行没有泄漏。两个独立恢复子进程 PID 23900／24300 均读取相同版本 1、operationId、锚点 1440/120 和文件 SHA，真实 native 平台载入及三项正常释放均通过。主进程 `ReleasedFrameOwner`、`ReleasedNativeWorld`、`ClosedOwnedWindows` 全为 true，正常退出码 0，900 秒外部 watchdog 未触发。最终复核时 verifier／子进程没有残留；数据、锁文件和失败证据均保留。

运行前后六个冻结 review／托管／native 文件 hash 全部一致，保护 DLL hash 未变；实际人工候选的 `Dango.Desktop.dll` 与已验证副本一致，native 仍为冻结候选。`final-identity-and-process-check.json` 记录核对结果，源码相对构建提交只有本文及接入计划两个文档变化。本轮没有改变门禁或产品行为。

导出实际 `LocalRoomWindow.Content` 最小／横向／默认窗口的 96 与 144 DPI 离屏图，用于布局核对。144 DPI 图片不是 OS 150% 的真实布局／输入验收。WPF Rendering 回调率单列，不能冒充 compositor Present、应用 59 FPS 或人工手感。

最小 640×420 图从大窗保持手动世界中心缩小，地面仅余底部 4 行，团子下沿局部裁切；不能据此声称最小尺寸完整场景通过。已定位为既有 `RoomViewController.Resize` 保留中心、Manual 不执行 Follow 身体下沿保护的规则，本轮未改既有相机或团子物理。固定工具与反馈可见；显示预览确认按钮和滚到底后整个仓库缩略图可达由真实 WPF 控件测试覆盖。人工仍需分别检查直接以最小窗入房和编辑中缩窗／右键调整视角。

## 待人工验收与交付边界

待用户在实际 `Dango.Desktop --local-room-review` 中操作：主页 W 进门廊、面向本地普通房 E、房内 Esc 菜单、预览拖动与微调、穿入拒绝／相切允许、确认保存、接触删除、本人手套及释放、右键镜头与退出恢复、按钮焦点不跳跃、失焦／捕获清理、隐藏／恢复、回门廊／重入及重启恢复。100%／150% OS DPI、高对比、减少动画及既有 59 FPS 门禁未完成时继续标记未验证。

验证数据、布局与 `.lock` 是任务独占的虚构本地证据，保留不删除。真实产品人工窗口的数据与自动验证数据独立。测试没有数据库、对象存储、网络端口或服务。最初依用户使用电脑的要求没有再弹出人工窗口；重新授权的完整观察完成后，先确认准确的 `Local\Dango.Phase1.App.LocalRoomReview.Mutex` 没有其他 review 占用，再启动同一 product-r3 实际 `Dango.Desktop --local-room-review`。新 PID 1984 已通过路径、初始存活和可见窗口句柄核对，保持打开供人工验收；记录见 `product-r3/manual-window.json`。这是本次有意保留的唯一产品人工进程，未借用自动观察布局。

有空时可从仓库根运行最终候选（普通房 review 与日常安装和其他 review 模式有独立 mutex）：

```powershell
.\artifacts\furniture-room\product-r3\bin\Dango.Desktop\release\Dango.Desktop.exe --local-room-review
```

进入路径为首页 W 到门廊，面向本地普通房门 E 进入布置；正常入房后 Esc 菜单也能进入。人工数据根为同一候选 bin 目录下 `local-room-review-state`，不借用自动验证存档或日常数据。600 秒持续观察已由单次完整结果关闭；真实输入、OS DPI／高对比／减少动画、既有 59 FPS 及人工确认继续开放。

本轮仅独立本地候选，本地实现提交为 `776ed4b`，人工尚未验收。未合并 main、推送、部署、更新日常安装、替换生产 DLL 或删除其他工作树／数据。
