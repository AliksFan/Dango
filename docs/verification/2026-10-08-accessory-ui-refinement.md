# DIY 配饰第二轮界面与实时预览验证

日期：2026-10-08。分支：`codex/accessory-diy`。修订起点：`fc5ccddf11f625334b22a557a862df8c4d9eb16b`。依据已授权的[设计第 11 节](../planning/2026-10-07-accessory-diy-design.md#11-已确认的第二轮界面与操作修订)及[实施计划](../planning/2026-10-07-accessory-diy-plan.md)。用户要求分组图标排列在团子左侧、参考捏脸界面。本报告记录当前候选和失败修复，不代表人工验收通过；[首版报告](2026-10-07-accessory-diy-implementation.md)保留其历史范围。

## 范围与实现

- 四个分组为“配饰库／图像与位置／柔性与连接／旋转与重力”，使用 32 DIP 线图标，在团子左侧竖排，沿用捏脸主题与选中反馈。切组保留选中项和未应用草稿，成功切组滚动到顶并把焦点返回预览。参数位于左侧，复位／取消／应用固定在预览下方。
- 素材库改为 `ListBox` 加四列 `UniformGrid`，显示真实透明素材、名称和佩戴状态。缩略图最长边 48 像素，绘制最大 48 DIP；按可见卡片懒加载，缓存最多 32 项，离屏清空 `Image.Source`。条目显式保留 `✓ 名称`／`名称` 的 UI Automation 名称。
- 缺失或损坏图片显示 `!`，Tooltip 和 `ItemStatus` 标记“图片不可用”，保留原条目及全部配置。正常内置回退、尚未加载和离屏有效图片不误报；修复素材后，新 session 的 `Bind` 清除旧失败缩略缓存，同一 `AssetId` 可以恢复。
- “创建新配饰”仍先选择静态透明 PNG，成功后新增换装草稿并打开图片编辑模态。模态取消仅撤销此次图像编辑，保留新草稿项；外层取消／Esc 恢复最近保存的整套配饰，撤销未应用的新项。不新增独立创建事务。
- 换装团子以实际 native 身体接受 A／D 移动、S 压扁、W／Space 同一跳跃。W 不进入门廊；两种跳跃来源独立跟踪，交错松开不能清除另一来源仍保持的意图。失焦、关闭、取消和切宿主清空控制意图。
- 文本、数字、下拉和模态输入隔离，按钮 Focus 时 Space 保留标准操作。滑条箭头／拖动、图库和支点鼠标操作由控件消费；未用于控件编辑的 WASD／Space 进入实时控制。外层数值框中的 Esc 也关闭换装并丢弃非法输入缓冲，模态 Esc 仅执行模态取消。
- 自由地面使用滚动局部原点：`abs(CoreX) >= 4` 时调用现有 `TranslateHorizontal` 和 `ClearHorizontalBounds`，宿主累计连续世界坐标。身体保持在有效 native 地面范围内，不靠扩大有限房间实现；不重建 body、entity 或 presenter，不增加求解步，不修改 native DLL／API。配饰和步态消费连续运动帧，避免原点平移产生虚假冲击。
- 镜头沿用普通房的水平跟随、低跳地面构图、中线以上垂直跟随和实际绘制身体下方边距。相机使用实际分轴缩放与布局尺寸；地面阴影与身体使用同一实际投影，移动使用 render transform，避免逐像素 Margin 取整。

主要实现见 [AccessoryEditorPanel](../../src/Dango.Desktop/Accessories/AccessoryEditorPanel.cs)、[换装接线与相机](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.Accessories.cs)、[实时输入](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.LiveScene.cs)、[首页布局](../../src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.xaml)、[相机规则](../../src/Dango.Desktop/AppShell/Rooms/RoomViewController.cs)、[滚动原点](../../src/Dango.Desktop/Physics/Presentation/DesktopPhysicsScene.cs)和 [runtime](../../src/Dango.Desktop/Rendering/Dango/StackedDangoSceneRuntime.cs)。

## 静态与自动门禁

已完成轮次的结果取自原 TRX counters，均为零跳过；轮次相互重叠，不能相加作为累计通过数。以下失败证据保留，不用最终重跑覆盖。

| 轮次 | 总数 | 通过 | 失败 | 证据（仓库相对路径） |
|---|---:|---:|---:|---|
| 功能 r1 | 205 | 194 | 11 | `artifacts/accessory-verification/accessories-ui-v2-functional-r1.trx` |
| 功能 r2 | 210 | 204 | 6 | `artifacts/accessory-verification/accessories-ui-v2-functional-r2.trx` |
| 功能 r3 | 220 | 212 | 8 | `artifacts/accessory-verification/accessories-ui-v2-functional-r3.trx` |
| 集中 r4 | 63 | 55 | 8 | `artifacts/accessory-verification/accessories-ui-v2-focused-r4.trx` |
| 集中 r5 | 63 | 55 | 8 | `artifacts/accessory-verification/accessories-ui-v2-focused-r5.trx` |
| 集中 r6 | 63 | 55 | 8 | `artifacts/accessory-verification/accessories-ui-v2-focused-r6.trx` |
| 跳跃专项 r7 | 10 | 10 | 0 | `artifacts/accessory-verification/accessories-ui-v2-jump-r7.trx` |
| 镜头修复后受影响功能组 | 222 | 222 | 0 | `artifacts/accessory-verification/accessories-ui-v2-functional-final.trx` |
| 最终图标卡片修订后的同组 | 224 | 222 | 2 | `artifacts/accessory-verification/accessories-ui-v2-layout-final.trx` |
| 同一最终构建隔离重跑失败方法 | 4 | 4 | 0 | `artifacts/accessory-verification/accessories-ui-v2-isolated-recheck.trx`；包含三个光栅参数组合及真实进程重启 |
| 同一最终构建的可见窗口性能 | 1 | 1 | 0 | `artifacts/accessory-verification/accessories-ui-v2-final-performance.trx` |

- 最新 Release 构建：**0 警告、0 错误**。最终图标卡片修订输出为 `artifacts/accessory-ui-v2-final-build`；先前各轮输出为 `artifacts/accessory-ui-v2-build`，二者保留。
- 受影响 C# 的 `dotnet format whitespace --verify-no-changes` 与 `git diff --check` 已通过；提交前再核对最终文档。
- 最终 224 项集中组中的两项失败，分别是 150% 等效大图光栅耗时超限和已打开验收实例导致重启测试主动拒绝；同源隔离重跑均通过，原红灯保留。不能将本次集中组写成 224/224 全绿，也不能将重跑四项与其相加。

完整受影响功能组 filter 为：

```text
(FullyQualifiedName~Dango.Desktop.Tests.Accessories|FullyQualifiedName~RoomCameraPresentationTests|FullyQualifiedName~DesktopPhysicsSceneTests|FullyQualifiedName~ProductHomeInteractionTests|FullyQualifiedName~HomePresentationLayoutTests|FullyQualifiedName~UnifiedLiveHomeTests|FullyQualifiedName~LocalRoomProductTests|FullyQualifiedName~HomeJumpShadowTests)&FullyQualifiedName!~ActualVisibleWindowRendersEightFlexibleAccessoriesWithMeasuredDpiAndFrameCadence
```

集中组包含 `HomeJumpShadowTests`、`AccessoryEditorTests` 和 `AccessoryPreviewControlTests`。八件可见窗口性能用例单独执行，不能从功能组结果推断性能通过。使用已核对哈希的本地模块作为显式环境输入，命令如下，`$taskFilter` 为上面的完整 filter：

```powershell
dotnet build tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj -c Release --artifacts-path artifacts/accessory-ui-v2-final-build "-p:DangoPhysicsModulePath=$env:DANGO_PHYSICS_MODULE_PATH"
dotnet test artifacts/accessory-ui-v2-final-build/bin/Dango.Desktop.Tests/release/Dango.Desktop.Tests.dll --filter $taskFilter --logger 'trx;LogFileName=accessories-ui-v2-layout-final.trx' --results-directory artifacts/accessory-verification
```

正式 DLL 仍为 SHA-256 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`；隔离候选 `BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`，旧对照模块 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`。本机模块路径只作为命令输入，不进入已跟踪配置。

## 真实 WPF、native 与进程门禁

这些是自动门禁设计及已执行轮次的检查范围；最终是否通过以上节最终结果为准，不替代用户试用。

| 门禁 | 可失败的实际检查 |
|---|---|
| 自有 WPF 素材库与布局 | 1024×640、640×420、480×800 检查真实四列图、左侧 32 DIP 图标、草稿往返、固定 footer 和实际命中可达；三列、右侧页签、过大图标、footer 随滚动及团子改尺寸的错误组合必须失败 |
| 参数滚动与直接应用 | 宽档用 1024×560，另保留 640×420、480×800；同时断言 `ScrollableHeight > 0`、`VerticalOffset > 0` 和真实按钮应用／重载，不用没有 overflow 的布局冒充滚动 |
| 编辑、坏图和输入 | 完整缓冲提交／拒绝、创建两层取消边界、全部参数快照、坏图配置保留及修复恢复；真实 Focus／按钮路径、W＋Space 交错松开、文本／模态隔离、失焦清理及 Esc 取消 |
| 无限地面真实轨迹 | 300 预步后 4600 步，先 1600 步右行再 3000 步左行，越过旧地面两端 ±800 DIP；同一 entity、逐步位移小于 4、持续地面接触、至少 6 次 rebase、grounded 步数大于 4500，求解步数每 tick 恰增一 |
| 配饰／步态连续性 | 300 预步后 1800 步右行，身体和配饰 StepIndex 同为 `301 + index`；rebase 前后真实轮廓支点绑定、mesh 与弹性无假冲击、步态相位连续。独立宽地面对照只作比较，不是产品实现 |
| 相机与地面阴影 | 真实跳跃、入场、缩放、切组和往返；按实际 RenderSize／分轴投影检查接地间距 0–2 DIP、阴影投影 0.5 DIP、相机中心／中线误差 1e-6。原门槛保留 |
| 首帧前冷入场 | 初始化 runtime 后明确断言没有 LastFrame、产品视图未 Loaded；先在隐藏视图打开换装，再 Show／Pump。打开隐藏编辑器不推进身体；显示后仍为同一 entity 和唯一 owner，真实轮廓可见、准备状态完成，无永久锁定的暂时错误。预热后再打开的夹具不能覆盖此回归 |
| 跨进程冷启动 | 第一 PID 以 UIA 打开库、选行、调用“图像与位置”图标，等待已选状态；聚焦身体附着 X 数值，用 ValuePattern 从 0.27 改 0.73，不先 Enter／Tab，直接 Invoke“应用”；先确认 UI 完成，再单次独立磁盘确认，正常退出后新 PID 恢复 |

用例见 [AccessoryHomeUiTests](../../tests/Dango.Desktop.Tests/Accessories/AccessoryHomeUiTests.cs)、[AccessoryEditorTests](../../tests/Dango.Desktop.Tests/Accessories/AccessoryEditorTests.cs)、[AccessoryPreviewControlTests](../../tests/Dango.Desktop.Tests/Accessories/AccessoryPreviewControlTests.cs)、[AccessoryUnboundedPhysicsTests](../../tests/Dango.Desktop.Tests/Accessories/AccessoryUnboundedPhysicsTests.cs)、[HomeJumpShadowTests](../../tests/Dango.Desktop.Tests/AppShell/HomeJumpShadowTests.cs)和 [AccessoryProcessRestartTests](../../tests/Dango.Desktop.Tests/Accessories/AccessoryProcessRestartTests.cs)。

跨进程用例仍用 session 预先导入虚构素材，没有自动操作文件选择器或图片编辑模态；完整创建与编辑链路需要人工试用。镜头修复后成功运行的第一 PID 14740、第二 PID 25472 均正常退出，退出码 0；重复 PID 19744 被单实例边界正常拒绝。附着 X 从 0.27 经真实焦点字段直接应用保存为 0.73，并在第二进程恢复。日志为 `artifacts/accessory-verification/process/c677c60f89f94fac93c3dbfc0c0e856e-restart/process-events.jsonl`。最终卡片构建隔离重跑第一 PID **13132**、第二 PID **25324**，正常退出码均为 0，重复 PID **11908** 正常拒绝；同一 0.73 磁盘及新进程恢复断言保留。证据为 `artifacts/accessory-verification/process/9365d59f738341cdb86e60adc8c842ba-restart/process-events.jsonl`。

可见性能用例使用实际 **150% ×150% DPI、tier 2、1085×583 DIP** 窗口，八件完整 2048px 源图、256 DIP、柔软度 100，连续 180 测量帧。镜头修复后的同源测量 median **11.21 ms**、p95 **15.70 ms**、max **20.54 ms**，通过原 p95 <16 ms 门槛。Rendering 约 **60.25 FPS**，间隔 p95 **16.60 ms**，>100 ms 间隔 **0**；raster p95 **12.45 ms**、上传 p95 **1.61 ms**。证据 `artifacts/accessory-verification/accessories-ui-v2-performance.trx`（1/1）。此值是配饰求解与绘制专项，原性能波动风险仍保留。

最终卡片构建的同一可见用例为 median **10.60 ms**、p95 **15.81 ms**、max **21.41 ms**，通过原门槛；Rendering 约 **60.25 FPS**，间隔 p95 **16.60 ms**，>100 ms 间隔 **0**；raster p95 **12.32 ms**、上传 p95 **1.65 ms**。截图 `artifacts/accessory-ui-v2-final-build/bin/Dango.Desktop.Tests/release/evidence/accessory-a0/actual-visible-eight-accessories.png`。两次可见测量及等效光栅测量不重复计数，也不代表所有产品场景的帧时保证。

## 失败定位与修复

- **缩略图首次不加载**：未加载 `Image` 没有 intrinsic size，以其 ActualSize 判断可见性会形成“未加载就不可见”的循环。现依据已测量卡片边界决定加载，保留懒加载、缓存上限及离屏清理。
- **数值框首入场 disabled**：`RefreshFields` 读取父容器禁用后的 effective `Slider.IsEnabled`／editor getter，再写 numeric／reset 的 local 值，导致父容器恢复后数值仍禁用。现按 editable、`LockRotation`、`LimitSwing` 计算自身规则，不把继承禁用固化。
- **冷启动夹具暴露同一禁用问题**：r1 超时日志明确数值框 `IsEnabled=false`、`IsOffscreen=false`，slider enabled；不是滚动或旧 Expander 导航问题。证据为 `artifacts/accessory-verification/process/8307f318bbaf4a0385a0b06391f7fa01-restart/process-events.jsonl`，原 PID 21048、重复 PID 26576。修复后仍保留直接应用和独立磁盘／新 PID 恢复断言。
- **滚动夹具没有实际 overflow**：新单组 Placement 在 1024×640 已完全可见；一度改为 1024×420 后 footer 位于 447–479，超出 420，且违反宽窗 MinHeight 560。最终两个滚动用例宽档为 1024×560，实际 overflow、滚动距离和按钮可达断言均保留，未放宽产品布局。
- **图库夹具覆盖改名草稿**：fixture 外部 `session.Stage` 改名后普通 `Panel.Refresh()` 保留旧名称输入，切组又把“叶芽”提交回来。初始化改用 `DiscardInputsAndRefresh()`；名称断言不降级，产品保留未提交输入的机制不改变。
- **r3 八项旧阴影预期**：换装已采用相机世界地面，旧 Wardrobe 固定屏幕 shadow 预期不再适用；测试改查相机世界地面合同。Face／关闭编辑的 Home 仍保持原固定阴影合同。
- **r4 八项分轴缩放及像素取整偏差**：WPF 声明 Width／Height 与实际布局的分轴缩放、取整尺寸不同，移动 shadow 的 Margin 还会逐像素取整。源码从 `LiveSceneCanvas.TransformToAncestor(PetVisualArea)`、ActualWidth、实际 Y scale 与 origin 计算 viewport；阴影改用 `PetShadowTranslation` 对齐实际投影。0–2 DIP、0.5 DIP、1e-6 门槛均未扩大。
- **r5 首次入场投影时序**：实际分轴投影修正后，预布局 shadow translation 在随后 layout 尺寸变化后短暂偏移，切组前地面高度门禁仍失败。候选在布局完成后重新消费同一身体帧，不额外推进 native。
- **r6 阴影重新可见及竖屏构图误差**：第一个地面重新可见帧中，此前 collapsed 阴影的 RenderSize 为零，造成投影误差；竖屏落地构图另有 1.0625 DIP 偏差。现使用布局槽位基准 `X = area.ActualWidth / 2`、`Y = Margin.Top + Height / 2`，不读取 collapsed 尺寸；仅换装的身体 Canvas 和阴影关闭 layout rounding，Y 使用真实 transform，并在排版后消费同一身体帧。原阈值下跳跃完整类 r7 为 10/10、零跳过，随后受影响组 222/222 通过。
- **实际窗口少量卡片过高**：原单行 `UniformGrid` 拉伸到图库的 240 DIP 高度，48 DIP 图片虽正确，选中卡片仍过高。最终将网格顶部对齐，保持自然行高；新增 3／4 件实际 WPF 门禁要求卡片及单行高度 ≤70 DIP，图库仍为 240 DIP、四列且可滚动，不改缓存或图片预算。
- **最终卡片集中组的两个红灯**：150% 等效／2048px 源图光栅 p95 **17.89 ms** 超过原 16 ms 门槛；随后关闭自有验收实例、避免与本任务其他验证并行的同源重跑为 **12.51 ms**，通过原门槛。本轮未改求解／光栅实现、图像、柔软度或预算，原因没有被唯一确定，保留稳定性风险。另一失败是根代理在复验前已打开自有验收 PID 21148，共用 review mutex 导致测试明确拒绝启动；确认无模态且应用按钮禁用后对该 PID 的 UI 线程发送正常退出消息，保留数据，退出码未能读取。随后重启测试正常通过，未绕过单实例保护。

旧红灯、测试夹具修复和产品修复分别记录；不能将截图、数值算法或局部方法调用写成完整桌面验收。

## 保存与分享边界

沿用独立 `accessories.json` Schema 1 和独立图片资产，无迁移。当前 `AccessoryItem` 同时包含可复用参数与本地佩戴状态，尚未拆为 definition／instance；`Layer` 兼任默认图层及当前本地图层。

参数快照包含名称、图像、图片支点与身体附着点、造型尺寸／角度、默认 Layer 及全部物理参数。`IsEquipped` 与 `Order` 属于本机穿搭。未来完整分享需要图像加配置，单 PNG 不携带支点与物理参数；本轮没有分享、导出、云上传、多人或新 Schema。

## 人工验收、交付与保留项

- 本轮候选及原 A0–A4 **未获用户人工验收**；不标记整个 Windows 阶段完成。
- `artifacts/accessory-acceptance/Launch.cmd` 已指向 `artifacts/accessory-ui-v2-final-build/bin/Dango.Desktop/release/Dango.Desktop.exe`，使用原 `artifacts/accessory-acceptance/review` 设置和 `.dango-accessory-review.json` 所有权标记。最终实际产品换装窗口 PID **14592**，四个分组均 enabled 且 onscreen，并恢复原“小礼帽”选择，`ui-v2-final-launch-evidence.json` 保留启动证据。旧 PID 22560 已结束，首个 v2 PID 21148 已按上述空闲检查正常退出。
- 对精确自有 PID 的产品窗口捕获并检查实际画面，身体、已佩戴配饰、四分组及自然高度的四列卡片可见；截图为 `artifacts/accessory-acceptance/ui-v2-final-actual-window.png`。这是实际产品窗口画面，区别于未绑定 runtime 的布局 harness，仍不替代用户人工验收。
- 启动前后核对原 `accessories.json`、`settings.json` 和 `settings.window-preset.json` 的哈希均一致；未重建库或覆盖用户保存的配饰。旧输出、旧截图、失败日志和设置继续保留。
- 最终真实窗口／native／UIA 自动证据与用户人工体验分开。实际系统 100%／125% DPI 未验证；先前 150% 动态八件性能 p95 波动、Wallpaper 四项回归差异、初次完整回归的 22 跳过继续保留，详见首版报告，未在本轮宣布闭合。
- 本轮实现、测试及报告纳入本地任务分支提交，提交基线为本报告开头的 HEAD，具体新提交由 Git 记录。未合入 main、推送远端、更新日常安装、部署或替换正式 DLL。
- 自动测试的自有进程均已退出；最终验收 PID 14592 主动保留。没有启动监听服务、数据库或云资源。本机其他任务的 Dango 进程不属于本轮停止目标，未干预。
- 虚构图片、隔离设置、TRX、截图和编译输出主动保留；未进行数据库、云资源或持久数据删除操作。
