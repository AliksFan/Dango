# 普通房人工评审修订

> 2026-10-08 后续决策：用户批准完整移除普通房壁纸功能，普通房只使用窗口，见[移除计划](../planning/2026-10-08-ordinary-room-window-only-plan.md)与[本轮验证](2026-10-08-ordinary-room-window-only.md)。下方真实壁纸抚摸失败及未定位原因保留，不将功能撤除记作修复通过。

日期：2026-10-05。依据用户对上一版候选的反馈及其补充的壁纸键盘、拖动、抚摸范围，在 `codex/local-room-implementation` 继续修订。人工验收尚未通过。

## 2026-10-06 范围收敛：壁纸暂停，窗口优先

用户反馈第六轮「无反馈，但卡顿明显变少」，随后明确要求先停止壁纸层、完善窗口模式，再重新梳理壁纸方案。已结束实体测试；不再启动壁纸诊断，不丢弃代码或失败证据，也不把该结果记为抚摸通过。后续窗口验收沿用现有需求，不增加家具、多人或新的呈现规则，见 [窗口收尾记录](2026-10-06-local-room-window-closeout.md)。

`wallpaper-physical-petting-20261006-r6.trx` 最终失败：非 injected Shift Down、干净起始帧、身体命中、捕获以及 Shift Move 到达动作均已观察到，但没有新反馈、同手势后续 Soft 眼帧和正常完成；pending 被取消。命令资格的图标否决和 scene blocked 是跨尝试汇总，尚不能认定某一取消的唯一原因。最大鼠标 callback 为 15.411ms、快速守卫为 12.923ms，只表示本轮最大观测值。用户确认卡顿明显变少；抚摸仍未修复。gameplay 与 wheel hooks 均已释放，Ready=false。真实壁纸 Esc／Shell 恢复等未完成项一并延期。

## 2026-10-06 第二轮修订（窗口待验，壁纸暂停）

用户确认普通房禁拖、保留抚摸；桌宠拖动保持。原左右物理边界以外改用棕灰斜线区域和虚线边缘，不再延续正常墙／地面材质。地面只将绘制上沿抬高 10 DIP，物理地面、左右限制、身体和相机投影不变。

窗口与壁纸复用首页暖白／棕灰菜单，删除“继续游戏”和标准／近景／全景按钮，显示 A/D、S、空格、滚轮、Shift+左键横移、Esc 说明。保留回门廊、控制中心、窗口／壁纸及恢复团子。壁纸 Esc 现在打开只含菜单的顶层蒙版窗口，保持 Wallpaper、源 HWND、代际、session、实体与单帧 owner；菜单期间清输入并阻断玩法。再 Esc 关闭时仅尝试经验证的 Shell 焦点恢复，外部失焦及隐藏不抢前台。关闭成功不等于 Shell 恢复成功。

### 当前实际验证

| 范围 | 结果 | 证据和限制 |
|---|---|---|
| 初次相关组 | 66 通过／2 失败 | `room-revision-20261006-focused-r1.trx`。150% DPI 布局取整产生 0.333 DIP 居中误差，以及新展开菜单未完成布局就检查按钮焦点。改为半个设备像素容差、布局后检查真实焦点，仍保留实质断言。 |
| 修订相关组 | **70/70 通过** | `room-revision-20261006-focused-r2.trx`。普通房禁拖／不补点、桌宠默认拖动、抚摸附件反馈、输入取消、两套菜单、相机、bridge，以及真实壁纸菜单同源／同 owner 和 Hide 清理。环境测试同时检查实际绘制像素、原生地面及左右限制不变。 |
| 受影响扩展组 | 226 通过／2 失败 | `room-revision-20261006-wide-r1.trx`。轻点反馈的固定时刻采样漏过了短暂闭眼帧；另旧 native baseline 环境变量缺失，旧模块合同未验证，不能拿本轮候选替代。其余 Home／Porch、相机、窗口、互动、资格与桥接门禁通过。 |
| 反馈帧定位及最终专项 | **24/24 通过** | `room-revision-20261006-ui-pointer-r2.trx`。中间失败保留于 `room-revision-20261006-ui-pointer-final.trx`、`room-click-frame-diagnostic-20261006-r1.trx`、`room-click-frame-diagnostic-20261006-r2.trx`。最后诊断实际观察到 3 个反馈帧、眼睛最小 openness 0.074，证明引擎已发布闭眼，Pump 返回后的最新值采样漏过。最终按真实 FrameApplied 记录同 generation、更大 StepIndex 的原闭眼阈值；下一手势先等双眼恢复，不重播、不强制 Tick、不改生产反馈参数。 |
| 壁纸 native 指针路由前置 | 未通过 | `wallpaper-pointer-route-20261006-r1.trx` 是测试初始化遗漏，修正后 r2/r3 均被真实前台／背景资格拒绝；r3 已知但 Shell foreground=false，尚未到 BeginPointer／捕获。不能据此认定捕获故障。 |
| 40 秒实体抚摸诊断首轮 | 未完成操作／测试失败 | `wallpaper-physical-petting-20261006-r1.trx`。低层左键、Shift Down 和资格观察均为 false；gameplay hooks 已释放。用户确认“没看到或来不及操作”，所以这轮没有定位输入故障。下一次等用户回复“开始”再打开临时壁纸计时，不以无事件冒充互动通过。 |
| 40 秒实体抚摸诊断第二轮 | 收到真实 Shift／左键，但未进入命令 | `wallpaper-physical-petting-20261006-r2.trx`。用户确认看到临时团子并在身体中央操作。低层 Down／Shift Down=true，Command Down=false，原生查询已知；未到捕获，不能认定捕获故障。该轮 BodyMiss 与资格为所有点击的聚合，包含先点击空白处，不能推断 Shift 身体点击未命中。 |
| Shift Down 专属诊断第三轮 | 未统计到未注入左键／测试失败 | `wallpaper-physical-petting-20261006-r3.trx`。所有未注入 Down 和资格观察为 false，真实 FrameApplied=true，gameplay／wheel hooks 均释放；该版本没有区分监听入口与 injected 事件，不能解释为用户未操作。 |
| 180 秒实体诊断第四轮 | 未统计到未注入左键／测试失败 | `wallpaper-physical-petting-20261006-r4.trx`。准备好后通过就绪布尔标记通知，仍没有未注入 Down，hooks 均释放。用户后续明确确认刚才一轮也在身体中央操作，并确认使用直接连接本机的鼠标／触控板；不得归因手势或用户未操作。下一轮补原 callback、raw Down、injected 分类和监听状态的聚合布尔。 |
| Esc 原生重复反例 | 2 失败／8 通过，随后专项全通过 | `wallpaper-esc-native-repeat-red-20261006.trx`。跨前台后 WPF IsRepeat 与原生 bit30 不一致时，旧行为分别误关长按菜单、拒绝真正新按。菜单现在优先使用精确本 HWND 的原生 Esc Down bit30，无有效原生消息才沿用 WPF fallback。 |
| 连续移动资格反例 | 1 失败／24 通过，随后修正 | `wallpaper-pointer-refresh-red-esc-green-20261006.trx`。同一生产 refresh helper 在合法背景水平移动 1 像素时被旧静止判定拒绝；同组 Esc 10 项全通过。新逻辑检查刷新点的范围、桌面角色与图标，保留之前的否决，仍保留 20ms 预算、两次全局扫描及最终前台／修饰键复核。这个反例证明代码合同缺陷，尚未证明用户现场失败走了该分支。 |
| 输入与菜单最终相关回归 | **103/103 通过** | `wallpaper-pointer-esc-green-20261006.trx`。最终点移动／越界／其他应用／任务栏／图标／unknown、之前否决保留、Esc native/WPF差异及 fallback，gameplay／wheel bridge 和输入 policy 均通过。 |
| 带监听入口聚合的第五轮实体诊断 | 真实 Down／命中／捕获通过，抚摸失败 | `wallpaper-physical-petting-20261006-r5.trx`。键盘／鼠标 callback 与非 injected Shift Down=true；存在前台否决，也存在已知 Shell 合格、ShiftBodyHit=true、干净 Down 及 CaptureAccepted=true。没有 Move 动作或新反馈，pending 被取消；Shift 检查包含 RefreshIcon 和预算过期，unknown=true。证据排除了该轮完全没输入或捕获从未成功；聚合结果仍不能把某一次取消唯一归因于某一阶段。用户同时反馈操作卡顿，后续检查低层 hook 的同步资格等待。两个 hooks 均已释放。 |

各组包含重叠，计数不相加。已查看仅渲染本房／本菜单的 `room-visible-boundaries.png` 和 `wallpaper-central-menu.png`；不捕获外部桌面，渲染树图不代表实际桌面合成或人工验收。持续按住打开菜单的 Esc、真实菜单关闭后 Shell 恢复、实际壁纸抚摸与跨屏 DPI 仍待实时／人工验证。

用户明确表示 Shift 手势仍无效，当前没有将问题归因于忘按 Shift。资格查询期间要求光标完全静止的代码合同缺陷已由失败反例证明并修正，但尚未证明是用户现场失败的根因；宿主捕获前置仍没有实际失败证据。没有弱化 20ms 预算、其他应用／图标／任务栏否决或 injected 过滤。真实低层平台／命令包装只原样转发一次，诊断只保存汇总布尔和阶段分类，不保存坐标、输入历史、外部窗口身份或内容。壁纸抚摸尚未宣告修复。

### main 整合与卡顿修订（进行中）

按用户新增授权，把 `main@70dfe36` 合入本任务分支，不修改主工作树或日常安装。合并前保存本任务修订为 `94c69a7`。四处冲突保留两套隔离 review、设置与 mutex；正常产品／普通房 review 接房门，FaceReview 仍无房门。普通房追加同 runtime 的眼色和外观实体绑定；外出首页取消编辑草稿，隐藏并禁用新增工具，恢复时保留编辑态布局，迟到动画不能覆盖外出隐藏状态。

`wallpaper-hook-red-main-integration-20261006.trx` 实跑 **8 失败／29 通过**：新增 main／房间集成的 5 个实例均通过，8 个失败均为已定义的旧 hook 同步完整检查及重入反例。集成测试检查外观 Home→Porch→普通房窗口→Wallpaper→窗口→Porch→Home 的实际原生帧、同实体与单 owner，不冒充真实硬件输入。

卡顿修正用单独 `WallpaperGameplayEventGuard` 表示排队守卫，只检查事件时上下文、前台、修饰键、范围和桌面窗口角色；守卫没有全屏或图标字段，不冒充完整资格。低层键盘／鼠标回调不再枚举所有应用窗口或同步等待 Explorer 图标查询。Dispatcher 应用每条玩法命令前仍执行完整的最大化／全屏、事件点／当前点／刷新点图标与桌面、前台、修饰键和代际复验；unknown 继续拒绝。完整检查期间到达的合法输入可排队，嵌套周期复验延期；失格输入仍取消且不能在恢复后补播。

此变更把全屏／图标判定移到动作应用前，没有保留它们在事件瞬间的完整历史，不沿用旧事件全字段测试作为该历史的证据。快速守卫仍采用原 20ms cooperative 预算；没有增大预算或放行其他应用。滚轮桥本轮保持原事件完整检查。真实反馈、鼠标回调耗时和用户卡顿感受仍需新实体诊断证明。

合并后的最终受影响组 `main-room-input-integration-green-20261006-r1.trx` 实跑 **407/407 通过，0 失败／0 跳过**。覆盖房间／单 owner／环境／禁拖／抚摸、两套菜单、独立 review、捏脸与外观保存／渲染、首页布局与跳跃阴影、原生外观跨呈现、快速守卫／完整命令复验／重入，以及原滚轮桥和 policy。旧 native baseline 缺输入和已知真实前台环境项没有包含在该组中；各组重叠，不相加。

main 的吸收已保存为本地合并提交 `3ba6621`（父为本任务修订 `94c69a7` 和精确 main `70dfe36`），主工作树、远端和日常安装不由本任务修改。卡顿修正与第六轮实体诊断当前仍在任务树，尚未记录人工通过。

手工诊断现进一步要求干净起始帧、同一次 Shift Down／新反馈 Move／正常 Up，以及真实 FrameApplied 中同实体、同附件 generation、更晚 StepIndex、Soft 双眼及左眼 openness<.4；允许 Up 后等待闭眼帧。取消或新 Down 不拼接旧结果。更新测试辅助代码已编译；关闭手工环境开关时实际结果为 0 通过／0 失败／1 跳过，见 `wallpaper-physical-petting-disabled-20261006-r1.trx`，不计作实体通过。gameplay／wheel hooks 和事件订阅均有释放检查。

本轮代码仍在隔离任务分支且尚未提交，main 与受保护 DLL 保持；未合并、推送或更新日常安装。新产物 `artifacts/local-room/review-revision-20261006-r2/` 已 Release 构建为 0 warning／0 error，native hash 与隔离输入一致；源为 `0682c96` 加本轮未提交修改，`delivery.json` 明确记录 dirty 源及汇总结果。当前尚未启动，先等待用户真实手势诊断就绪；L1–L3 未标记人工验收通过。

## 第一轮行为（历史候选）

- 房间不再显示常驻操作栏。Esc 打开全窗半透明蒙版和中央菜单，第二次 Esc 收起；菜单提供继续游戏、回门廊、控制中心、呈现切换、恢复团子及三档视角。保留极简隐藏／关闭窗控及拖动区。
- 控制中心可以和房间独立显示。外出首页隐藏团子及外观入口，显示“团子去玩拉！回来才能换衣服哦”。打开首页、设置或诊断不取得房间帧 owner，也不自动返回门廊；隐藏房间仍保持外出状态，明确返回后恢复首页能力。
- 共享 driver 用同一投影逆矩阵接入真实物理拖动及现有 Shift+左键抚摸。菜单、失焦、隐藏、宿主切换、缩放／resize／DPI 更新和失效帧取消手势及自有捕获；不改变物理参数或重建实体。
- 壁纸键盘和指针输入只在实际桌面 Shell 前台，且所有显示器没有可见非 Shell 最大化或全屏应用时生效。鼠标还需落在目标屏背景，排除图标、任务栏、菜单及其他应用。空前台和未知判定拒绝；仅 Shift 被抚摸路径允许，滚轮与键盘仍拒绝修饰键。Esc 转为窗口呈现，在目标健康首帧后打开中央菜单。

Windows 的“没有应用焦点”按桌面 Shell 前台解释；最大化工作区与覆盖整个显示器不同，任一显示器的最大化应用都阻断是用户本轮要求，严格程度高于上一版只排除全屏。

## 定位与验证

旧逻辑确有两个合同缺口：不识别覆盖工作区的最大化窗口，且仅认可壁纸目标所在 Shell 根，未单独认可经过系统 Shell HWND、进程、类名和根关系验证的桌面窗口。对应两项反例在修改行为前实跑失败，见 `wallpaper-input-contract-red.trx`。

真实 Shell 诊断已建立桌面前台，并仅记录阶段／角色布尔和耗时，未保存窗口标题、内容、身份、坐标或输入记录。采样事件 20ms 预算没有过期，拒绝来自指针下的其他应用窗口。不能将这个环境拒绝写成超时根因，也不能为了通过而放行其他应用。`wallpaper-input-diagnostic.trx`、`wallpaper-input-contract-red.trx`、`wallpaper-input-point-diagnostic.trx`、`wallpaper-input-uncovered-diagnostic.trx`保留诊断失败证据；旧候选已精确结束，仍有外部应用覆盖测试区域。

首批 UI／控制中心／原生 pointer 及相关 Home 布局回归 **50/50 通过**，见 `room-ui-pointer-first.trx`。随后验证如下，计数存在重叠，不相加为一次完整测试结果：

| 门禁 | 实际结果 | 证据和限制 |
|---|---|---|
| 首次扩展回归 | 203 通过／2 失败 | `room-review-revision-first-wide.trx`。一项发现隐藏门廊仍被当作健康首帧，已修复生产代码：确认时同时要求可见场景、可见身体及有效视口；失败返回恢复房间。另一项旧 native baseline 未提供，不得用候选模块替代。 |
| 受影响扩展回归 | **206 通过／0 失败／0 跳过** | `room-review-revision-final.trx`。覆盖普通房、pointer、窗口菜单、控制中心／Home／Porch、相机、滚轮与 gameplay bridge、资格 policy、Shell 查询 gate、调度及 proxy 优先级；明确排除需真实前台的 Wallpaper 类和缺 baseline 的旧模块测试。 |
| 最后补充组 | **20 通过／2 失败** | `room-review-revision-real-final.trx`。19 项 gameplay bridge 合同通过，包含最后新增的资格查询重入取消旧 pointer 反例；1 项真实最大化工作区 native fixture 通过。另两项在建立本房真实前台的断言处失败，下文单独列出。 |
| 隔离 Release 构建与静态检查 | 通过 | 新输出 `artifacts/local-room/review-revision-20261005-r1/` 构建为 0 warning／0 error，输出模块 hash 与输入一致；`git diff --check` 通过。构建不证明实体互动或人工验收。 |

最后补充组的两个失败项是 `ActualWorkerWRoundTripKeepsEntityAndSourceUntilHealthyFrameAndReleasesWallpaperResources` 和 `ActualNativeScanRecognizesBackgroundFullscreenWhileOwnRoomIsForeground`，均未能满足 `IsLocalRoomForeground` 的真实前台前置断言。日志保留，未弱化断言，也未用上一版通过记录替代本轮结果。2026-10-05 该组尚未取得 WorkerW 往返及房窗前台下后台全屏扫描的通过证据；2026-10-06 的复验见下文。

`ActualNativeScanRecognizesMaximizedWorkingAreaApplication` 在本轮通过：只创建自己的最大化测试窗，确认实际 `IsZoomed` 为真、DWM 工作区几何不是整屏，再验证分类阻断和全显示器扫描。不调整外部应用窗口。旧模块 `OldModuleRunsHomeButRuntimeResetCannotPretendPorchCapabilitiesExist` 因缺 `DANGO_PHYSICS_BASELINE_PATH` 输入未验证，首次失败证据保留；候选 native 不是 accepted baseline。

已查看仅渲染本房的 `room-window.png`、`room-menu.png`、`room-away-home.png`，分别对应纯色墙／连续地面、全窗蒙版中央菜单及外出提示。未捕获外部桌面，RenderTargetBitmap 不代表真实桌面合成或用户人工验收。

## 2026-10-06 真实桌面复验

用户通知“已就绪（你再检查一下我也不确定）”后，先检查真实条件，不调整其他应用。首次 `wallpaper-input-ready-20261006-r1.trx` 失败：Shell 前台已建立，查询已知且未超时，但仍有阻挡窗口。独立只读状态检查确认当前唯一主显示器有 1 个最大化应用、0 个全屏应用、0 个未知窗口，见 `desktop-readiness-20261006-r1.json`；只记录数量与状态，不保存窗口标题、内容、身份或输入。

用户将最大化窗口还原后，以 `--no-build --no-restore` 精确 filter 逐项串行复验，在所有测试 host 退出后才启动人工候选：

| 实时项 | 结果 | 当前证据 |
|---|---|---|
| 实际 Shell 前台背景事件资格 | **1/1 通过** | `wallpaper-input-ready-20261006-r2.trx`。15 次事件采样中 6 次背景资格接受，9 次因图标或其他应用覆盖拒绝；没有最大化／全屏阻挡，没有预算过期。证明存在可用桌面背景，不等于实体滚轮／键盘／拖动／抚摸已通过。 |
| 真实 WorkerW 往返 | **1/1 通过** | `wallpaper-roundtrip-ready-20261006-r1.trx`。原前台断言、真实 HWND 父关系、Hook 安装／释放、同实体／session／快照、单 owner、健康首帧前源保留及退出资源释放均保持。 |
| 房窗前台下后台全屏扫描 | **两次均失败** | `wallpaper-fullscreen-ready-20261006-r1.trx`、`wallpaper-fullscreen-ready-20261006-r2.trx`。仍在本房真实前台前置断言失败，未到达扫描断言；保留未验证，不能据此认定扫描器错误或已通过。 |

没有修改生产代码或测试断言来取得这些结果。实体输入及用户视觉／操作确认仍待用户反馈；本轮仅一块显示器，跨屏矩阵未验证。

## 交付与边界

受保护旧 DLL hash 保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；隔离 native 输入为 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`。无新依赖、网络／数据库、房间持久化或服务端业务。上一版候选验收 PID 21076 的路径核实后已结束，日常安装不变。

新隔离候选已构建至 `artifacts/local-room/review-revision-20261005-r1/bin/Dango.Desktop/release/`，使用 `--local-room-review` 启动时拥有独立 mutex／设置。2026-10-05 用户回复“一会验证，就绪了我会告诉你”，当时仅准备产物。2026-10-06 复验后已核对 source commit `8948eab`、托管程序集／EXE／native hash，再启动此既有候选，未重新构建或改变日常安装。验收 PID 为 28172，路径与候选精确一致；进程响应正常，UIAutomation 只读取该 PID，确认“Dango 控制中心”实际可见。已提供进入普通房及切壁纸的实体操作清单，等待用户反馈；进程存活和 UIAutomation 可见性不代替人工验收。

验收路线：进入普通房后检查无常驻操作栏；Esc 开／关中央菜单，菜单期间不响应玩法和滚轮；从菜单／托盘打开控制中心，首页只显示指定外出提示，设置可用，房间 owner 不变；明确返回后恢复首页外观入口。窗口前台下检查滚轮、A/D/S/空格、拖动和 Shift+左键抚摸。壁纸在所有显示器无最大化／全屏应用且桌面 Shell 前台时检查上述输入；再逐项检查应用前台、图标、任务栏、菜单、其他应用覆盖及主／副屏最大化／全屏否决，失格后没有旧输入补播；壁纸 Esc 切窗口，在健康首帧后打开中央菜单。

本轮人工确认仍需在真实桌面露出且所有最大化窗口已还原的条件下完成；混合 DPI、热插拔、长期动作手感及实际桌面合成没有由本次自动门禁证明。人工验收待用户确认；修订保存在任务分支本地候选提交，main 保持原 HEAD，未合并、未推送、未更新日常安装或部署。无本轮业务端口或数据库；隔离产物、测试结果及候选设置保留，交付状态记录于新产物目录的 `delivery.json`。
