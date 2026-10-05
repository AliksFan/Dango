# 普通房人工评审修订

日期：2026-10-05。依据用户对上一版候选的反馈及其补充的壁纸键盘、拖动、抚摸范围，在 `codex/local-room-implementation` 继续修订。人工验收尚未通过。

## 本轮行为

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
