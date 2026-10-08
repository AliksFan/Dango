# 桌宠体验反馈修订验证

日期：2026-10-07，最终候选核对及人工验收：2026-10-08。状态：用户明确“桌宠房间通过”，本轮单人候选人工验收通过；大小／边界和功能回归通过，自动性能差异保留，消失根因尚未确定。

范围依据[体验修订](../planning/2026-10-07-desktop-pet-room-feedback-revision.md)。在 `codex/desktop-pet-layered`、首轮候选 `6b0cb85` 上继续。用户明确消失发生在拖动／松手时，房间和团子同时不见但托盘仍在；默认团子为之前 1.4 倍，边界取主屏可用区域并避开任务栏，不画边界。

## 实现与定位

共享投影以世界中心缩放为原值的 1.4 倍；移除额外 16 DIP 活动边距和地面下方 48 DIP 留白，横向物理墙及游走空间跟随可见世界。桌宠不画全高边界虚线和排除区斜纹，真实墙面／地面保留。普通房保持既有规则。

贴底后的回归四行失败显示点击／Shift 轻抚的接触点被拖动顶节点夹取上移约 40 world units，两面仍健康。修复仅在真实 controller 选定物理拖动后约束 native target，点击和轻抚保留 raw contact，hook 资格仍保持原竖向阈值、取消锁存和时间戳水位。没有修改真实物理参数或降低四边足迹检查。

受控真实双层宿主的拖动测试仍未复现用户原消失。原关闭判据、停止输入、双层收起与重试保留；新增本地一个最新故障报告，包含固定原因／原生消息和错误码、健康／捕获／owner 布尔值、UTC、scheduler 聚合计数。没有输入坐标、鼠标轨迹、外部窗口内容或原始异常消息。LostDesktop 和当前 driver failure 都在收起前采样；写入失败不阻断退出和恢复。此为后续真实操作定位工具，不是消失已修复的证据。

性能探针使用实际 WPF／原生宿主，输入平台受控，物理 owner、实体和帧真实。第一轮 feeder 的 Input 优先级被 Render 挤压，bridge admission 未扣 WPF 整数点量化，故输入延迟不能采用；修订为 Normal feeder 及 own HWND 逆投影后，保持 `1e-6` 消费关联及所有预算。

| 有效修复前测量 | 新应用帧／秒 | Window 定位／尺寸 p50／p95 | Window 全 Apply p95 |
|---|---:|---:|---:|
| 静置 | 57.07 | 0.15／0.61 ms | 2.72 ms |
| 直接真实 driver 拖动 | 29.03 | 24.93／40.26 ms | 42.43 ms |
| 生产手势桥受控 burst | 30.52 | 24.50／39.19 ms | 40.89 ms |

证据：`feedback-performance-stages-before-02.trx`。减少重复 Width／Height 设置及同步重绘标志后，拖动仍约 30–32 帧，定位阶段约 25 ms，不能认定解决。最终固定独立透明 HWND 为主屏物理可用区域，在其内部按共享投影移动真实团子，逐帧更新身体／帽子实际轮廓 region；Show 后只定位一次，拖动不再逐帧搬动原生窗口。此前尝试的 NOREDRAW／NOCOPYBITS 和专用 InvalidateRect 路径已撤回。

原门禁下的最终 20 秒测量如下，证据为 `feedback-performance-fixed-viewport.trx`：

| 固定窗口后的场景 | 新应用帧／秒 | 物理步／秒 | 宿主 Apply p95 | 输入到新帧 p95 |
|---|---:|---:|---:|---:|
| 静置 | 56.51 | 60.01 | 2.83 ms | — |
| 直接真实 driver 拖动 | 55.20 | 60.00 | 3.40 ms | 22.26 ms |
| 生产手势桥受控 burst | 56.79 | 60.03 | 3.55 ms | 21.96 ms |

三组 59 新帧／秒门禁仍失败。静置帧间隔 p99 为 51.0292 ms，超过 51 ms；静置和 direct 各有 12 次 >50 ms 间隔，超过各自允许的 11 次；bridge 有 6 次，符合停顿预算。三组均无 >100 ms 间隔，同步 CPU 门禁通过，拖动输入关联及延迟门禁通过，没有 fault、drop、overload、pause、clock reset 或未观测输入淘汰。不得据此标记整体性能通过。

唯一 WPF 回调／零步／单步／多步分别为静置 1158／28／1076／54、direct 1136／32／1020／84、bridge 1157／21／1079／57。唯一回调约 57–58 Hz，零步回调减少新帧交付，多步追赶保持物理约 60 步／秒；这些汇总不能将差异全部归因于 clock，也不能把物理步数当作应用新帧或 DWM 呈现帧率。

原生 API 依据：[SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)、[InvalidateRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-invalidaterect)、[WPF Window.cs](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs)。阶段耗时不包含随后排队的全部布局工作，应用帧率不是 DWM 呈现帧率。

## 证据与门禁

产物位于本任务 `artifacts/desktop-pet-room/verification-01/results`，失败与空筛选结果保留，不计为最终通过。

| 检查 | 当前结果 | 证据 |
|---|---|---|
| 新尺寸／workarea 合同修复前 | 7 行真实失败 | `revision-before.trx` |
| 首组拖动及映射／交接 | 31 通过，尚未复现消失 | `feedback-drag-before.trx` |
| 贴底回归修复前 | 14 通过，点击／轻抚 4 行失败 | `feedback-footprint-01.trx` |
| 点击／轻抚修复、映射、绘制、健康 | 19 通过，1 前台资格未验证 | `feedback-contact-after.trx` |
| 扩展拖动／故障报告与恢复 | 23 通过 | `feedback-drag-report-after.trx` |
| 有效性能修复前 | 纯失败能力检查通过，3 个真实性能门禁失败 | `feedback-performance-stages-before-02.trx` |
| 第一定位优化 | 3 个真实性能门禁仍失败 | `feedback-performance-after-01.trx` |
| 最终桌宠／手势桥非性能组 | 114 通过，0 失败、0 跳过 | `feedback-final-native-contracts.trx` |
| 受影响轻互动／普通房／相机回归 | 58 通过，0 失败、0 跳过 | `feedback-affected-regression.trx` |
| 固定窗口最终性能组 | 失败能力检查通过，3 个真实性能门禁失败 | `feedback-performance-fixed-viewport.trx` |

最终非性能组覆盖真实物理拖动与松手、四边实际身体／帽子足迹、点击／Shift 轻抚、健康、生命周期、故障报告写失败与恢复；另验证固定 HWND 抬升不移动，以及普通／最大化自有应用上的旧身体位置与远处透明区 WindowFromPoint 穿透。原生消息／受控输入通过与真实用户输入验收分别记录。Release 测试构建和新候选构建均为 0 警告、0 错误；本轮全部变更 C# 的空白格式和 `git diff --check` 通过。最终生产差异只读复审未发现新增阻止人工候选的问题。

真实输入来源和低层 hook／原生 WM 时间戳路由、其它显示缩放／屏幕、长时运行，以及帽子身体外命中的跨进程穿透仍需专项技术验证。睡眠 z 被 region 裁剪仍为已知后置项。定向 own HWND 消息、受控 hook 平台及视觉树图片不代替这些证据；本轮主屏候选的人工体验接受见下节。

## 用户人工验收

2026-10-08，用户在修订候选交付后明确回复“桌宠房间通过”。验收对象为 `211e58b` 对应的 `review-20261008-r2` 单人桌宠房间：真实房间壁纸层、独立上层团子、点击／轻抚／拖动、1.4 倍默认尺寸和避开任务栏的可用区域边界。

记录本轮候选人工验收通过，保留三组 59 FPS 及部分停顿门禁的自动失败；不修改预算、重写历史结果或据此确认原消失故障的根因。此确认不扩展为双人联机、其它显示环境、长时稳定或完整 Phase 1／Phase 3 验收，也未授权合并、推送、安装更新或受保护 DLL 替换。本次仅同步验收事实，不改代码或重新优化性能。

## 交付状态

本轮没有合并 `main`、推送远端、更新日常安装、替换受保护 DLL、账号／网络／数据库操作或关闭完整阶段。隔离物理模块保持 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`；受保护 DLL 保持 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

新隔离候选为 `artifacts/desktop-pet-room/review-20261008-r2/bin/Dango.Desktop/release/start-desktop-pet-room-review.cmd`，保留 r1。旧 r1 进程 PID 5464 经其自身设置页“退出 Dango”及自身确认对话框正常退出；未强制停止进程。新 r2 PID 16604 已以 `--desktop-pet-room-review` 打开，持续运行核对发现两个自有可见显示面：上层固定窗口覆盖 1920×1128 主屏可用区域，壁纸子窗口覆盖 1920×1200 屏幕；控制中心隐藏，包内隔离设置存在，无最新故障报告。包内物理模块及受保护 DLL hash 与上述值一致。其他分支进程保留，本轮没有启动服务、端口或数据库。

实现提交为本地 `codex/desktop-pet-layered@211e58b`，人工验收事实另作本地文档提交；完整项目阶段未关闭。本记录中的第一候选和过程证据不能累加为本轮最终自动通过。后续若再次双面消失，可读取候选包 `desktop-pet-room-review-state/desktop-pet-last-failure.json`，继续定位原故障。

## 2026-10-08 用户授权与本地 main 合并

用户在人工通过后明确要求“合并mian吧”，本次目标为已有本地 `main`。合并前本地 `main@8ff78e0` 和任务分支 `codex/desktop-pet-layered@d090005` 工作区均干净，前者为后者祖先；传入仅包含桌宠分层实现、体验修订和验收记录三个提交。

已使用 `git merge --ff-only` 将本地 `main` 从 `8ff78e0` 快进到 `d090005`，没有冲突或额外代码修改。快进后完整源码树与任务分支一致；`src`、`tests`、`eng` 与人工通过的 `211e58b` 候选实现一致。受保护 DLL 不在传入差异中，合并后 hash 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

本次沿用上述 114＋58 项功能／回归、候选构建及人工验收证据，没有重新运行桌面测试或性能探针；59 FPS／部分停顿失败和未验证项继续保留。仅新增本地合并记录文档提交，不关闭完整项目阶段；未推送、更新日常安装、部署、替换 DLL、停止候选进程或删除任务分支／Worktree。前文未合并的交付状态为用户授权前的历史快照。
