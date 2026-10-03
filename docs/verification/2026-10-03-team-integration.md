# 团队整合候选验证记录

> 日期：2026-10-03
> 状态：本地整合候选；非阶段验收、非发布基线。

> 后续状态：Windows 本地收口及日常交付后，用户另行批准本地整合并合入 main、暂不推送。现已经本共享分支快进合入 main，见[本地整合记录](2026-10-03-windows-local-main-integration.md)。下文为最初整合时的证据和未合并历史快照；既有失败、跳过和阶段未验收边界不因 Git 合并关闭。

## 范围与来源

`codex/team-integration` 从本地 `main@89bacb5` 建立，分别合入：

- `codex/unified-live-home@dde2129`：已获用户本对话当前屏幕 UI 人工确认的实时首页、设置页与三档窗口尺寸，包含专项测试和[本批边界记录](2026-10-03-unified-live-home.md)。更早被退回的首页候选保留其回退历史，没有恢复旧界面。
- `codex/p2a-minimal-multiplayer-sync@5fc9704`：真实 SignalR、内存权威临时房间、客户端副本与隔离双 WPF 小样。其三条早期子分支的代码补丁已等价纳入该集成来源，没有重复合并。
- `codex/room-mechanism-design@530b111`：已批准的桌宠房间与共同房间产品/服务端规则；这是设计文档，不是房间领域代码或数据库交付。

整合仅在候选工作树完成；`main`、日常安装和来源分支没有被改写。基础外观色板、旧 Box2D 未提交实验以及房间工作树中仍标为“待审阅”的实施计划和工作流改动均未纳入。合并时保留了 README 两组文档入口，并协调阶段总览、双房间规则与 Phase 2A 同机/双机验收边界。

## 自动与同机验证

- 使用现有已核对指纹的统一原生物理模块，`dotnet build Dango.slnx -c Release` 成功，**0 警告、0 错误**；未编译或替换生产 DLL。
- 协议 6/6、服务端 24/24、客户端同步 30/30、Core 37/37、Physics 网络呈现探针 1/1 通过。客户端第一次与其他项目并行运行时为 26/30：三项缺本轮 `DANGO_SYNC_SERVER_PATH`，一项等待超时；设置本轮最终构建的 Server 路径后，独立复跑 30/30 通过。超时原因未单独定位，首次失败保留为稳定性观察项。
- Desktop 合并专项（多人、实时首页、尺寸、首页输入和 WorkerW 键盘桥）55 项中 **53 通过、2 因前台焦点条件跳过**。两项真实原生首页测试另行单独运行均通过。设置、存储、布局、应用宿主及会话范围 52/52 通过。源 UI 工作树在整合前的 Debug 专项 79 项中 75 通过、4 跳过；其带原生模块的 Debug 重跑约五分钟无最终结果，已中止该测试进程，不计为通过。
- 候选分支的隔离同机双窗口验证分别完成 native/native 与 native/fallback。两次 `verification.json` 的 `passed` 均为 `true`，`teardown.json` 的 `code` 均为 `0`，服务器和 A/B 客户端均已退出，使用过的回环监听端口无残留。证据分别保留于 `artifacts/multiplayer-sync/team-integration-native-20261003-01/` 和 `artifacts/multiplayer-sync/team-integration-fallback-20261003-01/`；这些目录被 Git 忽略，不进入提交。
- 文档冲突标记、相对链接及补丁空白检查已核对。整合后完整 Desktop/Physics 套件未取得全绿结论；旧 `dango_box2d.dll` 缺 `dango_box2d_self_bodies_create` 的历史门禁仍不得掩盖。

本次构建指定的是既有、已核对 SHA-256 的本地 `dango_physics_module.dll`。该模块不随 Git 提交；第二位成员从远端克隆后不能假定它已存在，须依[固定来源原生构建说明](../../eng/native/box2d/README.md)和[物理模块说明](../phase-1-physics-module.md)生成或取得经核验的模块，再显式传入 `DangoPhysicsModulePath`。本次没有把开发机绝对路径写入源码或配置。

## 未验证与交接边界

首页目前是本地真实团子和空门廊，尚未接入 Phase 2A 的隔离网络样本。托盘/桌面宿主在没有真实房间准入前保持门控；不能将 W 过渡、门廊或客户端可见性当作服务端授权。Phase 2A 仍使用回环连接、临时身份和内存房间；已批准的桌宠/共同房间规则尚未编码、迁移或持久化。完整 Windows UI、日常安装入口、跨屏 DPI、双 Windows M7、真实断网、受信任 LAN/WSS、正式房间权限与移动端跨端均未由本次整合证明。

本地候选可用于后续独立工作树和代码审查，但不是 `main` 的已验收阶段。未合并 `main`，未推送远端、部署或更新日常安装。要交给第二位成员，仍须明确共享分支与文件所有权，并另行授权推送。
