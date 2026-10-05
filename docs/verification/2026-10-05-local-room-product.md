# 本地普通房产品实现与验证

日期：2026-10-05。用户明确授权“通过，开始产品实现”。范围为本地单人普通房的窗口／壁纸两种呈现和已批准的视角机制；产品人工验收待用户确认，未标记阶段已验收。

## 实现范围

- 纯色无碰撞墙面与连续平坦碰撞地面；没有方块、家具、风景或房内场景门。固定逻辑世界 2160×360，左右为不可见物理安全边界。
- 窗口默认 960×600 DIP，最小 640×420；标准视距使用一次中性身体几何校准，目标可见宽约 154 DIP。近景 130%／全景和普通滚轮档间倍率按高度安全空间夹紧，窗口放大或变焦不修改物理世界。
- 窗口／壁纸各保留本次运行内的视距偏好。普通滚轮向上拉近、向下拉远，每 120 原生量增减标准倍率 10 个百分点，250 ms 停顿清余量。
- 首页 W → 门廊 → 普通房；房门／局部 Esc 返回门廊。× 隐藏并暂停；托盘恢复同一房间。只在实际前台房窗及场景指针位置处理玩法／滚轮，菜单先消费自身输入。
- 壁纸使用真实 WorkerW 子 HWND／HwndSource，在一个选定显示器呈现；地面避让任务栏工作区。团子右键及托盘提供视角、窗口切换和房门入口；背景不截获 Shell 鼠标输入。
- 壁纸滚轮要求 Shell 前台、所有显示器均无可见非 Shell 整屏应用、目标屏背景命中且不在图标／任务栏／菜单／其他应用上。每事件及实际应用前复核，未知则否决；只使用瞬时身份、关系及几何，不读标题／内容或保存外部窗口信息和输入。
- 两种呈现共享一个 session／runtime／实体及唯一帧 owner。切换先清输入并停原 owner，目标本次健康身体帧和后续渲染确认后才隐藏源；失败、取消、首帧超时及运行时故障保留恢复入口。

首版壁纸玩法键盘未接通；窗口支持 A／D 移动、S 轻压和空格跳跃。没有正式房间准入、权威快照、账号、多人、网络／数据库、房间磁盘存档或重启自动入房。

主要实现位于 `DangoApplicationHost.LocalRoom.cs`、`AppShell/Rooms/LocalRoom*`、`RoomViewController`、`RoomWheelAccumulator` 和 `DesktopHost/WorkerW/WallpaperWheelInput*`。既有 Home／Porch 仅增加受控暂停、健康帧返回及本地普通房入口，正式 `CanEnterRoom` 语义保持。

## 验证输入与静态门禁

实施分支 `codex/local-room-implementation` 从设计提交 `14e7a14` 建立。相机与壁纸输入分树开发后由单负责人集成；本地 `main` 仍为 `e2f4037ea2e56a222d41a5cd3085b21cee220565`，工作区干净。

使用隔离 E1 候选 `dango_physics_module.dll`，SHA256：`BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507`。通过现有 `DangoPhysicsModulePath` 构建输入复制到测试／验收产物，不覆盖受保护的 `dango_box2d.dll`；后者复核为 `6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A`。

Release 桌面与测试项目构建通过，0 warning／0 error；`git diff --check` 通过。没有新增依赖、业务端口、数据库／对象存储或远端发布操作。验收启动脚本检查候选与受保护 DLL 的 hash，并拒绝复用既有产物目录。

## 自动／native／WPF 集成

受影响回归涵盖本地房、相机、滚轮、后台查询生命周期、Home／Porch、应用宿主、帧 scheduler 及窗口预设。全部最终源码的扩展回归为 **168 passed／0 failed／2 not executed**，见 `artifacts/local-room/results/local-room-final-with-native.trx`；真实 Windows 两项另行串行执行并通过，不与焦点测试并发。

最后回归第一次使用未携带候选 native 的构建产物，28 项集成断言因 MissingLibrary／输入不存在失败，日志 `local-room-final-verified.trx` 保留。随后以明确的 `DangoPhysicsModulePath` 重新构建，并核对测试目录模块 SHA256，再取得上述最终结果；没有修改断言或以候选代替旧模块门禁。

两项 not executed 是既有前台鼠标测试：`ClosingSizeMenuReturnsKeyboardFocusToTheDangoStage(False)` 和 `RealMouseSizeSelectionRestoresTheHomeStageFocus`。测试进程未能安全保持前台，按原断言返回 Inconclusive；未改成通过。

集成测试实际验证：

- 真实 WPF 首帧、源视图暂存、唯一 owner、同一实体、隐藏暂停和 fresh-ready 恢复。
- 缩放／resize 保持 session 快照、物理地面和实体不变；返回中取消后不残留门廊 owner，再恢复能重新配置房间世界。
- 首帧超时后可重试，运行时故障不显示旧身体，显式恢复及诊断→首页可重新推进并清除房间物理侧边界。
- 原生物理连续行走 2600／3000 步到两侧、真实落地、中性轮廓与地面接触误差不超过 2 单位，最小视口内跳跃／帽子安全包络。
- 滚轮方向、原生 120 刻度、档间倍率、上下界、250 ms 余量、失焦／代际／宿主／菜单否决及晚到回调。

相机回归曾以错误实现复现两项同尺寸 Resize 导致 follow/dead-zone 重置失败；壁纸桥曾以禁用近期资格检查复现五项余量残留失败，修复后通过。旧模块专用两项测试未执行：未提供未改变的 accepted baseline 输入，不能以本次候选代替；旧 DLL 缺导出及既有发布 hash 门禁没有关闭。

## 真实 Windows 证据与修复

真实 WorkerW 往返与后台整屏窗口扫描分别 **1/1 passed**，使用 `--no-build --no-restore` 串行执行，保留真实前台、原生 HWND、父关系及几何断言：

- `real-wallpaper-shellsafe.trx`：窗口→壁纸→窗口，同 entity／session／snapshot、源保留至当前健康目标帧、实际 Hook 安装与释放、旧 HWND 销毁；实际应用前台被壁纸资格拒绝。
- `real-fullscreen-shellsafe.trx`：自建测试窗口整屏覆盖另一显示器（存在副屏时优先副屏），房窗保持真实前台；扫描明确返回已知且有整屏应用，证明没有只检查前台窗口。

初次扫描失败来自真实可见顶层辅助窗口：DWM 查询成功但 frame 为零面积，原实现把它当未知并提前停止。修复为用 `GetWindowRect` 复核：已知零面积可跳过，正面积按通常整屏分类，查询失败／反向几何仍拒绝。失败日志 `real-fullscreen-diagnostic.trx` 保留，没有保存真实外部窗口元数据。

最后审查又发现事件不合格但 20 ms 内恢复合格时旧滚轮可能被应用；应用前台／整屏应用／图标／未知四个恢复反例先实跑 4/4 failed，见 `wheel-event-qualification-red.trx`。补事件检查及应用前二次检查后，专项 **36/36 passed**，见 `wheel-event-qualification-green.trx`；同时覆盖检查中 Stop／Start、读取中代次变更、嵌套反向滚轮、嵌套 Flush 和命令完成时旧代次隔离。

相同资格条件不能只在延迟命令执行时验证。原“hook 仅取 raw、不得检查资格”是实现自行增加的限制，与产品合同不符，因此修订该架构断言。事件检查采用 20 ms 协作式截止：每个枚举回调及跨进程命中查询检查剩余量，Shell 消息等待使用剩余预算；超时返回未知。原生清理及 OS 调度不可抢占，因此不是硬实时 20 ms 承诺。observer 始终传递输入，不在 callback 修改视角，重入时保守作废旧输入；旧代次完成不清除新代次输入。真实实体鼠标操作与 callback 性能仍需人工确认。

图标命中查询的既有 `SendMessageTimeout` 超时释放路径存在资源生命周期隐患：[官方语义](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)允许函数在接收端处理尚未结束时返回。改为最多一个后台查询，用同步 `SendMessageW` 等待实际处理完成，再释放远程参数内存与 process handle；调用方仅按剩余预算等待，超时丢弃本结果，忙时不创建额外任务／分配，迟到结果不交给后续事件。前后复核 Shell／ListView 的身份和父关系，合法索引 0 不当错误，错误／清理失败为未知。cleanup 失败关闭查询 gate，避免继续分配。

受控阻塞 worker 的错误实现先 **2/2 failed**；修复后生命周期 7 项与滚轮桥 36 项合计 **43/43 passed**，见 `shell-query-lifecycle-red.trx`／`shell-query-lifecycle-green.trx`。这是受控托管生命周期证据，未将其称为真实 Shell 挂死测试。Shell 永久不回应时最多保留一个后台 worker／远程参数缓冲区并拒绝后续查询，应用退出不等待；若此时退出，远程分配可能保留至 Explorer 退出。不能提前释放接收端仍可能访问的内存。

最终 Scene-only PNG 已视觉查看：`artifacts/local-room/wallpaper-tests/8a97c293af934befaffd7faff8507ee7/room-wallpaper.png`、`room-window-restored.png` 及 `artifacts/local-room/visual/room-window.png`。画面为纯色墙、连续地面和真实身体／芽帽，足底接地；只渲染本房场景，没有捕获外部桌面。RenderTargetBitmap 不能证明用户已看见实际桌面合成效果。

## 待人工验收与交付

待确认：实体鼠标滚轮的窗口焦点／桌面 Shell 条件、图标／任务栏／菜单命中、全屏出现与消失、混合 DPI 跨屏、热插拔、实际桌面合成、视觉尺寸／软体动作和无黑屏往返。没有进行双 Windows／移动端／多人验收。未将测试模拟或进程响应当作这些人工结果。

独立验收构建／启动入口为 `eng/run-local-room-review.ps1 -RunName <fresh-name> -NativeModule <candidate-module>`。使用独立 mutex 及产物目录内 `local-room-review-state/settings.json`，不改日常安装与设置。验收路线：首页点击场景后 W → 门廊靠近普通房 W／点门 → A／D／S／空格 → 滚轮与三档菜单 → 显示方式切壁纸／托盘切回 → 房门／Esc → ×／托盘恢复。

交付保留独立人工验收窗口，启动状态记录在忽略目录 `artifacts/local-room/` 内；测试宿主会 Dispose、释放 owner／Hook／HWND。忽略目录下的测试设置、TRX、PNG 与验收产物保留，没有新业务端口或数据服务。main 合并、推送、日常安装、受保护 DLL 替换、部署和发布标签均未执行。
