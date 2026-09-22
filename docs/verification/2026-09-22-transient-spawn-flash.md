# 两种桌面宿主短暂闪回出生点修复

日期：2026-09-22。用户报告日常版本更新后，Window 和 Wallpaper 都会偶尔消失一帧、出现在出生点后闪回。本记录针对该回归；此前批准延后至多人候选的兼容与持续运行专项仍按原安排执行。

## 根因与修改

性能改动将宿主改为 `CompositionTarget.Rendering` 驱动、固定步长推进物理。`HostFrameScheduler` 在判断本回调是否有物理步之前调用 `PrepareMotionFrame()`；两个宿主的准备方法会将 Core 场景快照中的初始坐标写到身体 Canvas。Core 的 `ObservePresentationFrame` 并不把物理位置回写为场景坐标，因此当本回调 `Steps == 0`、没有执行 `ApplyPresentationFrame` 时，出生点会短暂成为屏幕上的位置。下一次物理步才恢复真实位置，帽子仍在之前的位置。

旧调度每次写入后都会推进并呈现，掩盖了这个临时写入；新的零物理步回调使问题暴露。真实旧二进制和能失败的宿主回归均复现同一链路，不是推测的物理世界重置。

修复提交 `83293977ad19ff7df563c4b6840a1f16a568de1d`：

- `src/Dango.Desktop/DesktopHost/Window/DangoSceneWindow.xaml.cs`：准备阶段不写团子坐标；布局或恢复时如已有 `LastFrame`，完整绑定身体与帽子的物理呈现帧，只有首帧之前使用 Core 初始位置。
- `src/Dango.Desktop/DesktopHost/WorkerW/WorkerWWindow.xaml.cs`：相同修复；首次家具布局也不能覆盖已有团子物理位置。输入代理仍随实际呈现帧更新。
- `tests/Dango.Desktop.Tests/DesktopHost/HostPresentationContinuityTests.cs`：7 个真实 WPF/native 物理用例，覆盖两宿主零步与重复回调、下一物理步、已有帧的宿主布局、无帧时初始化，以及 Wallpaper 首次家具布局。

未改物理参数、渲染帧率、固定步长或 native DLL，没有通过重建物理世界或隐藏团子规避问题。

## 自动验证

先在未修改的宿主上运行新增回归，Window 与 Wallpaper 都在零步回调的实际身体坐标断言失败；下一物理步又返回实际位置。两组记录分别为：

- Window：`(282.713144, 449.259441)` → 零步 `(171.392, 442.4)` → 下一步 `(283.425215, 449.265479)`。
- Wallpaper：`(287.321144, 486.859441)` → 零步 `(176, 480)` → 下一步 `(288.033215, 486.865479)`。

修复后，新增用例与帧调度、时钟、阶段计时、物理宿主接入、双宿主配置、轻互动及安静待机相关回归 **74/74 通过，0 跳过**。Release 构建通过；三个修改文件的 whitespace 验证与 `git diff --check` 通过。只读独立复核未发现阻塞项。

证据均位于本工作区被忽略的 `artifacts/flash-regression/`：

- `red/host-presentation-continuity.trx`：修改前失败。
- `green/flash-targeted.trx`：修改后 74 项结果。
- `format/`：修改文件格式检查。

测试使用隐藏、非激活、屏外的 fixture；Wallpaper 控件只放在测试自身 HWND 内，不附着 Explorer，不使用用户设置。未重跑完整 Desktop 套件，旧 DLL 的 `dango_box2d_self_bodies_create` 缺失导出失败仍是继承边界。

## 真实宿主观察

本机临时观察器从已发布二进制创建正常产品 `DangoApplicationHost`，采用独立虚构设置，运行真实 Window 或附着桌面的 Wallpaper；不注入输入，不截图，不读取其他应用内容。每次 `CallbackCompleted` 后比较身体 Canvas 坐标与物理 `LastFrame`，记录是否属于零物理步。这里的帧数是呈现回调数，不是 GPU 扫描或人工逐帧观感结论。

| 候选 | 模式 | 时长 | 有效回调 | 零物理步回调 | 坐标错位次数 | 最大误差 DIP |
|---|---|---:|---:|---:|---:|---:|
| 更新后旧版 `8c46a0f` | Window | 20 秒 | 1163 | 19 | 19 | 37.611828 |
| 更新后旧版 `8c46a0f` | Wallpaper | 20 秒 | 1187 | 23 | 23 | 201.514835 |
| 修复版 `8329397` | Window | 20 秒 | 1089 | 11 | 0 | 0 |
| 修复版 `8329397` | Wallpaper | 20 秒 | 1136 | 22 | 0 | 0 |

旧版全部错位均发生在零物理步回调；修复版明确覆盖同类回调且零误差。四次均健康、退出码 0。证据为 `live-before-v2-window/`、`live-before-v2-wallpaper/`、`live-after-window/`、`live-after-wallpaper/` 下的 `summary.json` 与对应的 `*-exit.json`。初次 `live-before-window` 曾因观察器访问非公开事件失败，属探针错误，原记录保留；修正事件订阅后重新执行上述对照。

另外从同一候选创建独立正常产品宿主，调用实际 `DesktopHostCoordinator.SetMode/SetVisibility`，执行 Window → Wallpaper → 隐藏/恢复 → Window → 隐藏/恢复。全程约 29.18 秒，身体与帽子都与 `LastFrame` 一致：

| 阶段 | 回调 | 零物理步回调 | 身体/帽子错位 |
|---|---:|---:|---:|
| Window 初始 | 243 | 5 | 0 |
| 切至 Wallpaper | 209 | 12 | 0 |
| Wallpaper 隐藏 | 0 | 0 | 0 |
| Wallpaper 恢复 | 294 | 6 | 0 |
| 切回 Window | 302 | 1 | 0 |
| Window 隐藏 | 0 | 0 | 0 |
| Window 恢复 | 304 | 6 | 0 |

合计 1352 回调、30 次零物理步回调，最大坐标误差 0；非活动宿主没有回调，各阶段状态与模式符合预期，无 runtime 错误。观察进程正常 Dispose，退出码 0；两类观察器均无残留进程。证据为 `lifecycle-candidate-8329397/summary.json` 与 `process-exit.json`，本机观察源码/独立输出位于 `lifecycle-probe/`。该验证仍限于 WPF 呈现回调之后的坐标，不替代 GPU 像素或人工视觉验收。

## 候选包与保护

候选位于本工作区 `artifacts/desktop-entry/spawn-flash-8329397/package/`，版本 ID 为 `83293977ad19-63945cc70d88`，format 2、win-x64、self-contained、`sourceDirty=false`。清单 SHA-256 为 `63945CC70D8805A3B7A4BA2BC444AD4C23866A286F92CAD1F4621B7612BEA9C1`。

既有 `Test-DangoPackage` 校验 478 文件通过，设置契约 `dango-desktop-settings-v4` 与五项设置源哈希一致。真实观察器和候选包的三份 Dango 程序集、两份 native DLL、`coreclr.dll` 指纹逐项相同，见 `package-probe-fingerprints.json`。

设置基于本次问题调查开始时的 `protection-before.json` 核对，SHA-256 保持 `6FE69CF9D5B4CFC15E6EB35239458AA25392FE97EAC21A08F1685017BB8632AC`；不沿用用户操作前的历史设置指纹。统一模块 SHA-256 仍为 `00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`，旧 DLL 仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。调查与候选验证期间未更新日常安装；后续授权交付如下。

## 后续授权的本地交付

用户收到修复及验证结论后，对“合入本地 main，并更新日常版本”回复“合入”。据此执行本地整合与日常安装更新，不据此补记尚未发生的人工视觉验收。

- 开始时任务分支为 `1f59be4`，阶段分支与 main 均为 `3ccfd19`，三处工作区干净。先将 `phase/1-desktop-life-prototype` 快进至 `1f59be4`，再将 main 快进至阶段分支，无冲突。范围仅已批准的测试延期文档、闪回修复与回归、验证记录；产品代码与实测候选 `8329397` 完全一致。
- 再次校验候选 478 文件、设置契约与源哈希，执行现有安装器 `-StageOnly`；确认 current 未变化、设置字节未变化后激活。当前版本变为 `83293977ad19-63945cc70d88`，previous 为 `8c46a0f8b85d-066a12d5a250`；更早的 `333ec9c2eeee-8b67926bf3bd` 也保留在原目录。没有实际执行回退。
- 更新前已无日常实例运行。通过实际桌面快捷方式冷启动，启动器退出 0，唯一产品 PID `15744` 来自修复版本目录；重复打开启动器退出 0，仍为同一实例并显示控制中心。
- 通过仅限该 PID 的 UI Automation 执行 Wallpaper → Window → Wallpaper。两个模式均报告场景 Ready、统一物理 world 运行、附件/互动/待机无故障、原生错误 0；Wallpaper 附着成功且父句柄有效。恢复原 Wallpaper 模式后，设置 SHA-256 与更新前完全一致，未用备份回拷。
- 运行进程实际加载修复目录中的统一物理模块，两份 native DLL 指纹仍与上文一致。关闭检查用控制中心后，保留该日常实例供用户使用，探针残留为 0。本轮没有执行日常进程退出测试，也未重跑无代码变化的 74 项回归和性能长测。

交付证据位于本工作区 `artifacts/flash-regression/local-delivery/`：`before.json`、`package-validation.json`、`staged.json`、`activated.json`、`cold-start.json`、`repeat-start.json`、`capture-ui.json`、`window-ui.json`、`wallpaper-ui.json`、`final.json`。初次旧 UI 脚本只从桌面根的直接子节点查找，漏掉了已存在的控制中心；随后改为补充读取经 PID/路径核对的主窗口句柄，再完成检查，未修改产品。

## 交付边界

- 修复和本记录已按授权经阶段分支合入本地 main，日常安装已更新并启动；未推送远端或部署。
- 当前日常版本为 `83293977ad19-63945cc70d88`，旧版本和设置保留。当前 previous 是带该闪回问题的更新前版本，回退会重新带回该已知问题。
- 真实坐标观察通过不等于用户人工视觉验收；该修复的人工观感待用户确认。
- 未重跑性能长测，未宣布 GPU 呈现、第二 DPI、多屏、Windows 10、长期稳定性或整个 Phase 1 通过；此前原始失败与延期安排保留。
- 未启动网络服务、数据库或外部资源；本地证据和候选文件保留，不删除其他版本、数据或工作树。
