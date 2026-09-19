# 物理模块正式宿主接入：验证与人工交接

> 状态：宿主接线已实现，用户已确认本轮人工验收通过；完整静态门禁仍有一项历史旧 DLL 导出失败。
> 当前范围：Phase 1 / P1-004 的物理消费入口，不是完整动作或 Phase 1 总验收。

2026-09-12，用户在本轮隔离正式宿主测试交接后确认“验收通过”。此结论针对本次 Window/Wallpaper 物理接入交付，不沿用此前双体或三体预览验收。用户未提供逐项清单、设备及 DPI 组合或故障现场结果，本文不补写这些明细，也不自动勾选图形化清单。人工通过不消除下面的旧 DLL 自动门禁失败，不代表完整动作、整个 Phase 1 或发布验收完成。

## 1. 本轮实现

- `StackedDangoSceneRuntime` 已消费 `DesktopPhysicsScene → DangoPhysicsWorld`；Window 与 Wallpaper 共享同一实例。原生算法、参数版本 1、ABI 1、16 节点、3% 接触和三体支撑规则未改。
- 同一世界真实添加/移除，数量 1～3；按稳定 ID 选择和绑定输入，移除选中实体且保留幸存者。颜色槽随实体保存，不因显示数组压缩而变色。
- 三只视觉均支持拖放。图标下按 ID 维护输入代理，图标上由共享透明覆盖窗先画全部身体、再画全部帽子。空白区不接收输入。
- 隐藏/失焦/Esc/失捕获/重置/移除清理捕获和输入；释放拖动不重复制造跳跃边沿。被清除的按住键须释放后再按，不能通过重复按键事件传给新选中实体。
- 新增实体选择可见区域空位；切换/缩放只平移显示，不改物理坐标或重新创建世界。超大场景会提示视口容纳不足，不缩放身体或强行传送。
- 参数只读；缺库/错误接口和运行错误可识别、故障锁存，不静默使用旧物理。控制中心周期刷新宿主状态。
- 新人工清单有 20 项，Window/Wallpaper 与 1/2/3 体分别留痕。旧验收草稿不作为本轮通过依据。

## 2. 自动验证与证据

### 可失败门禁

- 参数写入先行红灯：`artifacts/p2d-physics-desktop/red/parameter-policy-red.trx`。
- 正式 runtime 数量开放先行红灯：`artifacts/p2d-physics-desktop/host-red/host-red.trx`；旧实现实际返回 false。
- 接线后、测试迁移前完整 Desktop 192/196，4 项失败：两个旧单体锁定断言、一项旧迁移继续拖动断言，以及历史旧 DLL 导出缺失。证据：`host-before-test-migration/host-before-test-migration.trx`（位于同一证据根目录）。前三项按本轮已批准契约迁移，未放宽数值容差；旧 DLL 检查保留。
- 小视口出生测试发现固定实验偏移会导致窗口外出生；修正可见区域空位选择和显示边缘余量后，480×360、800×600、1920×1080 三例通过。红灯保留在 `host-layout/host-layout.trx`，复验为 `host-viewport/host-viewport.trx`。

### 实际覆盖

- M2 显示场景适配和 M3 正式 runtime 各自完成 1/2/3 体、每例 900 帧严格对照（各 2,700 帧），所有物理状态、轮廓、ID、接触计数和错误码与已验收模块直接调用相同；不把两个层级的验证混称新的人工验收。
- 运行时专项涵盖任意实体移除、稳定 ID/颜色、失效 ID、数量边界、暂停恢复、迁移取消拖动、缺库锁存、compiled WPF 三体绑定/帽子绘制顺序、空白命中、持续按键抑制及三种视口。
- Core：36/36 通过，`artifacts/p2d-physics-desktop/core/core.trx`。
- Desktop 最终完整回归：211/212 通过，`artifacts/p2d-physics-desktop/host-complete-regression/host-complete-regression.trx`。这不是全绿；唯一失败仍为 `SharedSelfWorldExportsAreAvailable`，旧 `dango_box2d.dll` 没有历史 `dango_box2d_self_bodies_create` 导出。本轮新 runtime 不消费该导出，但没有删除或跳过这项旧检查，也没有替换旧 DLL。
- Physics 常规回归：32/32 通过，`artifacts/p2d-physics-desktop/physics/physics.trx`；显式排除 `DangoPhysicsStabilityTests` 长测类，不声称该长测本轮通过。
- WPF 渲染产物已检查：`artifacts/p2d-physics-desktop/render/window-three.png`、`above-icons-three.png`。它们证明真实模块帧与 WPF 绑定/绘制，不等于桌面图标、真实鼠标或高 DPI 实测。

Release/self-contained 隔离发布成功；显式复制的统一模块哈希一致。`--smoke-test` 返回 0，启动进程已退出。受影响文件的 `dotnet format --verify-no-changes`、文档链接、启动脚本语法和差异空白检查通过。

## 3. 隔离人工运行

从仓库根构建并运行：

```powershell
.\eng\run-physics-host-review.ps1 -Build
```

已有本轮构建时，只启动：

```powershell
.\eng\run-physics-host-review.ps1
```

该脚本只发布托管桌面程序并复制已验收的统一模块，不重建 native。它校验模块哈希；若本测试程序正在运行，构建会拒绝覆盖，不自动结束进程。发布目录是 `artifacts/p2d-physics-desktop/host-review`，运行参数为 `--physics-host-review`。

测试版单实例与普通应用分开；个人正式设置不被改写。测试设置与新验收草稿仅留在该发布目录下的 `physics-review-state`。首次进入普通窗口并打开控制中心，WASD 已开启；下次按该测试目录保存的设置恢复。

### 建议人工顺序

1. 普通窗口先单体：A/D 移动、S 下压、W 跳跃，拖放、Esc、失焦、最小化/恢复。
2. 点击“添加团子”分别验证双体和三体。轻拖一次选择目标，松开后用键盘控制；“移除团子”作用于选中实体。
3. 三体独立验证持续碰撞、偏心叠放、拖走/移除底部支撑、逐只反复拖放。“重置当前团子”不恢复已移除的实体。自然滑落是此前已认可行为，不要求永久不滑落。
4. 切到壁纸层，重复对应清单；三体分别验证图标之上/之下、空白区输入穿透、桌面图标、任务栏与焦点。
5. 至少两种分辨率或缩放；往返宿主至少三次，最后从控制中心/托盘退出并检查残留。无法执行的项目标记未验证。

清单入口为控制中心“实验与验收”。逐项人工填写，不自动勾选通过。岛仍是占位显示，本轮未实现爬岛/真实岛平台；抚摸、自主行动和完整动作状态闭环仍需先完成 B 的详细设计。

## 4. 保护与未验证边界

- 正式旧 DLL SHA-256：`6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。
- 已验收统一模块 SHA-256：`00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C`；测试输出复制相同字节。
- 使用原 `p1-004-box2d-soft-body` Worktree、`feat/p1-004-box2d-soft-body`，HEAD `ad33146`；已有 dirty 修改保留，未提交、合并、推送、部署或创建发布标签。
- 没有修改原生算法、正式旧 DLL 或已验收预览 DLL。未关闭旧预览，不删除测试证据或用户数据。
- 本轮没有重跑十分钟稳定性长测；本次宿主接入已获用户整体人工认可，但桌面图标优先、DPI/跨屏、Explorer 恢复和故障现场未收到逐项证据，仍不声明这些专项矩阵全部通过。不能将旧实验三体通过写成本轮正式宿主三体通过。
- 物理快照及测试输入都是虚构场景数据。仅在交互需要时读取鼠标与当前控制键状态；不记录真实键盘内容、桌面截图或用户窗口信息。

## 5. 实际启动与残留状态

隔离候选已用 `--physics-host-review` 启动，普通窗口和控制中心留给用户人工操作。检查时 PID 为 19652，进程响应，实际加载模块来自 `artifacts/p2d-physics-desktop/host-review/dango_physics_module.dll`。这只确认启动及模块加载；并未因此勾选双宿主手感、图标优先、DPI 或三体人工通过。

该交接实例有意保持运行，测试设置/草稿的保存目标为独立 `physics-review-state`（首次启动检查时尚未生成该目录）。后续关闭或重建前重新核实进程路径，不直接复用历史 PID；从控制中心/托盘正常退出。此前预览未被关闭，本轮未启动服务器或端口。
