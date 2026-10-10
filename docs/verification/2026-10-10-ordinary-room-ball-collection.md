# 普通房皮球与多件家具：本地候选验证

日期：2026-10-10。状态：**M0–M3 实现与自动验证完成；M4 实际产品短验证通过，600 秒有效观察及本轮人工功能验收待完成。当前批次未标记已验收。**

依据[批准计划](../planning/2026-10-10-multiple-furniture-ball-room-plan.md)。用户批准官方皮球与总共最多 30 件家具、仓库双击生成预览后确认、重入恢复皮球确认锚点。实现分支 `codex/furniture-ball-room`，基线 `63f1222d3916d2c460ea9fb02686e47afec6e529`。上一轮单平台的人工通过和 600 秒观察不作为本轮通过证据。

## 实现范围与文件

- Core：不可变且结构值等的家具集合、官方平台／球实例、普通房 schema 2、单实例 Insert／Move／Delete、30 件上限、保存锚点碰撞与出生区保护。严格读旧普通房 schema 1，仅成功编辑时写新版；独立 Trial 仍为原单平台合同。
- native／Physics：新增可选 collection ABI，最多 30 件，同一世界的团子－平台、团子－球、球－球和球－平台联合求解；稳定 native 实例 ID、完整共同安全帧、Probe／Prepare／Apply／Cancel／Read。旧 ABI 保留，旧单件 API 不能截断集合。
- Desktop：`OrdinaryRoomFurnitureSession` 与现有 `DesktopPhysicsScene`、唯一帧 owner 接线；GUID 选择及单件操作，实际球中心／转角绘制，隐藏保持动态状态，离房卸载而不保存运行位姿，重入恢复确认锚点。
- Panel／View／Window：仓库两列真实家具图片，双击一个预览、确认才添加；30 件后仍可移动／删除；圆形命中、单独预览、方向微调、原手套与镜头。球沿用半径 66 DIP、q 半径 1.1、质量 .02、重力 .5；球面不授予接地或跳跃。

不接入可调参数、自定义 PNG、新圆形团子、账号／云保存、多人、桌宠或壁纸房家具。主线合并、推送、安装、部署、受保护 DLL 替换不在本轮授权中。

界面遵循 `desktop-ui-design-v0.1.md` v0.1 §§2–6：共享暖白／棕色资源、图标操作、中文提示及辅助名称、微调／参数／仓库三段侧栏、常驻细滚动条、固定确认反馈。仓库使用实际家具图片是已批准的内容展示方式，参数区仍提示后续开放。未另建整套主题。

## 自动门禁

| 验证 | 实际结果 | 本地证据 |
|---|---|---|
| Core 家具合同与操作 | 98/98 通过，0 跳过 | `artifacts/ball-room-core-store-results/ball-room-core-r4.trx` |
| Store 真实文件与租约 | 48/48 通过，0 跳过 | `artifacts/ball-room-desktop-tests-results/ball-room-store-r1.trx` |
| 最终 native collection r5 | 1507 检查，0 失败 | `artifacts/furniture-collection-native/r5/collection-0.txt` |
| 故意关闭团子－球／球－球碰撞 | 分别产生 2／5 个预期失败 | 同目录 `collection-1.txt`、`collection-2.txt` |
| 旧 native 球与平台 | 球 1037 用例／244104 检查通过；平台通过 | 同目录 `legacy-ball.txt`、`legacy-platform.txt` |
| 最终托管物理集合及回归 | 146/146 通过，0 跳过 | 同目录 `managed-results/collection-and-regression.trx` |
| 最终受影响 Desktop 回归 | 254/254 通过，0 跳过 | `artifacts/ball-room-desktop-tests-results/desktop-regression-r5.trx` |
| 实际 Desktop 与友元工具 Release 构建 | 0 警告、0 错误 | `artifacts/furniture-collection/product-final-r2` |

新反例覆盖两球双向接触、球落平台、团子反作用及真实方向输入／转角、球顶不能跳跃、30／31 件、陈旧 token、事务拒绝、完整故障安全帧、独立单件修改、旧文件原字节保留和未知状态保护。无集合的 1／2／3 团子轨迹与原 EFEB 基线零容差一致。准备／应用不另造世界或帧 owner。

只读审查发现两个实际遗漏，先复现失败再修复：集合平台的团子着地摩擦／被动转动衰减遗漏；全局 touched 使无关地面球无法睡眠。r5 使用每个团子的实际支撑摩擦与逐球接触标记，原睡眠边界保持地面球；删除平台仍须令物体真实下落。独立修复复核未发现确定缺陷。相关 r4、r4-repro 与 r4-repro-sleep 证据保留，不能作为最终候选。

早期 WPF 球图失败源于解析 Geometry 已被冻结，修改 Transform 前改为 Clone，随后 UI 10 项通过。桌面 r3 的 206 通过／48 失败是构建命令漏传候选模块造成加载缺失；显式复制 r4 后 254/254，通过最终 r5 再次 254/254。早期 UI／Session 的错误缺能力基线也保留，后改用专用普通房 baseline 环境变量。未删除失败结果或改旧对照 hash。

## 候选身份与实际产品行为

最终 native r5 SHA256：`BA6E92D57CAFBD5CC806F709890AA1E8ED784F97CF42A09F4168EE08065E97A0`。

受保护 `src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll` 前后仍为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。旧单平台及单球脚本的固定候选 hash 未改写。

`product-final-r2/source-snapshot.json` 记录构建前实际相对路径／逐文件 SHA256，并于构建后核对没有漂移；完整快照 SHA256 为 `0AA58684FAA736FE14B6D26B75F0D5DFC471BA7497570FDDC3AC54E48E4123E7`。当时 sourceDirty=true，基线 HEAD 不代表已测试的所有源码。manifest 另记录实际 exe、托管程序集与两处复制 native 的 hash。本记录与计划状态是在测试后补充的文档，不构成产品源码变更。

实现本地提交为 `e1293f634d6ee3776a0aefbafe15ff2bb759afbe`。提交前还仅移除了两个新增验证 helper 的末尾空行以通过 staged diff-check；未修改其执行语句。`delivery-source-delta.json` 逐项核对构建快照与交付文件，差异仅为计划状态及这两处空行，另新增本验证文档，产品／native／测试源码无测试后变更。后续只更新本记录不另造候选。

最终 `product-final-r2/verification/verification-summary.json`：实际产品 `DangoApplicationHost`／`LocalRoomWindow`、同一世界、同一原团子，30 件真实混合家具（24 平台、6 球）、80 次逐操作保存、50 次房间往返及 50 次编辑往返、两次不同 OS 进程真实保存／加载。球被实际 driver 方向输入推动，中心／转角渲染核对通过；重新进房回到确认锚点且保存字节不变；隐藏不推进物理，恢复不替换任何 native ID。正常退出核实帧 owner、原生世界及所属窗口均已释放。耗时 32.9433 秒，保存提交边界 p95 4.1165 ms、最大 7.2850 ms，scheduler faults=0。

初始化每颗球前经过真实退出编辑／返回门廊／重新入房，显式核对世界引用、generation、实体 ID、健康帧、相同已保存布局、团子合法出生位置和既有球实际锚点。新增初始化往返不计入 50 次循环；不关闭自动行走、不吞掉碰撞失败。随后动态推动、隐藏、50 次循环与观察仍走正常产品行为。

保留的 `product-r5` 实际短验证曾在第 27 件（球 800/66.1）因碰撞拒绝失败：此前 24 平台及两球已保存，独立进程恢复与正常释放通过，但 30 件及循环门禁没有完成。当时与桌面窗口回归并行，主进程布置约 19 秒。布局静态几何没有重叠，产品编辑中保留随机短途行走；因此自然移动／窗口时序是候选解释，但当时未记录身体位置，**未确认根因**。顺序 `product-r5-repro` 同样位置完整通过，随后稳定化真实初始化的 `product-final-r1`、增强初始化断言的最终 r2 也完整通过。未因此改碰撞容差、产品行走或门禁；最新工具在拒绝时记录候选与完整物理帧。后续桌面窗口验证顺序执行。

native 真实 30 件混合场景 600 步 p95 1.4823 ms、最大 2.1182 ms，属于原生步耗时，不代表产品 FPS。实际绘制另导出最小 640×420、横向 1200×620、竖向 640×800、默认 960×600 的离屏 96／144 DPI 图片。正常与竖向画面核对通过；最小高度仓库需要滚动，自动测试核实滚动到底可访问。既有最小尺寸镜头／身体裁切限制未在本轮扩展修复，也未将离屏渲染作为真实 OS DPI 通过。

## 人工现场、开放门禁与交付

新实际候选 `product-final-r2/bin/Dango.Desktop/release/Dango.Desktop.exe --local-room-review` 已打开，PID 35896，初始标题“Dango 控制中心”，使用该构建内独立 `local-room-review-state`。自动 30 件压力数据位于独立 verification-data，不污染人工场景。`manual-window.json` 记录 PID、启动时间、native hash、数据与窗口身份。

为避免单实例 mutex 激活旧版本，仅结束准确核实路径及启动时间的旧任务 review PID 1984；旧数据逐文件 hash 前后不变，保留 `previous-review-stop.json`。没有停止日常安装或其他 Dango 进程，没有删除任何数据或 worktree。新人工进程保持运行；所有自动 verifier 及重启子进程正常退出，无网络服务、端口或数据库操作。

本轮人工待验证：从门廊 E 或房内 Esc 进入编辑；仓库真实双击生成一个预览、确认后添加；重复平台／球、移动和删除指定实例；皮球顶起／滚动手感；离房重入恢复布置。软件调用和自动窗口测试不替代硬件鼠标／键盘。

本轮持续观察待完成：仍需 600 秒有效活动，窗口可见、未最小化、有活动焦点、健康唯一 owner 且有 native step 增加；失焦时间排除。内部观察墙钟为 720 秒，外部 owned-process watchdog 为 900 秒，超时失败。短验证 RequiredActiveSeconds=0，不能报持续观察通过。已询问用户方便的观察时间，先保留功能验收现场。

真实 OS 150% DPI、高对比／减少动画与既有 59 FPS 专项继续未验证；WPF Rendering 回调不能代替 compositor Present 或 59 FPS。平台上球暂不睡眠，公共 SurfaceContact 主要为地面诊断，平台碰撞真实参与求解。没有在本轮主张这些专项通过。

当前交付仅隔离本地候选及本地提交；未合并 main、未推送、未安装、未部署、未替换生产 DLL。待本轮人工与活动观察完成后，才可更新相应门禁状态。
