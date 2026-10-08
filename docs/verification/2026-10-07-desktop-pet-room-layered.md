# 桌宠房间单人分层候选验证

日期：2026-10-07。首轮状态：单人首阶段候选已实现，首轮自动验证通过并隔离启动；修订候选已于 2026-10-08 获用户“桌宠房间通过”的人工确认，自动性能差异保留，见下方修订记录。

此处保留首轮历史，下文待人工验收及 r1 运行状态均为首轮交付时快照。后续大小／边界、拖动性能、双面消失反馈及 r2 人工验收见[修订验证](2026-10-07-desktop-pet-room-feedback.md)；首轮通过结论不代表修订性能通过或消失根因已解决。

## 范围与实现

依据[已通过设计](../planning/2026-10-07-desktop-pet-room-layered-design.md)和[实施计划](../planning/2026-10-07-desktop-pet-room-layered-implementation-plan.md)，从 `main@8ff78e0` 在独立分支 `codex/desktop-pet-layered` 实施。此前退回候选及用户撤回修改保留原样，不沿用其通过结论。

真实环境由 WorkerW 上的独立场景承载；独立透明置顶窗口消费同一身体、眼睛和帽子帧。两层共同使用固定主屏投影及一个物理运行时、实体和帧 owner。桌宠房门与普通房门分开，只有同次双层健康呈现后才收起来源并开启输入。包含点击、Shift 轻抚、真实物理拖动和同地面重力，整体隐藏／同状态恢复／返回门廊，以及不产生第二 owner 的控制中心。

新 review 参数、mutex 和包内设置目录与日常版分离；启动脚本校验显式隔离模块和受保护 DLL hash。无新依赖、网络、账号、数据库、安装更新或正式 DLL 写入。本轮没有 main 合并、远端推送、部署或完整阶段关闭。

## 定位与修复证据

- 首轮独立轮廓 region 测试暴露空 `PathGeometry.Transform`，修正为恒等变换，保留非空健康轮廓与原生 region 门禁。
- 真实抬升失败的 `desktop-pet-drag-05.trx` 显示：身体帧健康、目标未被夹取、原生节点已开始拖动，但 `pet screen bounds unavailable` 导致宿主失效停止 owner。游走包围函数要求帽子原生 pose 存在，拖动帧缺 pose 时无法描述已实际绘制的健康身体／帽子。
- 修正独立层包围依据为本次身体／帽子实际填充路径及真实变换，将其回填同一场景映射；不改物理、不移动替代 UI、不降低抬升门禁。`desktop-pet-drag-06.trx` 原测试 1/1 通过，验证抬升超过 65 world units、松手回到同一原生地面且实体与 owner 不更换。
- 保存外观 fixture 需在宿主加载前真正保存虚构外观，直接临时改 runtime 会被首页取消预览恢复保存配置；fixture 已修正，不改变产品保存规则或颜色断言。
- Win32 后台窗口的 `SetCapture` 仅覆盖自身可见区域，不能单独保证跨应用连续拖动。新增手势桥只在身体命中且自身成功捕获的当前手势内观察 move／up；没有键盘 hook、鼠标移动、输入历史或轨迹持久化。仅保留起点、有限极值、最后 move 和 up，Dispatcher 合并处理；结束、失捕获、菜单、隐藏、故障或关闭立即停止，解除失败静默重试，并阻止重复安装。
- `desktop-pet-health-bridge-before.trx` 实际为 17 通过、6 失败，复现折返样本丢失、旧 WM 重复处理和真实打盹唤醒误失效。修复保留有限极值与手势时间戳水位，up 前再次检查捕获资格；初始 Ready 仍严格要求三者同帧。运行中仅允许真实唤醒造成的一次健康检查间隔差异，新 apply 清除等待，连续第二次仍未呈现即失败。
- 最后审查发现较短反向越界可能被更远的有效样本覆盖，首次取消也可能被迟到极值重新排序。隔离 `artifacts/desktop-pet-layered/boundary-before-01/results/pet-boundary-before.trx` 三个真实 controller 反例均失败。改为每次实际 apply 刷新当前身体、投影、原有安全区域夹取和 8 world 竖向阈值的有限资格快照；hook 只做常量比较，一旦违规保持取消，不保存完整路径。600 ms 与动作判定继续由原 controller 裁定。
- 四边安全检查改为逐个 `Scene.FrameApplied` 观察新帧，保留原 2.5 秒／至少 6 新帧／实际身体帽子安全边距门禁；没有降低帧数或修改物理。
- 新增三项产品资格快照检查最初比较 `PointToScreen` 的整数结果与连续像素坐标，出现约 0.08–0.09 像素的固定量化差异。核对 [WPF PointUtil 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/Shared/MS/Internal/PointUtil.cs) 的取整后，测试改用自有壁纸 HWND 的 `ClientToScreen(0,0)` 实测原点与 `CompositionTarget.TransformToDevice` 独立计算连续坐标；生产不变，0.01 像素门禁、真实物理抬升及实际 driver 夹取结果对照均保留。
- 落地高度检查在 `desktop-pet-final-03.trx` 出现一次失败；未用随后两次定向通过将它关闭。`desktop-pet-settle-product-diagnostic.trx` 实际再次失败并定位：基准中心 Y 为 311.882、`Grounded=False`、0 接触、原生最大速度 1.8909；落地中心 Y 为 330.221、`Grounded=True`、3 接触，地面一直为 366.726，实体与 owner 不变。fixture 只等待双面 Ready，错误地将仍在空中的入房帧当作地面基准；测试前置条件与落稳等待据此修订，保持原抬升 65、下降 35、落地误差 10 和每段 4500 ms 期限，不修改生产物理。

## 当前实际验证

Release 桌面和测试构建均为 0 警告、0 错误；`git diff --check` 和 21 个变更 C# 文件的空白格式检查通过。自身视觉树截图已查看，环境不画第二个身体，独立团子透明层正常；未截取真实桌面或其它应用内容。

证据均保留在本任务隔离 artifacts：

| 组别 | 当前结果 | 证据 |
|---|---|---|
| 新桌宠合同／真实宿主／原生定向输入组 | 最终 77 通过、0 失败、0 跳过 | `desktop-pet-final-04.trx` |
| 受影响普通房／门廊／帧与轻互动 | 首批 227 通过、2 缺旧基线输入失败、15 未执行 | `affected-regression-01.trx` |
| 真正旧模块能力反例补跑 | 补齐只读旧基线输入后 2/2 通过 | `old-module-regression-accepted.trx` |

15 项未执行来自普通房原生平移夹具未取得精确前台窗口焦点，属于 Inconclusive；未强夺用户焦点或降低资格门禁。旧模块反例补跑后，受影响组为 229 通过、15 未验证。最终计数按 `(testId, testName)` 去重，不累计重复运行的通过数量；失败过程证据保留，不写成最终通过。

新组 77 项与受影响组有 4 项重叠，三个最终证据文件合并去重为 **302 通过、15 未验证、0 最终失败**。桥合同 29 项、产品快照／健康 5 项、入口 10 项、映射／交接 15 项、原生前台定向输入 4 项、产品宿主 14 项组成新组。最后只修复测试地面基准和连续像素 oracle，生产与既有回归覆盖区域保持一致；不机械重跑已通过的全部历史测试。

文档范围内相对链接及评审脚本语法已核对。隔离候选通过评审脚本构建，0 警告、0 错误；以 review 参数启动后核对了自身进程仍在运行。此检查证明候选实际启动，不替代人工体验或长时稳定性。

## 模块与交付边界

| 用途 | SHA256 |
|---|---|
| E1 隔离物理候选 | `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507` |
| 只读真正旧模块反例 | `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C` |
| 未替换的受保护跟踪 DLL | `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A` |

桌宠 HWND 定向消息、原生 region 和自有虚构前台窗口检查不能替代真实鼠标路由与用户人工体验。前台夹具为自有同线程普通／最大化窗口，确认身体空白角被原生 region 排除、原窗口和编辑焦点保持；不将这些结果写成真实硬件或跨进程全部点击路由通过。

已知限制与未验证项：

- 原生 region 包含帽子，而手势起点只接受真实身体。可见帽子填充内、身体外点的跨进程下层点击路由仍待验证；`HTTRANSPARENT` 的官方语义只保证继续搜索同线程窗口，已有夹具不能关闭此项。参见 [WM_NCHITTEST](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest) 与 [Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows)。本轮不改变帽子手势语义，也不增加第三显示层。
- 身体轮廓外的睡眠漂浮 z 字样被 Path-only 原生 region 裁剪，属未完成视觉细节；睡眠眼睛保留。后续须一起明确外沿视觉与点击穿透设计。
- 真实硬件点击／轻抚／跨应用连续拖动、其它系统缩放、其它显示器、独占全屏、其它置顶软件、长时稳定性尚未验证。窗口平台、双人联机与跨重启房间存档不在本期范围。

人工链仍为：房间环境固定壁纸→团子位于普通／最大化应用上方→点击／轻抚／拖动→同地面重力→轮廓外操作下层应用→隐藏恢复→返回门廊。只有用户实际通过后才能标记人工功能验收。

## 隔离候选与 Git 交付

入口为 `eng/run-desktop-pet-room-review.ps1`，必须显式传入已核对的隔离模块及新的 `RunName`，保留已有构建输出；参数 `-NoLaunch` 仅构建。review 使用独立互斥体、包内 `desktop-pet-room-review-state` 设置目录与 `--desktop-pet-room-review` 参数，自动进入真实分层房间。

包内 `start-desktop-pet-room-review.cmd` 可重复启动同一个隔离候选，自动携带 review 参数。

持久候选位于 `artifacts/desktop-pet-room/review-20261007-r1/bin/Dango.Desktop/release`，以 review 参数打开后持续运行，作为本轮留给用户的交互进程；本任务 testhost 已退出。隔离 artifacts、包内设置与失败证据保留，不清理其它任务进程或数据。

候选以 `codex/desktop-pet-layered` 本地任务提交归档。本轮不合并 main、推送远端、更新日常安装、替换受保护 DLL 或关闭完整 Windows／正式房间／多人阶段。
