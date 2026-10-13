# 实时家具布局：首批本地实施验证

日期：2026-10-10。状态：**首批 Windows 单人木平台布局候选获用户人工验收通过。定向单元、native 与最终 r7 文件／物理整链、真实进程重启门禁及 r8 的 600 秒／50 次配对观察已通过；专项键鼠／100% 与 150% DPI 矩阵尚未逐项记录，既有 59 FPS 门禁保持开放。**

依据[实时编辑设计](../planning/2026-10-10-live-furniture-editing-design.md)、[实施计划](../planning/2026-10-10-live-furniture-editing-implementation-plan.md)与[C0 合同](../planning/2026-10-10-live-furniture-c0-contract.md)。用户在实施准备交付后明确回复“开始”，授权本批本地实现、隔离构建、测试和独立单人验收窗口。实现来源为设计提交 `cba27b9`，分支为 `codex/furniture-layout-live`。本记录不继承 HTML 原型、旧暂停式布局或其他物理模型的通过结果。

## 已实现范围

- 独立 Windows 单人窗口，空布局或一件固定官方木平台。编辑继续使用同一 world、同一本人团子，预览不改真实家具；添加／移动确认后提交，选中家具直接删除。
- 三段侧栏、两列纯图标仓库、家具选择／拖动与本人手套、右键相机平移。平台大小与圆球入口禁用并标记后续接入；没有参数调节或圆球布局效果。
- 可选 Furniture ABI 1 的 Probe／Prepare／ApplyPrepared／CancelPrepared／共同只读帧。平台成功变更不 Clear→Set、Reset 或重建世界，正常拒绝完整保留旧现场；token 随 Step、场景变化、取消或释放失效。
- schema 1 只保存官方预设、初始顶面锚点、场景版本、`layoutVersion` 和 `lastOperationId`。严格拒绝多件、未知／重复字段、非有限坐标、非法边界／出生区与超出 64 KiB 的文件；不保存运行身体、相机或手套状态。
- 每次家具请求持家具专用 `FileShare.None` lease，取得后重读正式文件。后台唯一 temp 写入、Flush(true)、重读校验不接触物理 world；唯一 owner 在步边界完成最新 Prepare、正式 Move／Replace、目标确认、Apply 与真实帧对齐。
- 明确区分未提交、已保存未应用、保存结果未知和运行结果未知。原始字节确认与真实共同帧决定恢复，不能凭布局语义相等或 UI 缓存解禁；运行未知停止 Step，已保存未应用恢复不重复写文件或增版本。

本批沿既有平台兼容物理基线，保持身体／材质与显示 adapter 参数。新圆形模型仍需独立准入；本批没有复制其他工作树未提交文件。球、多件／组合、跨玩家手套、3／5／10 秒在线权威规则、账号云保存、正式门廊 E 与房内 Esc 入口不属于本次实现。

## 文件范围

| 层 | 实现文件 |
|---|---|
| 数据与校验 | `src/Dango.Core/Furniture/FurnitureLayout.cs`、`FurnitureLayoutSerializer.cs`、`FurnitureEditOperation.cs` |
| 文件存储 | `src/Dango.Desktop/Furniture/FurnitureLayoutStore.cs` |
| native 与 managed 能力 | `eng/native/box2d/dango_collision_review.c`、`dango_static_platform.h`，`src/Dango.Physics/DangoPhysicsWorld.Furniture.cs`、`DangoFurnitureTypes.cs` |
| 单人场景与窗口 | `eng/tools/Dango.JointReview/FurnitureLayoutSceneSession.cs`、`FurnitureLayoutWindow.cs`、`FurnitureLayoutProjection.cs`、`FurnitureLayoutInputPolicy.cs` 与 `Program.cs` 入口 |
| 验证与隔离启动 | `FurnitureLayoutVerification.cs`、`FurnitureLayoutWindowVerification.cs`，`eng/run-live-furniture-review.ps1` 与 Core／Desktop／Physics 家具测试 |

## 当前可核验结果

| 门禁 | 实际结果／证据 | 边界 |
|---|---|---|
| Core 布局与操作定向测试 | **66／66 通过，0 失败、0 跳过**；`tests/Dango.Core.Tests/TestResults/core-furniture-all.trx` | 数据／几何／请求规则，不代表 native 或窗口行为 |
| Desktop Store 定向测试 | **18／18 通过，0 失败、0 跳过**；`tests/Dango.Desktop.Tests/TestResults/desktop-furniture.trx` | 真实隔离文件与跨进程锁；不代表整链物理应用 |
| native 与 Physics 门禁 | **58／58 通过**：新增实时家具 17，既有静态平台 41；native 静态门禁 0 失败，既有球回归 1,037 cases／244,104 checks、0 失败；`artifacts/furniture/live-native-c1/evidence.json` | 保留既有物理回归；球回归不代表球布局已接入 |
| 错误候选负例 | 故意破坏 stale-token 约束的候选被门禁检出；`artifacts/furniture/live-native-c1/results/negative-stale-token.trx` | 该有意失败证明门禁能识别错误实现，不计作正式候选失败或通过项重复累计 |
| Release／静态门禁 | **最终 r7 Release 成功，0 warning、0 error**；本轮三文档相对链接／围栏／无本机绝对路径检查及差异空白检查通过 | r5／r6 的中间通过分别保留，不继承为 r7；r7 已重新构建并运行整链 |
| 文件＋native＋SceneSession 整链 | **最终 r7 13／13 cases 通过**，9.6753909 秒；`artifacts/furniture/live-r7/verification/verification-summary.json` | 软件驱动真实 native 和隔离文件，包含 50 次 scene/world 生命周期、150 次保存并实际应用、故障恢复及真实进程重启 |
| WPF 投影与输入所有权纯门禁 | **35 checks 通过**，由 r7 整链启动时执行 | 验证投影、按钮／场景焦点、外窗口隔离及清理规则；不创建 native world，不代替硬件输入或真实手感 |
| 600 秒真实 WPF 观察 | **r8 通过**：600.0371269 秒墙钟、600.0348299 秒活动、50 次轮转；`artifacts/furniture/live-r7/window-evidence-r8/window-observation-summary.json` | 同一最终 r7 executable；r5／r6／r7 提前结束的失败记录仍保留；应用自报 ExitCode 0，不宣称外部已捕获 OS 退出码 |
| 50 次循环 | **r7 整链 50 次 scene/world 关闭／新建、150 次提交／保存／应用通过；r8 可见窗口 50 次进入／退出编辑配对轮转通过** | 两组循环分别报告，进入／退出编辑不等于提交或关闭；scene/world 重建不等于操作系统进程重启 |
| 真实进程关闭后重启读取已保存布局 | **r7 两个真实子进程门禁通过**；`process-first.json` 与 `process-restarted.json` | 首个进程保存并退出；第二个新进程从同一文件载入并在真实 native 应用，分别归零 world owner |
| 用户整体人工验收 | **通过**；用户于 2026-10-10 对已交付真实窗口明确回复“通过” | 接受本批单人木平台布局、界面与操作体验；不扩大到球、参数、正式入口或多人 |
| 专项键鼠、100%／150% DPI 矩阵 | **未逐项验证** | 整体人工通过不补造专项操作、DPI 切换或测量证据 |

Core 负例覆盖非法目标／内容、同 ID 改内容、陈旧版本、重启旧请求、无效基线、空 GUID、非有限坐标与版本溢出。schema 测试覆盖有限值、0／1 件、未知／重复字段、版本、出生保护及完整边界，不截断／钳制。

Store 使用独占虚构测试目录，实际覆盖首次 Move、已有文件 Replace、staging 失败与坏 temp、取消、同版本外部字节变化、提交前后抛错、旧目标消失／第三种内容／锁不可读的结果分类、另一个 PowerShell 进程不能打开已持 lease、锁文件可重新使用，以及其它操作残留 temp 不被载入或删除。目标等于候选才确认提交，旧目标原来存在但后来消失必须是未知。测试子进程已退出，合成正式文件和锁文件保留在 ignored `artifacts/test-runs/live-furniture-store`；清理只限本次创建的 temp。

本组单元构建输出使用独占 `artifacts/live-furniture-storage-build`。native 来源与最终 r7 候选见下一节；所有观察／验证子进程已退出，合成正式文件、锁文件与各轮 artifacts 保留，不删除数据。当前另行打开的正常人工验收窗口 PID 为 14600，不把它算作观察残留。

## native 候选与最终 r7 整链实测

native 候选为 `artifacts/furniture/live-native-c1/build/Release/dango_physics_module.dll`，SHA256 为 `EFEBFC2E8F93953F8DA91244BF70363773A01E58C07C490A87B56C25BEC4815A`。受保护生产 DLL SHA256 为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，沿隔离脚本前后核验保持；对照基线 hash 为 `B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E`。来源仍是设计提交 `cba27b96e50e779c34cb8b68956d514aab5bc452`，没有整合新圆形模型。

`live-native-c1/evidence.json` 记录了 native／managed／测试源码 hash、Box2D 归档 hash 与 67 个来源文件核验。58 个 managed 正式候选测试结果位于 `artifacts/furniture/live-native-c1/results/live-furniture-final.trx`。native 球回归仅用于确认原有单球行为没有退化，不开放本批多件或球布局。

r7 隔离 Release 候选与 manifest 位于 `artifacts/furniture/live-r7`，复制 DLL 与 native 源候选 hash 相同。通过入口为 `Dango.JointReview --furniture-layout-verify <候选模块> <新独占证据目录>`；三个层次分别是纯窗口规则、真实 world／文件整链及另行启动的可见观察窗口，不能混为同一套硬件验收。最终整链返回 `OwnedWorldCount=0`。

13 个整链 case 实际覆盖：

- 50 次新建／关闭真实 scene/world，每次 Insert、Move、Delete 共 150 次保存并实际应用，文件版本从 0 递增到 150；文件重读与已应用布局一致，关闭后 world owner 为 0。同一团子／world 在每组编辑操作内保持，不把循环中的新建当作家具应用手段。
- 临时文件 staging 期间物理继续；原先允许的预览位置被真实团子进入后，最新 Prepare 拒绝，正式文件与真实家具保持旧值。
- 接触平台时删除，实际身体位置／速度保持，后续自然脱离支撑；这是独立于上述 150 次循环的额外支持删除夹具，不把额外操作混入其耗时统计。
- 正式替换前异常保留文件／世界／预览；正式替换后抛错仍依据目标确认应用。已保存未应用的恢复实际 Apply 一次，不再次写文件或增版本；native Apply 后抛错通过真实 receipt 识别成功。
- 未知目标字节继续保护，确认候选后的恢复不重复写入；未列入常见 I/O 类型的 writer 异常也不能误报未保存。运行结果与 receipt 不匹配时停止 Step，保留已保存候选；坏文件只读空场景保持原字节，第二 live scene 不能覆盖新版本。
- 真实操作系统子进程 37088 从空布局保存版本 1、锚点 `(0,1.375)` 后退出；新进程 10116 载入版本 1 与相同锚点，实际 native 平台存在，再移动并保存版本 2、锚点 `(.5,1.5)`。两进程各自在释放后记录 `OwnedWorldCount=0`；证据位于 `artifacts/furniture/live-r7/verification/process-first.json` 与 `process-restarted.json`，不再用同进程 world 重建推定这一门禁。

上述 150 次循环的最终提交步边界耗时 P95 **5.4528 ms**，最大 **9.7547 ms**；整个 r7 整链共 **9.6753909 秒**。该统计包含边界 Prepare、正式提交和 Apply／观测，临时写入与 flush 在边界外；不包含额外 case／子进程操作，不能把它称为整个窗口 FPS 或无 I/O 停顿。r8 窗口观察的步成本另行记录，不混入这一提交统计。

中间 r5／r6 均曾完成 12／12 整链 case，分别耗时 8.8553614／8.9376519 秒；其日志与 manifest 保留。r7 在 UI 修订后重新执行原有整链并增加真实进程重启 case，最终结果由 r7 自身证据裁定，不累计重复轮次提高通过数量。

## 验证中发现与修正

定向审查发现提交结果与运行结果不能共用简单“失败即未保存”：writer 抛错后需要精确目标字节分类；初次 native 读取失败不能把空观察当作旧现场；提交结果未知的恢复不能把不可读与原文件不存在都判为空。现有实现已按这些边界分类，最终 r7 真实整链故障夹具已通过。

恢复确认旧字节后须同时对齐 `PersistedLayout` 与已应用 `Layout`；Step／真实读取抛错、已应用运行映射不一致冻结为 `RuntimeUnknown`。完整 last-success 收据仅在当前 session 去重，重启旧请求安全拒绝。Core 请求先校验基线，防止通过删除非法实例生成看似有效的新布局。相关数据负例及最终 r7 状态机整链均已通过。

早期失败保留：r1 的 Program 入口补丁位置导致构建失败；r2 latest-shape 夹具尚在 staging 时 Dispose 的异常掩盖原失败，已修正夹具收尾；r3／r4 的手套目标高度不足，实际身体未穿入目标，所以夹具明确失败。r5 改为遵守真实 8 p/s 目标限制并把身体实际拖入预览区域，再验证 Prepare 拒绝；未降低碰撞或身体门槛。r2–r4 的 `verification-failure.json` 与各自目录保留，没有覆盖为通过证据。

## 可见窗口观察与资源收尾

r5 观察中途出现 `HasPreview=true` 与 `Minimized=true`，物理步／活动时长停止增长；最终 164.8069065 秒墙钟、57.2441689 秒活动、2 次编辑轮转，提前结束，`ExitCode=1`。r6 在 r7 代码冻结前结束，最终 103.4807205 秒墙钟、103.4710097 秒活动、10 次轮转，`ExitCode=1`。r7 观察同样在最小化后提前结束，最终 181.3170399 秒墙钟、57.1190747 秒活动、18 次配对轮转，`ExitCode=1`。三轮都没有满足 600／560 秒和 50 次轮转，不算完整观察通过；各自 `window-evidence/window-observation-summary.json` 保留。三轮均记录 scene 已释放、world owner 与 timer owner 为 0、鼠标捕获已释放，这些只证明提前结束后的资源收尾。

最终 r7 已加入 660 秒最大墙钟失败退出保护：未达到活动时间或 50 次轮转时明确失败并关闭，不无限等待。动态碰撞拒绝提示随真实 probe 更新，重新合法后移除旧碰撞提示，但保留实际存档错误；取消新添加的预览后清除不存在家具的选中态。r7 重新构建、整链通过；初始观察 PID 16912 的结果归入上述未通过记录。

用户随后明确“恢复窗口，完成观察”。集成负责人重新打开同一个最终 r7 executable，不改源、不重建，保留同一隔离 `data`，使用新独占证据目录 `artifacts/furniture/live-r7/window-evidence-r8`；初始 PID 为 38044。r8 是观察运行名，不是新的代码／构建候选，最终软件门禁仍归 r7。最终窗口观察完成，PID 38044 已退出。

| r8 实测项目 | 结果 |
|---|---|
| 墙钟／活动时长 | 600.0371269／600.0348299 秒 |
| 物理步／新 native 状态绘制更新 | 36,002／21,798 |
| 编辑轮转／调度回调 | 50 次进入／退出配对／22,967 次回调 |
| 丢弃累计步时间 | 0 秒 |
| 步成本 P95／最大 | 0.9646／14.1088 ms |
| 峰值 working set | 239,734,784 字节 |
| 关闭收尾 | SceneDisposed=true、OwnedWorldCount=0、TimerOwners=0、MouseCaptureReleased=true |
| 应用完成摘要 | ExitCode=0、Failure=null；外部未捕获 OS 退出码 |

21,798 次绘制更新除以活动时长约为 **36.328 次／秒**，这是新 native 状态的窗口绘制更新率，不是显示合成 FPS，不关闭既有 59 FPS 性能门禁。r8 没有执行家具提交，`CommandWallMaximumMilliseconds=0` 不能当作提交零耗时；实际提交成本来自前述 r7 整链。

`artifacts/furniture/live-r7/observation-audit-r8.json` 对 591 条观察记录复核：始终一个 world、一个 entity、一个 scene revision，physics step 单调，fault rows 与 minimized rows 均为 0，进程已经退出。最终候选源码 manifest 与实际来源一致，生产 DLL hash 最终核验仍保持前述值；该审计与应用自报 ExitCode 分别记录，没有补造外部 OS 退出码。

可见观察中的 50 次轮转只做“进入／退出编辑”配对；整链另有 50 次 scene/world 关闭／新建与 150 次真实提交，两者不互相代替。规定观察时长、配对轮转与资源收尾门禁通过；用户的整体人工确认另记下文，专项键鼠与 DPI 仍保留未逐项验证状态。

## 2026-10-10 用户人工通过

交付实现提交为 `956ec56d211c166a6c9d911581e64a6582e8aee7`。打开同一最终 r7 候选的正常 r9 验收窗口后，用户明确回复“通过”，据此记录本批 Windows 单人木平台布局候选整体人工验收通过。范围为已交付的三段侧栏／图标仓库、家具预览确认与取消、直接删除、本人手套、右键视角、本地保存及编辑时同一团子留场活动；这不是逐项动作测量结果。

本次确认不补写专项键鼠或 100%／150% DPI 矩阵，不关闭既有 59 FPS 性能门禁、完整 Windows 门禁、服务端／多人／双机验收，也不代表新圆形模型、球布局、参数调节、账号保存或正式门廊／Esc 已实现。当前仅记录人工通过和创建当前分支的本地文档提交，不包含 main 合并、推送、日常安装、生产 DLL 替换或部署授权。

## 交付与未验证项

当前处于实现与验证分支，本批候选整体人工验收已通过，专项未验证项保留。没有合并 main、推送、更新日常安装、替换生产 DLL、部署、数据库写入或启动房间服务。独立候选只能证明本机 Windows 单人增量，不能关闭正式房间、多人、后端或双机门禁。

运行结果未知不宣称原世界仍健康，重新打开只按最终正式文件建立初始场景，不承诺恢复异常前游玩状态。文件与 native 不构成跨资源原子事务；最终替换可能短暂延迟一个物理步，须以实际候选提交耗时评估。坏存档可使用只读空场景游玩，普通编辑不可覆盖原件；损坏文件恢复和持久数据清理不属于本批。

规定运行观察与用户整体人工验收已通过；C4 中未逐项验证的键鼠／DPI 专项门禁继续保留，不标记完整矩阵已完成。所有最终实测结果须由本轮真实证据更新，不能由历史原型、整体人工确认或独立单元通过推定。

任务代码与最终 r7 候选来源已冻结并本地提交为 `956ec56`。正常人工验收窗口使用同一 r7 binary／data，PID 14600，独占证据目录为 `artifacts/furniture/live-r7/human-acceptance-r9`；启动记录 `human-acceptance-run-r9.json` 保留交付时的 pending 快照，随后人工通过单独记录于 `human-acceptance-approved.json`。确认时该窗口仍运行，予以保留；此前观察与验证子进程均已退出。专项键鼠／DPI 状态不因人工确认而改写。

没有合并 main、推送、更新安装或部署授权。本次保留真实存储测试数据／锁及所有成功、失败 artifacts，不执行持久数据清理。
