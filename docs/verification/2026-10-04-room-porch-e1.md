# E1 二维门廊候选验证

状态：E1 初版已获用户整体认可；2026-10-05 门距修订候选待复验，未将整个阶段标为已验收。受影响自动门禁与隔离 Windows 启动完成；全量历史／夹具差异与未验证项保留。设计与范围见[实施计划](../planning/2026-10-04-room-entry-implementation-plan.md)。

## 范围与边界

- 独立 `codex/room-porch-e1` 从文档基线 `d42e860` 开始。保持首页编辑、设置、三尺寸及同屏 DPI 行为。
- 门廊使用同一个物理运行时、实体 ID 与帧驱动；二维横向世界、落地横排门、指数跟随相机。首页 A／D 编辑与 S／空格保持，门廊 A／D 移动、W 本地选门、Esc 回首页；没有恢复方向键。
- `--porch-scene-review` 使用独立单实例名与输出目录内的 `porch-scene-review-state`。仅此评审模式有四扇虚构门，页面标注“场景预览 · 未接房间业务”。产品默认目录为空；预览选门不进行准入、不修改权威归属、不打开房间、不发送网络动作。
- `RoomNavigationCoordinator` 的准入／ready／失败接缝仅由虚构自动夹具验证，产品 E1 不调用它们。E2／E3 尚未实施。

## 必败证据

- `artifacts/room-porch-e1/red/results/red-confirmed.trx`：旧实现两项失败，实际仍是右侧栏，且缺失世界场景。
- `artifacts/room-porch-e1/native-red/results/native-red.trx`：真实 native 长按移动越界，5 项中 1 失败；保留原证据，不改成视觉坐标 clamp 掩盖。
- 相机忽略 CameraX、host-ready 绕过权威确认的两项错误实现均使对应模型测试失败，恢复后模型 23/23 通过；源证据保留在模型隔离树的 `artifacts/test-runs`。
- 初轮针对首页／设置／相机／导航／尺寸／帧调度回归：84 通过、2 项依赖前台鼠标操作未验证。这不是最终全量结论。
- `restore-red/results/restore-red.trx`：每次墙边重进补可见性、真实步数和 frame 映射断言后，第一次往返即失败，证明仅比较缓存位置不足以验收。
- `fault-input-red/results/fault-input-red.trx`：三个真实反例失败——首页／门廊故障锁存后 W／Esc 未捕获异常，以及按住空格后焦点移到按钮再释放时 Jump 残留。`recovery-red/results/recovery-red.trx` 两项再证明恢复运行时后 UI 故障仍阻止步进。均先复现再修复，不广泛吞异常；运行时故障与可选导出缺失分开处理。
- `restore-debug-new/results/restore-debug-new.trx`：接触 slop 修复后仍失败。右墙位置为 `2078.919677734375`；离墙、首页运行 90 帧后身体真实舒展，恢复该中心使 node-circle 右边缘到 `2141.826359`，超过右墙 `2136`。模型树独立 `home-pause-red` 也先复现原生 `-410`。这里使用逻辑像素（DIP），不是 Windows 缩放后的物理像素。

## 局部物理边界决策

既有原生地板范围有限，单次 viewport fitting 不约束持续移动。相机夹边或只夹屏幕坐标会隐藏物理越界，不满足真实团子门廊。故 E1 增加显式、可选的本地物理地板／侧墙能力；未启用时默认行为保持。宿主切换通过同实体横向浮动原点交接，保留轮廓、速度、ID，避免 native CoreX 被累计两次。真实地板 Y 单独投影，不用身体包围盒底边猜脚线。

旧统一模块仍可初始化和运行首页；调用新能力必须因缺导出失败并显示不可用，不能装作已有能力。没有更换物理引擎、升级 Box2D、改调参或回填服务器坐标。

墙边合法接触采用固定 Box2D 3.1.1 的 `.005 * b2GetLengthUnitsPerMeter()` 包含预检容差；只调整预检，不修改求解器或墙位置。离墙后形状舒展是另一问题，不能继续放宽容差：本地页面交接按发布帧中所有实体的真实 node-circle footprint 查询可放中心，仅在必要时做最小向内放置（该反例约 `5.83 DIP`）。查询不推进物理、不改形状、速度或身份；持续移动仍靠物理侧墙，`SetHorizontalBounds` 对困住实体的收缩仍必须失败。

仅构建隔离 `dango_physics_module` target。Box2D 使用既定 commit `8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3`，源码 ZIP SHA256 `FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721`，许可证 SHA256 `68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2` 已实查。

本轮不运行会覆盖旧 DLL 的 `build-box2d.ps1`。旧 `dango_box2d.dll`、依赖清单与现有正式发布模块 hash 门禁保持；新候选不符合旧发布 hash 是预期隔离边界，不放宽发布脚本或更新日常安装。

## 最终验证与交付

- 静态：`git diff --check` 通过，5 份修改文档的 194 个相对链接存在；两次独立只读复核及后续故障／恢复／placement 接缝复核无残留阻塞。不新增依赖、认证、服务端或数据库代码。
- 构建：隔离 Release WPF 及其测试构建成功。候选位于 `artifacts/room-porch-e1/acceptance/build/bin/Dango.Desktop/release/`，没有发布或更新安装。
- 最终 Desktop 受影响回归：`acceptance/results/desktop-focused.trx` 共 128 项，**126 通过、0 失败、2 跳过／未验证**；覆盖首页／编辑／设置、三档尺寸、相机／导航、真实 native 世界边界、首页往返、隐藏后恢复、单一帧驱动、可选能力缺失、故障锁存及显式恢复。每次边缘重进必须实际可见、真实步数推进且坐标映射一致，交接后首帧绘制前的缓存位置也必须与真实放置一致。
- 2 项前台鼠标用例为 `ClosingSizeMenuReturnsKeyboardFocusToTheDangoStage(False)`、`RealMouseSizeSelectionRestoresTheHomeStageFocus`；当前会话不具备所需前台鼠标条件，不能计为通过。
- 原生 Physics 专项：`final-physics/results/porch-physics.trx` 7/7、`world-physics.trx` 6/6，共 **13/13 通过**。模型树最终相关 Desktop 47/47 也通过；默认未启用边界的 900 帧与只读旧统一模块 JSON 严格一致。真实 native 资源分配失败（OOM）注入未验证。
- 三档布局及跟随／左墙截图来自实际 WPF native 帧，保存在 `acceptance/evidence/porch-scene-e1/`，已检查图像。它们是自动渲染证据，不是跨屏 DPI、壁纸房间或用户人工观感验收。

### 全量回归差异（保留原结果，不宣称全量通过）

- `final/results/desktop-full.trx`：最终 placement 修复前的全量快照，**678 通过、6 失败、2 跳过／共 686**。其中 1 项为本轮边缘往返问题，已由最终 128 项复跑闭合；1 项为受保护旧 `dango_box2d.dll` 缺 `dango_box2d_self_bodies_create` 的既有失败；另 4 项为旧步态工具坚持批准模块 hash `00FF...`、拒绝新候选 `BFDD...`。未放宽其门禁；仅在本轮测试输出夹具中使用只读旧模块复跑，`accepted-gait-gate-correct/results/accepted-gait-gate-correct.trx` **4/4 通过**，随即恢复候选夹具。初次误换非该测试实际读取的夹具造成的 4 项失败也保留在 `accepted-gait-gate/results/`。没有改写全量 TRX 或将分次结果相加声称全量通过。
- `physics/results/physics-full.trx`：候选 `D124...` 时的长时全量快照，**39 通过、13 失败／共 52**，用时约 16 分 37 秒，已正常结束。13 项都缺少本树的历史比较产物（旧 collision-review 4 项、原 extracted-module 3 项、three-preview 6 项）；不能用新 DLL 冒充旧基线。最终模块的专项与默认轨迹门禁另见上文，不替代这个全量结果。
- Core：`core/results/core.trx` **37/37 通过**，本轮未修改 Core。

### 隔离候选与 Git 状态

- 已真实启动 `Dango.Desktop.exe --porch-scene-review`。核对 PID `31792`、标题 `Dango · 门廊 E1 场景预览`、非零窗口句柄及响应状态；窗口保持供用户验收。此检查确认启动，不冒充画面／操作的人工验收。日常已安装进程未停止、未更新；所有本轮 testhost 已结束，无残留测试 runner。
- 首页 W 进入门廊；门廊 A／D 左右、S 轻压、空格跳跃、W 本地选门、Esc 首页。四扇门明确为虚构场景预览；不调用业务准入或宿主房间显示。
- 新模块 SHA256：`BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`。旧 `dango_box2d.dll` 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`，依赖清单仍为 `F878EE6173D5037446F75FFED09F286AEEAFB1B50BEAE794D1615A59CC27E698`，许可证仍为 `68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2`，只读已验收统一模块仍为 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。
- 独立 `codex/room-porch-e1` 保存本地文档、模型、边界／rehost、容差、placement 与 UI 提交。`main` 仍为 `4c893764831a4f1a188dde91f1c48af69ac17600`；未合并 main、未推送、未安装或部署。
- 正式目录／认证／权限／准入、服务器／数据库、房间显示与返回 E2／E3、跨屏 DPI、真实多客户端、M7、完整发布门禁和用户人工验收均**未验证／未交付**。E1 仍为候选待验收，不标整个阶段通过。

未合并 main、未推送、未安装或部署。没有新增房间服务、持续业务监听或数据库写入；虚构门数据仅在内存评审夹具中。

## 2026-10-05 门距修订

- 用户认可初版，要求缩小门间空隙。仅调整隔离评审四门的世界 X 为 `560 / 780 / 1000 / 1220`；中心距由 `380` 改为 `220 DIP`，净间隙由 `256` 改为 `96 DIP`（减少 62.5%）。门宽 `124`、高 `194 DIP`、落地点、风格、世界宽 `2160 DIP`、物理、相机与操作保持不变。
- 门距收紧后相邻交互范围会重叠，W 提示与实际 W 目标共用唯一最近门判断；交互距离仍为 `132 DIP`，等距时沿用前门优先。产品默认目录仍为空，不接业务准入、不修改房间归属。
- 必败验证：`artifacts/room-porch-e1/door-spacing-red/results/door-spacing-red.trx` 新增 6 项中 **4 失败、2 通过**，捕获旧门距及中点右侧提示与实际最近门不一致。
- 修订回归：`artifacts/room-porch-e1/door-spacing/results/door-spacing.trx` 共 134 项，**132 通过、0 失败、2 跳过／未验证**；新增三档几何／落地及最近门用例均通过。长走至世界远端的 `>1750` 门禁保留，没有因末门提前而放宽物理边界测试。上述两项前台鼠标用例仍未验证。本轮没有重新运行全量 Desktop、Core 或 Physics 专项，不将历史结果算作新全量通过。
- 三档实际 WPF native 渲染截图已检查，门距更紧且标签未重叠：`artifacts/room-porch-e1/door-spacing/evidence/porch-scene-e1/review-960.png`、`review-1280.png`、`review-portrait.png`。这些是自动渲染证据；新门距版的用户观感与操作复验待确认。
- 精确核对后停止旧评审进程 PID `31792`，未删除产物；从 `artifacts/room-porch-e1/door-spacing/build/bin/Dango.Desktop/release/` 启动新隔离预览 PID `35736`，已核对标题 `Dango · 门廊 E1 场景预览`、非零窗口句柄及响应状态。窗口保持供复验，首页按 W 进入门廊。日常已安装进程 PID `29268` 保留且响应，未更新安装。候选 native SHA256 仍为 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`；本轮未修改或构建 native。
- 仅门廊视图、对应集成测试与本记录进入本地修订提交；未合并 main、未推送、未安装或部署。无新增服务／数据库写入，E2／E3 与正式房间业务未推进。
